using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;
using Verse.AI;

namespace RaidersHaveFamilies
{
    // Phase 1: a small contact wing, all other accessible members work on the pad.
    // Recompute at the strategy cadence, not in per-pawn ThinkTree ticks.
    internal static class NJREarlyWingR053
    {
        private sealed class State
        {
            internal readonly HashSet<Pawn> Assault=new HashSet<Pawn>();
            internal readonly HashSet<Pawn> Engineering=new HashSet<Pawn>();
            internal int Tick=-1;
        }
        private static readonly ConditionalWeakTable<RHFOperation,State> Wings=new ConditionalWeakTable<RHFOperation,State>();
        internal static bool Enabled(RHFOperation op)=>op.Phase==1 && op.TryPod && op.FortifyPrison && op.PodConstructionStage>=1 && op.PodConstructionStage<=3;
        internal static bool IsAssault(RHFOperation op,Pawn p)=>Enabled(op) && Wings.TryGetValue(op,out State state) && state.Assault.Contains(p);
        internal static bool IsBuilderWing(RHFOperation op,Pawn p)=>Enabled(op) && Wings.TryGetValue(op,out State state) && state.Engineering.Contains(p);
        internal static void Reserve(RHFOperation op,Map map,Pawn captive,HashSet<Pawn> reserved,int now)
        {
            if(!Enabled(op) || captive==null)return;
            State state=Wings.GetValue(op,_=>new State());
            if(state.Tick<0 || now-state.Tick>=90 || !ExistsActive(state.Assault,map))
            {
                var old=new HashSet<Pawn>(state.Assault);state.Assault.Clear();state.Tick=now;
                var available=new List<Pawn>();var contacts=new HashSet<Thing>();
                Room prison=captive.Position.GetRoom(map);
                if(prison!=null && !prison.PsychologicallyOutdoors)
                {
                    foreach(Pawn enemy in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
                        if(enemy!=null && enemy.Spawned && !enemy.Dead && !enemy.Downed && !enemy.IsPrisonerOfColony && enemy.Position.GetRoom(map)==prison)contacts.Add(enemy);
                    foreach(Building gun in map.listerBuildings.allBuildingsColonist)
                        if(RHFCombatPlannerV125.IsTurret(gun) && gun.Position.GetRoom(map)==prison)contacts.Add(gun);
                }
                foreach(Pawn p in op.Squad)
                {
                    if(!RHFRescuePlannerS41.ActiveForRoleS41(p,map) || reserved.Contains(p))continue;
                    available.Add(p);
                    Thing threat=NJRCombatContactS44.VisibleContact(p,op,captive.Position,18);
                    if(threat!=null && (threat.Position-captive.Position).LengthHorizontalSquared<=144)contacts.Add(threat);
                }
                Thing routeThreat=op.BoardS41?.Threat;
                if(routeThreat!=null && routeThreat.Spawned && !routeThreat.Destroyed)contacts.Add(routeThreat);
                // A defended route gets a combat wing; an undefended prison needs one operator.
                int needed=contacts.Count==0?1:Math.Min(available.Count,Math.Max(2,contacts.Count+1));
                var ranks=new Dictionary<Pawn,int>();foreach(Pawn p in available)ranks[p]=Rank(op,p,captive,old);
                available.Sort((a,b)=>ranks[a].CompareTo(ranks[b]));
                foreach(Pawn p in available)
                {
                    if(state.Assault.Count>=needed)break;
                    state.Assault.Add(p);
                    // A real new combat allocation may interrupt obsolete engineering,
                    // but never a CE emergency or a protected prisoner escort.
                    Job current=p.jobs?.curJob;RHFOrderS41 prior=op.BoardS41?.For(p,false);
                    if(!old.Contains(p) && prior!=null && (prior.Role==(int)RHFRoleS41.Engineer || prior.Role==(int)RHFRoleS41.PodSupply) &&
                       current!=null && !RHFCombatPlannerV125.IsCombatEmergencyJob(p))
                        p.jobs.EndCurrentJob(JobCondition.InterruptForced,false);
                }
                if(!old.SetEquals(state.Assault)){if(op.BoardS41!=null)op.BoardS41.NextReplanTick=0;Log.Message("[NJR][R053][Wing] assault="+state.Assault.Count+" engineering candidates="+(available.Count-state.Assault.Count)+" contacts="+contacts.Count);}
            }
            // A nearby kit carrier unloads once BEFORE leaving to breach. Do not
            // let the assault planner overwrite this physical job in the same pulse.
            foreach(Pawn p in state.Assault)
                if(RHFRescuePlannerS41.ActiveForRoleS41(p,map) && p.jobs!=null &&
                   now-op.EarlyPadProbeTickR052<=240 && RHFPodSupplyS41.HasCargo(op,p) &&
                   op.BoardS41?.For(p,false)?.Role==(int)RHFRoleS41.PodSupply &&
                   !RHFCombatPlannerV125.IsCombatEmergencyJob(p) && NJRCombatContactS44.VisibleContact(p,op,p.Position,10)==null)reserved.Add(p);
            state.Engineering.Clear();
            foreach(Pawn p in op.Squad)
                if(RHFRescuePlannerS41.ActiveForRoleS41(p,map) && !state.Assault.Contains(p) && !reserved.Contains(p) &&
                   (p.CanReach(op.PodLauncherCell,PathEndMode.Touch,Danger.Deadly) || p.CanReach(op.PodCell,PathEndMode.Touch,Danger.Deadly))){reserved.Add(p);state.Engineering.Add(p);}
        }
        private static bool ExistsActive(HashSet<Pawn> pawns,Map map)
        {foreach(Pawn p in pawns)if(RHFRescuePlannerS41.ActiveForRoleS41(p,map))return true;return false;}
        private static int Rank(RHFOperation op,Pawn p,Pawn captive,HashSet<Pawn> old)
        {
            int score=(p.Position-captive.Position).LengthHorizontalSquared;
            if(p==op.BoardS41?.Breacher)score-=20000;
            if(old.Contains(p))score-=2000;
            if(p.CanReach(captive,PathEndMode.Touch,Danger.Deadly))score-=10000;
            if(RHFTactical.IsBreachAxe(p))score-=500;
            if(p.WorkTagIsDisabled(WorkTags.Violent))score+=30000;
            if(RHFPodSupplyS41.HasCargo(op,p))score+=400; // unload before separation when safe
            return score;
        }
    }
}
