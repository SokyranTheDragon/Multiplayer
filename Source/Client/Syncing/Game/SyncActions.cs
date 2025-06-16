using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Client
{
    static class SyncActions
    {
        static SyncAction<FloatMenuOption, WorldObject, Caravan, object> SyncWorldObjCaravanMenus;
        // static SyncAction<FloatMenuOption, WorldObject, IEnumerable<IThingHolder>, CompLaunchable> SyncTransporterMenus;
        // static SyncAction<FloatMenuOption, WorldObject, IEnumerable<IThingHolder>, CompLaunchable> SyncShuttleMenus;
        // static SyncAction<FloatMenuOption, object, FloatMenuContext, object> SyncFloatMenuMakerMap;
        // static SyncAction<FloatMenuOption, object, FloatMenuContext, object> SyncFloatMenuMakerWorld;

        public static void Init()
        {
            SyncWorldObjCaravanMenus = RegisterActions((WorldObject obj, Caravan c) => obj.GetFloatMenuOptions(c), o => ref o.action);
            SyncWorldObjCaravanMenus.PatchAll(nameof(WorldObject.GetFloatMenuOptions));

            // TODO: Fix and update, look into possible patch without syncing full actions
            // TODO: Consider if this is actually needed, or would syncing CompLaunchable.TryLaunch be sufficient

            // SyncTransporterMenus = RegisterActions((WorldObject obj, IEnumerable<IThingHolder> p, CompLaunchable r) => obj.GetTransportersFloatMenuOptions(p, r), o => ref o.action);
            // SyncTransporterMenus.PatchAll(nameof(WorldObject.GetTransportersFloatMenuOptions));
            //
            // SyncShuttleMenus = RegisterActions((WorldObject obj, IEnumerable<IThingHolder> p, CompLaunchable r) => obj.GetShuttleFloatMenuOptions(p, r), o => ref o.action);
            // SyncShuttleMenus.PatchAll(nameof(WorldObject.GetShuttleFloatMenuOptions));

            // TODO: Try to see if I can get this to work, otherwise just go back to the old approach
            // TODO: Really consider reworking the SyncAction code into something that would work better with situations like those

            // SyncFloatMenuMakerMap = RegisterActionsStatic((FloatMenuContext context) =>
            // {
            //     var list = new List<FloatMenuOption>();
            //     FloatMenuMakerMap.GetProviderOptions(context, list);
            //     return list;
            // }, o => ref o.action);
            // SyncFloatMenuMakerMap.PatchAll(nameof(FloatMenuMakerMap.GetProviderOptions), nameof(SyncAction_FloatMenu_Postfix), typeof(FloatMenuMakerMap));
            // SyncFloatMenuMakerMap.context = SyncContext.QueueOrder_Down | SyncContext.MapMouseCell;

            // SyncFloatMenuMakerWorld = RegisterActionsStatic((FloatMenuContext context) =>
            // {
            //     var list = new List<FloatMenuOption>();
            //     FloatMenuMakerMap.GetProviderOptions(context, list);
            //     return list;
            // }, o => ref o.action);
            // SyncFloatMenuMakerWorld.PatchAll(nameof(FloatMenuMakerMap.GetProviderOptions), nameof(SyncAction_FloatMenu_Postfix), typeof(FloatMenuMakerMap));
            // SyncFloatMenuMakerWorld.context = SyncContext.QueueOrder_Down | SyncContext.MapMouseCell;
        }

        static SyncAction<T, object, B, object> RegisterActionsStatic<T, B>(Func<B, IEnumerable<T>> func, ActionGetter<T> actionGetter)
        {
            return RegisterActions<T, object, B, object>((_, b, _) => func(b), actionGetter);
        }

        static SyncAction<T, A, B, object> RegisterActions<T, A, B>(Func<A, B, IEnumerable<T>> func, ActionGetter<T> actionGetter)
        {
            return RegisterActions<T, A, B, object>((a, b, _) => func(a, b), actionGetter);
        }

        static SyncAction<T, A, B, C> RegisterActions<T, A, B, C>(Func<A, B, C, IEnumerable<T>> func, ActionGetter<T> actionGetter)
        {
            var sync = new SyncAction<T, A, B, C>(func, actionGetter);
            Sync.handlers.Add(sync);

            return sync;
        }

        public static Dictionary<MethodBase, ISyncAction> syncActions = new();
        public static bool wantOriginal;
        private static bool syncingActions; // Prevents from running on base methods

        public static void SyncAction_Prefix(ref bool __state)
        {
            __state = syncingActions;
            syncingActions = true;
        }

        public static void SyncAction1_Postfix(object __instance, object __0, ref object __result, MethodBase __originalMethod, bool __state)
        {
            SyncAction2_Postfix(__instance, __0, null, ref __result, __originalMethod, __state);
        }

        public static void SyncAction2_Postfix(object __instance, object __0, object __1, ref object __result, MethodBase __originalMethod, bool __state)
        {
            if (!__state)
            {
                syncingActions = false;
                if (Multiplayer.ShouldSync && !wantOriginal && !syncingActions)
                    __result = syncActions[__originalMethod].DoSync(__instance, __0, __1);
            }
        }

        public static void SyncAction_FloatMenu_Postfix(object __0, ref object __1, MethodBase __originalMethod, bool __state)
        {
            SyncAction2_Postfix(null, __0, null, ref __1, __originalMethod, __state);
        }
    }

}
