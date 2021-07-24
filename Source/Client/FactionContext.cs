﻿//extern alias zip;

using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Multiplayer.Client
{
    public static class FactionContext
    {
        public static Stack<Faction> stack = new Stack<Faction>();

        public static Faction Push(Faction newFaction)
        {
            if (newFaction == null || (newFaction.def != Multiplayer.FactionDef && !newFaction.def.isPlayer))
            {
                stack.Push(null);
                return null;
            }

            stack.Push(Find.FactionManager.OfPlayer);
            Set(newFaction);

            return newFaction;
        }

        public static Faction Pop()
        {
            Faction f = stack.Pop();
            if (f != null)
                Set(f);
            return f;
        }

        public static void Set(Faction newFaction)
        {
            var playerFaction = Find.FactionManager.OfPlayer;
            var factionDef = playerFaction.def;

            //Log.Message($"set faction {playerFaction}>{newFaction} {stack.Count}");

            playerFaction.def = Multiplayer.FactionDef;
            newFaction.def = factionDef;
            Find.FactionManager.ofPlayer = newFaction;
        }

        public static void Clear()
        {
            stack.Clear();
        }
    }

}
