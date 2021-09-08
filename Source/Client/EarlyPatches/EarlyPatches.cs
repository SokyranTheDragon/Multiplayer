using HarmonyLib;
using Multiplayer.Common;
using Multiplayer.Client;
using RimWorld;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Verse;
using System.IO;
using Multiplayer.Client.Patches;

namespace Multiplayer.Client
{
    [EarlyPatch]
    [HarmonyPatch]
    static class CaptureThingSetMakers
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return typeof(ThingSetMaker_MarketValue).GetConstructor(new Type[0]);
            yield return typeof(ThingSetMaker_Nutrition).GetConstructor(new Type[0]);
        }

        public static List<ThingSetMaker> captured = new();

        static void Prefix(ThingSetMaker __instance)
        {
            if (Current.ProgramState == ProgramState.Entry)
                captured.Add(__instance);
        }
    }

    [EarlyPatch]
    [HarmonyPatch(typeof(DirectXmlLoader), nameof(DirectXmlLoader.XmlAssetsInModFolder))]
    static class XmlAssetsInModFolderPatch
    {
        // Sorts the files before processing, ensures cross os compatibility
        static IEnumerable<LoadableXmlAsset> Postfix(IEnumerable<LoadableXmlAsset> __result)
        {
            var array = __result.ToArray();

            Array.Sort(array, (x, y) => StringComparer.OrdinalIgnoreCase.Compare(x.name, y.name));

            return array;
        }
    }

    [EarlyPatch]
    [HarmonyPatch(typeof(GenTypes), nameof(GenTypes.GetTypeInAnyAssemblyInt))]
    static class GenTypesGetOptimization
    {
        private static Dictionary<string, Type> RwTypes = new();

        static GenTypesGetOptimization()
        {
            foreach (var type in typeof(Game).Assembly.GetTypes())
            {
                if (type.IsPublic)
                    RwTypes[type.Name] = type;
            }
        }

        static bool Prefix(string typeName, ref Type __result)
        {
            if (!typeName.Contains(".") && RwTypes.TryGetValue(typeName, out var type))
            {
                __result = type;
                return false;
            }

            return true;
        }
    }

    [EarlyPatch]
    [HarmonyPatch(typeof(GenTypes), nameof(GenTypes.AllSubclasses))]
    static class GenTypesSubclassesOptimization
    {
        static bool Prefix(Type baseType, ref List<Type> __result)
        {
            __result = Multiplayer.subClasses.GetOrAddNew(baseType);
            return false;
        }
    }

    [EarlyPatch]
    [HarmonyPatch(typeof(GenTypes), nameof(GenTypes.AllSubclassesNonAbstract))]
    static class GenTypesSubclassesNonAbstractOptimization
    {
        static bool Prefix(Type baseType, ref List<Type> __result)
        {
            __result = Multiplayer.subClassesNonAbstract.GetOrAddNew(baseType);
            return false;
        }
    }

    [EarlyPatch]
    [HarmonyPatch(typeof(AccessTools), nameof(AccessTools.TypeByName))]
    static class AccessToolsTypeByNamePatch
    {
        static bool Prefix(string name, ref Type __result)
        {
            return !Multiplayer.typeByFullName.TryGetValue(name, out __result) && !Multiplayer.typeByName.TryGetValue(name, out __result);
        }
    }
}
