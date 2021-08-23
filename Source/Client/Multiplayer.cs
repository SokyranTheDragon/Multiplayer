using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Xml;

using HarmonyLib;

using RimWorld;
using UnityEngine;
using Verse;

using Multiplayer.Common;
using System.Runtime.CompilerServices;
using Multiplayer.Client.Desyncs;
using Multiplayer.Client.Patches;

namespace Multiplayer.Client
{
    public class Multiplayer : Mod
    {
        public static Harmony harmony = new Harmony("multiplayer");
        public static MpSettings settings;

        public static MultiplayerGame game;
        public static MultiplayerSession session;

        public static IConnection Client => session?.client;
        public static MultiplayerServer LocalServer => session?.localServer;
        public static PacketLogWindow WriterLog => session?.writerLog;
        public static PacketLogWindow ReaderLog => session?.readerLog;
        public static bool IsReplay => session?.replay ?? false;

        public static string username;

        public static bool reloading;

        public static IdBlock GlobalIdBlock => game.worldComp.globalIdBlock;
        public static MultiplayerWorldComp WorldComp => game.worldComp;

        public static bool ShowDevInfo => Prefs.DevMode && settings.showDevInfo;

        public static Faction RealPlayerFaction
        {
            get => Client != null ? game.RealPlayerFaction : Faction.OfPlayer;
            set => game.RealPlayerFaction = value;
        }

        public static bool ExecutingCmds => MultiplayerWorldComp.executingCmdWorld || AsyncTimeComp.executingCmdMap != null;
        public static bool Ticking => MultiplayerWorldComp.tickingWorld || AsyncTimeComp.tickingMap != null || ConstantTicker.ticking;
        public static Map MapContext => AsyncTimeComp.tickingMap ?? AsyncTimeComp.executingCmdMap;

        public static bool dontSync;
        public static bool ShouldSync => InInterface && !dontSync;
        public static bool InInterface => Client != null && !Ticking && !ExecutingCmds && !reloading && Current.ProgramState == ProgramState.Playing && LongEventHandler.currentEvent == null;

        public static string ReplaysDir => GenFilePaths.FolderUnderSaveData("MpReplays");
        public static string DesyncsDir => GenFilePaths.FolderUnderSaveData("MpDesyncs");

        public static Stopwatch clock = Stopwatch.StartNew();

        public static HashSet<string> xmlMods = new HashSet<string>();
        public static List<ModHashes> enabledModAssemblyHashes = new List<ModHashes>();
        public static Dictionary<string, DefInfo> localDefInfos;

        public static bool arbiterInstance;
        public static bool hasLoaded;
        public static bool loadingErrors;
        public static Stopwatch harmonyWatch = new Stopwatch();

        public Multiplayer(ModContentPack pack) : base(pack)
        {
            Native.EarlyInit();
            DisableOmitFramePointer();

            if (GenCommandLine.CommandLineArgPassed("profiler"))
            {
                SimpleProfiler.CheckAvailable();
                Log.Message($"Profiler: {SimpleProfiler.available}");
                SimpleProfiler.Init("prof");
            }

            if (GenCommandLine.CommandLineArgPassed("arbiter"))
            {
                ArbiterWindowFix.Run();

                arbiterInstance = true;
            }

            SyncDict.Init();

            EarlyPatches();
            CheckInterfaceVersions();

            settings = GetSettings<MpSettings>();

            LongEventHandler.ExecuteWhenFinished(() => {
                // Double Execute ensures it'll run last.
                LongEventHandler.ExecuteWhenFinished(LatePatches);
            });

#if DEBUG
            Application.logMessageReceivedThreaded -= Log.Notify_MessageReceivedThreadedInternal;
#endif
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void DisableOmitFramePointer()
        {
            Native.mini_parse_debug_option("disable_omit_fp");
        }

        private void EarlyPatches()
        {
            Assembly.GetCallingAssembly().GetTypes().Do(type => {
                if (type.Namespace != null && type.Namespace.EndsWith("EarlyPatches"))
                    harmony.CreateClassProcessor(type).Patch();
            });

            // Might fix some mod desyncs
            harmony.PatchMeasure(
                AccessTools.Constructor(typeof(Def), new Type[0]),
                new HarmonyMethod(typeof(RandPatches), nameof(RandPatches.Prefix)),
                new HarmonyMethod(typeof(RandPatches), nameof(RandPatches.Postfix))
            );

            harmony.PatchMeasure(
                AccessTools.PropertyGetter(typeof(Rand), nameof(Rand.Int)),
                postfix: new HarmonyMethod(typeof(DeferredStackTracing), nameof(DeferredStackTracing.Postfix))
            );

            harmony.PatchMeasure(
                AccessTools.PropertyGetter(typeof(Rand), nameof(Rand.Value)),
                postfix: new HarmonyMethod(typeof(DeferredStackTracing), nameof(DeferredStackTracing.Postfix))
            );

#if DEBUG
            DebugPatches.Init();
#endif
        }

        private void LatePatches()
        {
            // optimization, cache DescendantThingDefs
            harmony.PatchMeasure(
                AccessTools.Method(typeof(ThingCategoryDef), "get_DescendantThingDefs"),
                new HarmonyMethod(typeof(ThingCategoryDef_DescendantThingDefsPatch), "Prefix"),
                new HarmonyMethod(typeof(ThingCategoryDef_DescendantThingDefsPatch), "Postfix")
            );

            // optimization, cache ThisAndChildCategoryDefs
            harmony.PatchMeasure(
                AccessTools.Method(typeof(ThingCategoryDef), "get_ThisAndChildCategoryDefs"),
                new HarmonyMethod(typeof(ThingCategoryDef_ThisAndChildCategoryDefsPatch), "Prefix"),
                new HarmonyMethod(typeof(ThingCategoryDef_ThisAndChildCategoryDefsPatch), "Postfix")
            );

            if (MpVersion.IsDebug) {
                Log.Message("== Structure == \n" + SyncDict.syncWorkers.PrintStructure());
            }
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            settings.DoSettingsWindowContents(inRect);
        }

        public override string SettingsCategory() => "Multiplayer";

        static void CheckInterfaceVersions()
        {
            var mpAssembly = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Multiplayer");
            var curVersion = new Version(
                (mpAssembly.GetCustomAttributes(typeof(AssemblyFileVersionAttribute), false)[0] as AssemblyFileVersionAttribute).Version
            );

            Log.Message($"Current MultiplayerAPI version: {curVersion}");

            foreach (var mod in LoadedModManager.RunningMods) {
                if (mod.assemblies.loadedAssemblies.NullOrEmpty())
                    continue;

                if (mod.Name == "Multiplayer")
                    continue;

                // Test if mod is using multiplayer api
                if (!mod.assemblies.loadedAssemblies.Any(a => a.GetName().Name == MpVersion.ApiAssemblyName)) {
                    continue;
                }

                // Retrieve the original dll
                var info = mod.ModAssemblies()
                    .Select(f => FileVersionInfo.GetVersionInfo(f.FullName))
                    .FirstOrDefault(v => v.ProductName == "Multiplayer");

                if (info == null) {
                    // There are certain mods that don't include the API, namely compat
                    // Can we test them?
                    continue;
                }

                var version = new Version(info.FileVersion);

                Log.Message($"Mod {mod.Name} has MultiplayerAPI client ({version})");

                if (curVersion > version)
                    Log.Warning($"Mod {mod.Name} uses an older API version (mod: {version}, current: {curVersion})");
                else if (curVersion < version)
                    Log.Error($"Mod {mod.Name} uses a newer API version! (mod: {version}, current: {curVersion})\nMake sure the Multiplayer mod is up to date");
            }
        }

        public static void StopMultiplayer()
        {
            if (session != null)
            {
                session.Stop();
                session = null;
                Prefs.Apply();
            }

            game?.OnDestroy();
            game = null;

            TickPatch.Reset();

            Find.WindowStack?.WindowOfType<ServerBrowser>()?.Cleanup(true);

            foreach (var entry in SyncFieldUtil.bufferedChanges)
                entry.Value.Clear();

            if (arbiterInstance)
            {
                arbiterInstance = false;
                Application.Quit();
            }
        }
    }
}
