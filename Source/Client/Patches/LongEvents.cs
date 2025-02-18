using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.Common;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client.Patches
{
    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.QueueLongEvent), typeof(Action), typeof(string), typeof(bool), typeof(Action<Exception>), typeof(bool), typeof(Action))]
    static class MarkLongEvents
    {
        private static MethodInfo MarkerMethod = AccessTools.Method(typeof(MarkLongEvents), nameof(Marker));

        static void Prefix(ref Action action, string textKey)
        {
            if (Multiplayer.Client is { State: ConnectionStateEnum.ClientPlaying } && (Multiplayer.Ticking || Multiplayer.ExecutingCmds || textKey == "MpSaving"))
            {
                action += Marker;
            }
        }

        private static void Marker()
        {
        }

        public static bool IsTickMarked(Action action)
        {
            return action?.GetInvocationList()?.Any(d => d.Method == MarkerMethod) ?? false;
        }
    }

    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.LongEventsUpdate))]
    static class NewLongEvent
    {
        public static bool currentEventWasMarked;

        static void Prefix(ref bool __state)
        {
            __state = LongEventHandler.currentEvent == null;
            currentEventWasMarked = MarkLongEvents.IsTickMarked(LongEventHandler.currentEvent?.eventAction);
        }

        static void Postfix(bool __state)
        {
            currentEventWasMarked = false;

            if (Multiplayer.Client == null) return;

            if (__state && MarkLongEvents.IsTickMarked(LongEventHandler.currentEvent?.eventAction))
                Multiplayer.Client.Send(Packets.Client_Freeze, new object[] { true });
        }
    }

    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.ExecuteToExecuteWhenFinished))]
    static class LongEventEnd
    {
        static void Postfix()
        {
            if (Multiplayer.Client != null && NewLongEvent.currentEventWasMarked)
                Multiplayer.Client.Send(Packets.Client_Freeze, new object[] { false });
        }
    }

    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.QueueLongEvent), new[] { typeof(Action), typeof(string), typeof(bool), typeof(Action<Exception>), typeof(bool), typeof(Action) })]
    static class LongEventAlwaysSync
    {
        static void Prefix(ref bool doAsynchronously)
        {
            if (Multiplayer.ExecutingCmds)
                doAsynchronously = false;
        }
    }

    // [HarmonyPatch(typeof(SectionLayer_BuildingsDamage), nameof(SectionLayer_BuildingsDamage.PrintScratches))]
    // [HarmonyPatch(typeof(SectionLayer_BuildingsDamage), nameof(SectionLayer_BuildingsDamage.PrintCornersAndEdges))]
    [HarmonyPatch(typeof(MapDrawer), nameof(MapDrawer.RegenerateEverythingNow))]
    [HarmonyPatch(typeof(SettleInEmptyTileUtility), nameof(SettleInEmptyTileUtility.Settle))]
    static class Testing1
    {
        // static void Prefix(Building b)
        static void Prefix()
        {
            LongEventTest.logRand = false;
            // Log.Error($"MP: {Multiplayer.Client != null}, Main thread: {UnityData.IsInMainThread}, building: {b}, tick: {Find.TickManager?.TicksGame ?? -1}, RNG seed: {Rand.seed}, RNG iterations: {Rand.iterations}, RNG state: {Rand.StateCompressed}");
            Log.Error($"MP: {Multiplayer.Client != null}, Main thread: {UnityData.IsInMainThread}, tick: {Find.TickManager?.TicksGame ?? -1}, RNG seed: {Rand.seed}, RNG iterations: {Rand.iterations}, RNG state: {Rand.StateCompressed}, stack: {(Rand.stateStack.Count > 0 ? Rand.stateStack.Peek() : 0)}");
        }
    }

    [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateMap))]
    static class LongEventTest
    {
        public static bool logRand = false;

        static void Prefix(MapParent parent, bool isPocketMap)
        {
            Log.Error($"Pre-Pre long event, MP: {Multiplayer.Client != null}, Main thread: {UnityData.IsInMainThread}, tick: {Find.TickManager?.TicksGame ?? -1}, RNG seed: {Rand.seed}, RNG iterations: {Rand.iterations}, RNG state: {Rand.StateCompressed}, stack: {(Rand.stateStack.Count > 0 ? Rand.stateStack.Peek() : 0)}");
        }

        static void Postfix(MapParent parent, bool isPocketMap)
        {
            // logRand = true;
            Log.Error($"Pre long event, MP: {Multiplayer.Client != null}, Main thread: {UnityData.IsInMainThread}, tick: {Find.TickManager?.TicksGame ?? -1}, RNG seed: {Rand.seed}, RNG iterations: {Rand.iterations}, RNG state: {Rand.StateCompressed}, stack: {(Rand.stateStack.Count > 0 ? Rand.stateStack.Peek() : 0)}");
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                Log.Error($"Post long event, MP: {Multiplayer.Client != null}, Main thread: {UnityData.IsInMainThread}, tick: {Find.TickManager?.TicksGame ?? -1}, RNG seed: {Rand.seed}, RNG iterations: {Rand.iterations}, RNG state: {Rand.StateCompressed}, stack: {(Rand.stateStack.Count > 0 ? Rand.stateStack.Peek() : 0)}");
            });
        }
    }

    [HarmonyPatch(typeof(Rand), nameof(Rand.PushState), [])]
    static class LogRandTest1
    {
        static void Prefix()
        {
            if (LongEventTest.logRand)
                Log.Error($"Pushed state.");
        }
    }

    [HarmonyPatch(typeof(Rand), nameof(Rand.PushState), [typeof(int)])]
    static class LogRandTest2
    {
        static void Prefix(int replacementSeed)
        {
            if (LongEventTest.logRand)
                Log.Error($"Pushed state with {replacementSeed}.");
        }
    }

    [HarmonyPatch(typeof(Rand), nameof(Rand.Seed), MethodType.Setter)]
    static class LogRandTest3
    {
        static void Prefix(int __0)
        {
            if (LongEventTest.logRand)
                Log.Error($"Set seed with {__0}.");
        }
    }

    [HarmonyPatch(typeof(Rand), nameof(Rand.PopState))]
    static class LogRandTest44
    {
        static void Prefix()
        {
            if (LongEventTest.logRand)
                Log.Error($"Pre Popped state, MP: {Multiplayer.Client != null}, interface: {Multiplayer.InInterface}, Main thread: {UnityData.IsInMainThread}, tick: {Find.TickManager?.TicksGame ?? -1}, RNG seed: {Rand.seed}, RNG iterations: {Rand.iterations}, RNG state: {Rand.StateCompressed}, stack: {(Rand.stateStack.Count > 0 ? Rand.stateStack.Peek() : 0)}");
        }

        static void Postfix()
        {
            if (LongEventTest.logRand)
                Log.Error($"Post Popped state, MP: {Multiplayer.Client != null}, interface: {Multiplayer.InInterface}, Main thread: {UnityData.IsInMainThread}, tick: {Find.TickManager?.TicksGame ?? -1}, RNG seed: {Rand.seed}, RNG iterations: {Rand.iterations}, RNG state: {Rand.StateCompressed}, stack: {(Rand.stateStack.Count > 0 ? Rand.stateStack.Peek() : 0)}");
        }
    }
}
