// using HarmonyLib;
// using RimWorld.Planet;
// using System;
// using System.Collections.Generic;
// using System.Reflection;
// using Verse;
//
// // TODO: Look into actually making those work in 1.6
// namespace Multiplayer.Client
// {
//
//     [HarmonyPatch(typeof(MapDrawer), nameof(MapDrawer.RegenerateEverythingNow))]
//     public static class MapDrawerRegenPatch
//     {
//         public static Dictionary<int, MapDrawer> copyFrom = new();
//
//         // TODO: FieldRefs for speed
//         // These are readonly so they need to be set using reflection
//         private static FieldInfo mapDrawerMap = AccessTools.Field(typeof(MapDrawer), nameof(MapDrawer.map));
//         private static FieldInfo sectionMap = AccessTools.Field(typeof(Section), nameof(Section.map));
//
//         static bool Prefix(MapDrawer __instance)
//         {
//             Map map = __instance.map;
//             if (!copyFrom.TryGetValue(map.uniqueID, out MapDrawer keepDrawer)) return true;
//
//             map.mapDrawer = keepDrawer;
//             mapDrawerMap.SetValue(keepDrawer, map);
//
//             foreach (Section section in keepDrawer.sections)
//             {
//                 sectionMap.SetValue(section, map);
//
//                 for (int i = 0; i < section.layers.Count; i++)
//                 {
//                     SectionLayer layer = section.layers[i];
//
//                     if (!ShouldKeep(layer))
//                         section.layers[i] = (SectionLayer)Activator.CreateInstance(layer.GetType(), section);
//                     else if (layer is SectionLayer_TerrainScatter scatter)
//                         scatter.scats.Do(s => s.map = map);
//                 }
//             }
//
//             foreach (Section s in keepDrawer.sections)
//                 foreach (SectionLayer layer in s.layers)
//                     if (!ShouldKeep(layer))
//                         layer.Regenerate();
//
//             copyFrom.Remove(map.uniqueID);
//
//             return false;
//         }
//
//         static bool ShouldKeep(SectionLayer layer)
//         {
//             return layer.GetType().Assembly == typeof(Game).Assembly;
//         }
//     }
//
//     [HarmonyPatch(typeof(WorldGrid), MethodType.Constructor)]
//     public static class WorldGridCachePatch
//     {
//         public static WorldGrid copyFrom;
//
//         static bool Prefix(WorldGrid __instance, ref int ___cachedTraversalDistance, ref int ___cachedTraversalDistanceForStart, ref int ___cachedTraversalDistanceForEnd)
//         {
//             if (copyFrom == null) return true;
//
//             WorldGrid grid = __instance;
//
//             // TODO: Look for something similar for other layers
//
//             grid.surfaceViewAngle = copyFrom.surfaceViewAngle;
//             grid.surfaceViewCenter = copyFrom.surfaceViewCenter;
//             Log.Error($"Grid surface null: {grid.surface == null}");
//             // grid.surface.verts = copyFrom.surface.verts;
//             // grid.surface.tileIDToNeighbors_offsets = copyFrom.surface.tileIDToNeighbors_offsets;
//             // grid.surface.tileIDToNeighbors_values = copyFrom.surface.tileIDToNeighbors_values;
//             // grid.surface.tileIDToVerts_offsets = copyFrom.surface.tileIDToVerts_offsets;
//             // grid.surface.averageTileSize = copyFrom.surface.averageTileSize;
//
//             // TODO: Check if the following line of code will work in 1.6
//             Log.Error($"Surface tiles count: {grid.surface?.tiles}");
//             // grid.surface.tiles.Clear();
//             ___cachedTraversalDistance = -1;
//             ___cachedTraversalDistanceForStart = -1;
//             ___cachedTraversalDistanceForEnd = -1;
//
//             Log.Error($"Global layers count: {grid.globalLayers.Count}");
//             // grid.globalLayers.Clear();
//             // grid.globalLayers.AddRange(copyFrom.globalLayers);
//
//             Log.Error($"Planet layer count: {grid.planetLayers.Count}, copyFrom: {grid.planetLayers.Count}");
//
//             copyFrom = null;
//
//             return false;
//         }
//     }
//
//     // TODO: Make this work with 1.6
//     [HarmonyPatch(typeof(WorldGrid), nameof(WorldGrid.ExposeData))]
//     public static class WorldGridExposeDataPatch
//     {
//         public static WorldGrid copyFrom;
//
//         static bool Prefix(WorldGrid __instance)
//         {
//             if (copyFrom == null) return true;
//
//             WorldGrid grid = __instance;
//
//             // TODO: Look into doing something similar for other layers
//
//             Log.Error($"Grid surface null: {grid.surface == null}");
//             // grid.surface.tileBiome = copyFrom.surface.tileBiome;
//             // grid.surface.tileElevation = copyFrom.surface.tileElevation;
//             // grid.surface.tileHilliness = copyFrom.surface.tileHilliness;
//             // grid.surface.tileTemperature = copyFrom.surface.tileTemperature;
//             // grid.surface.tileRainfall = copyFrom.surface.tileRainfall;
//             // grid.surface.tileSwampiness = copyFrom.surface.tileSwampiness;
//             // grid.surface.tileFeature = copyFrom.surface.tileFeature;
//             // grid.surface.tileRoadOrigins = copyFrom.surface.tileRoadOrigins;
//             // grid.surface.tileRoadAdjacency = copyFrom.surface.tileRoadAdjacency;
//             // grid.surface.tileRoadDef = copyFrom.surface.tileRoadDef;
//             // grid.surface.tileRiverOrigins = copyFrom.surface.tileRiverOrigins;
//             // grid.surface.tileRiverAdjacency = copyFrom.surface.tileRiverAdjacency;
//             // grid.surface.tileRiverDef = copyFrom.surface.tileRiverDef;
//
//             // This is plain old data apart from the WorldFeature feature field which is a reference
//             // It later gets reset in WorldFeatures.ExposeData though so it can be safely copied
//             // TODO: Check if this will work in 1.6
//             Log.Error($"Surface tiles count: {grid.surface.tiles}");
//             // grid.surface.tiles.Clear();
//             // grid.surface.tiles.AddRange(copyFrom.surface.tiles);
//             // grid.surface.tiles = copyFrom.surface.tiles;
//
//             Log.Error($"Global layers count: {grid.globalLayers.Count}");
//             // grid.globalLayers.Clear();
//             // grid.globalLayers.AddRange(copyFrom.globalLayers);
//
//             Log.Error($"Planet layer count: {grid.planetLayers.Count}, copyFrom: {grid.planetLayers.Count}");
//
//             // ExposeData runs multiple times but WorldGrid only needs LoadSaveMode.LoadingVars
//             copyFrom = null;
//
//             return false;
//         }
//     }
//
//     // TODO: Will likely use one of the previous 2 methods, check if true and delete if not needed.
//     [HarmonyPatch(typeof(WorldRenderer), MethodType.Constructor)]
//     public static class WorldRendererCachePatch
//     {
//         public static WorldRenderer copyFrom;
//
//         static bool Prefix(WorldRenderer __instance)
//         {
//             if (copyFrom == null) return true;
//
//             __instance.layers = copyFrom.layers;
//             copyFrom = null;
//
//             return false;
//         }
//     }
// }
