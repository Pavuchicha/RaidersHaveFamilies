using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;
using Verse.AI;

namespace RaidersHaveFamilies
{
    // One temporary assembly point before an unavoidable firing lane. Not a
    // leader, a global ThinkTree patch, or a second routine job issuer.
    internal static class NJRRescueRallyR033
    {
        private sealed class State
        {
            internal Thing Threat;
            internal IntVec3 Anchor=IntVec3.Invalid;
            internal bool Gathering, Released;
            internal int FirstReady=-1;
            internal int LastRallyProgress, LastReadyCount;
            internal NJRRallyFieldR037 Field;
            internal readonly Dictionary<Pawn,int> PathBest=new Dictionary<Pawn,int>();
            internal readonly Dictionary<Pawn,IntVec3> Slots=new Dictionary<Pawn,IntVec3>();
            internal readonly Dictionary<Pawn,int> BestDistance=new Dictionary<Pawn,int>();
            internal readonly Dictionary<Pawn,int> LastApproach=new Dictionary<Pawn,int>();
            internal readonly HashSet<int> Notes=new HashSet<int>();
        }
        private static readonly ConditionalWeakTable<RHFOperation,State> States=
            new ConditionalWeakTable<RHFOperation,State>();
        private sealed class Incoming {internal int Tick;}
        private static readonly ConditionalWeakTable<Pawn,Incoming> IncomingFire=
            new ConditionalWeakTable<Pawn,Incoming>();
        internal static void ObserveIncomingR037(Pawn pawn,int now)
        {if(pawn!=null)IncomingFire.GetValue(pawn,_=>new Incoming()).Tick=now;}
        private static bool RecentlyTargeted(Pawn pawn,int now)=>
            IncomingFire.TryGetValue(pawn,out Incoming shot) && now>=shot.Tick && now-shot.Tick<300;
        internal static bool IsGathering(RHFOperation op) => op!=null && op.Phase==1 &&
            States.TryGetValue(op,out State s) && s.Gathering;
        internal static IntVec3 Anchor(RHFOperation op) => op!=null && States.TryGetValue(op,out State s)?s.Anchor:IntVec3.Invalid;
        internal static void StopGathering(RHFOperation op)
        {
            if(op!=null && States.TryGetValue(op,out State s))s.Gathering=false;
        }
        // These are ordinary mission decisions, not engine exceptions. Keep
        // the information in both Player.log and the test recorder without
        // asking the game's auto-open error console to handle routine recovery.
        internal static void NoteOnce(RHFOperation op,string message,int key)
        {
            if(op==null)return;
            if(States.GetValue(op,_=>new State()).Notes.Add(key))Log.Message(message);
        }
        private static bool Active(Pawn p,Map map) => p!=null && p.Spawned &&
            p.Map==map && !p.Dead && !p.Downed && !p.InMentalState;
        private static bool Safe(Map map,IntVec3 cell) => cell.InBounds(map) &&
            cell.Standable(map) && cell.GetEdifice(map)==null &&
            !RHFObjectiveRouteV128.StagingExposedToTurret(map,cell);
        private static IntVec3 Slot(Pawn pawn,Map map,IntVec3 anchor,HashSet<IntVec3> used)
        {
            var cells=new List<IntVec3>();
            for(int x=-5;x<=5;x++)for(int z=-5;z<=5;z++)
            {
                if(x*x+z*z>25)continue;
                IntVec3 c=anchor+new IntVec3(x,0,z);
                if(used.Contains(c) || !Safe(map,c))continue;
                Pawn occupant=c.GetFirstPawn(map);
                if(occupant!=null && occupant!=pawn)continue;
                if(!RHFObjectiveRouteV128.FormationLegSafe(anchor,c,map,false))continue;
                cells.Add(c);
            }
            cells.Sort((a,b)=>((pawn.Position-a).LengthHorizontalSquared+
                (anchor-a).LengthHorizontalSquared*2).CompareTo(
                (pawn.Position-b).LengthHorizontalSquared+(anchor-b).LengthHorizontalSquared*2));
            int probes=0;
            foreach(IntVec3 c in cells)
            {
                if(++probes>12)break;
                if(pawn.CanReach(c,PathEndMode.OnCell,Danger.Deadly))return c;
            }
            return IntVec3.Invalid;
        }
        internal static bool TryGather(RHFOperation op,Map map,RHFTacticalBoardS41 board,
            RHFRoutePlanV128 route,Thing turret,HashSet<Pawn> reserved,int now)
        {
            State s=States.GetValue(op,_=>new State());
            if(NJRRescueFirebreakR034.IsActive(op)){s.Gathering=false;return false;}
            if(route==null || !route.Valid || turret==null || turret.Destroyed ||
               route.FirstTurretExposureIndex<0 || (route.FirstBarrierIndex>=0 &&
               route.FirstBarrierIndex<route.FirstTurretExposureIndex))
            {s.Gathering=false;if(route==null || !route.SearchPending){s.Threat=null;s.Released=false;}return false;}
            if(s.Threat!=turret)
            {s.Threat=turret;s.Released=false;s.Anchor=IntVec3.Invalid;s.FirstReady=-1;s.Slots.Clear();s.BestDistance.Clear();s.LastApproach.Clear();s.PathBest.Clear();s.Field=null;s.LastRallyProgress=now;s.LastReadyCount=0;}
            if(s.Released){s.Gathering=false;return false;}
            var fighters=new List<Pawn>();
            foreach(Pawn p in op.Squad)if(Active(p,map) && !reserved.Contains(p))fighters.Add(p);
            if(fighters.Count<2){s.Gathering=false;return false;}
            if(!s.Anchor.IsValid || !Safe(map,s.Anchor))
            {
                s.Anchor=IntVec3.Invalid;
                // Stand back from the first exposed tile, not on the firing
                // line itself. A new route from the current positions must
                // not continuously move an already valid assembly point.
                int first=route.FirstTurretExposureIndex;
                for(int i=Math.Max(0,first-6);i>=0;i--)
                    if(i<first && Safe(map,route.Cells[i])){s.Anchor=route.Cells[i];break;}
                s.Slots.Clear();s.BestDistance.Clear();s.LastApproach.Clear();s.PathBest.Clear();s.FirstReady=-1;
                s.Field=null;s.LastRallyProgress=now;s.LastReadyCount=0;
                if(!s.Anchor.IsValid)
                {s.Gathering=false;s.Released=true;return false;} // already under fire: fight, not wait
                Log.Message("[NJR][R033][Rally] gather at "+s.Anchor+" before "+turret.LabelShortCap+
                    " members="+fighters.Count);
            }
            bool wasGathering=s.Gathering;
            s.Gathering=true;
            if(!wasGathering)s.LastRallyProgress=now;
            // A route replanned from the frontmost pawn no longer contains the
            // approach of stragglers. Keep a separate, resumable reverse field
            // rooted at the FIXED rally point; use actual passable terrain.
            if(s.Field!=null && now-s.Field.Created>=1800 && !s.Field.NeedsExpansion(fighters))
                s.Field=null;
            if(s.Field!=null)s.Field.ExpandFor(fighters);
            var used=new HashSet<IntVec3>();
            int ready=0, exposed=0,incoming=0;bool laggardProgress=false;
            foreach(Pawn p in fighters)
            {
                bool actualFire=RecentlyTargeted(p,now);
                if(actualFire)incoming++;
                bool underFire=actualFire || RHFObjectiveRouteV128.StagingExposedToTurret(map,p.Position);
                if(underFire)exposed++;
                int distance=(p.Position-s.Anchor).LengthHorizontalSquared;
                if(!s.BestDistance.TryGetValue(p,out int best) || distance<best)
                {s.BestDistance[p]=distance;s.LastApproach[p]=now;s.LastRallyProgress=now;}
                if(s.Field!=null && s.Field.TryDistance(p.Position,out int remaining) &&
                   (!s.PathBest.TryGetValue(p,out int oldPath) || remaining<oldPath))
                {s.PathBest[p]=remaining;s.LastApproach[p]=now;s.LastRallyProgress=now;}
                if(distance<=36 && !underFire &&
                   !RHFCombatPlannerV125.IsCombatEmergencyJob(p))ready++;
                else if(now-s.LastApproach[p]<600)laggardProgress=true;
            }
            if(ready>s.LastReadyCount)s.LastRallyProgress=now;
            s.LastReadyCount=ready;
            if(ready>0 && s.FirstReady<0)s.FirstReady=now;
            // Wait for the whole mobile wing. A genuinely immobile/suppressed
            // straggler may not hold a majority forever. Never trickle a
            // lone fighter forward after a timeout.
            bool quorum=ready>=2 && ready*3>=fighters.Count*2;
            bool timed=s.FirstReady>=0 && now-s.FirstReady>=1800 && !laggardProgress;
            if(ready==fighters.Count || (timed && quorum) || exposed==fighters.Count)
            {
                s.Gathering=false;s.Released=true;
                Log.Message("[NJR][R033][Rally] release ready="+ready+"/"+fighters.Count+
                    " timed="+timed+" exposed="+exposed);
                return false;
            }
            // No majority: never manufacture an attack quorum or wait for
            // 27,000 ticks in a firing lane. A running bounded field may
            // finish its search, but cannot hold a stationary squad forever.
            int stalled=now-s.LastRallyProgress;
            if((stalled>=900 && incoming>0 && !laggardProgress && !quorum) ||
               (stalled>=1800 && !laggardProgress && (s.Field==null || !s.Field.NeedsExpansion(fighters))) ||
               stalled>=3600 || now-Math.Max(op.LastProgressTick,op.PhaseStartTick)>27000)
            {
                s.Gathering=false;op.Phase=2;op.PhaseStartTick=now;
                board.Decision="R037 rally failed; organized withdrawal";
                Log.Message("[NJR][R037][Rally] no approach progress; withdrawal ready="+ready+"/"+fighters.Count+
                    " stalled="+stalled+" incoming="+incoming+" field="+(s.Field?.Count??0));
                return true;
            }
            board.Threat=turret;board.Breacher=null;
            // Retain all valid local slots before allocating new movement.
            foreach(Pawn p in fighters)
            {
                if(s.Slots.TryGetValue(p,out IntVec3 c) && Safe(map,c) &&
                   (c-s.Anchor).LengthHorizontalSquared<=25 && !used.Contains(c) &&
                   (c.GetFirstPawn(map)==null || c.GetFirstPawn(map)==p))used.Add(c);
                else s.Slots.Remove(p);
            }
            foreach(Pawn p in fighters)
            {
                if((p.Position-s.Anchor).LengthHorizontalSquared>144 || s.Slots.ContainsKey(p))continue;
                IntVec3 cell=Slot(p,map,s.Anchor,used);
                if(cell.IsValid){s.Slots[p]=cell;used.Add(cell);}
            }
            foreach(Pawn p in fighters)
            {
                IntVec3 cell=IntVec3.Invalid;
                if(s.Slots.TryGetValue(p,out IntVec3 slot) &&
                   RHFObjectiveRouteV128.FormationLegSafe(p.Position,slot,map,false))cell=slot;
                if(!cell.IsValid)
                {
                    // Keep a safe forward leg when still valid; avoid a 90t
                    // start/stop loop for injured and slow members.
                    Job running=p.jobs?.curJob;
                    IntVec3 dest=running?.def==JobDefOf.Goto?running.targetA.Cell:IntVec3.Invalid;
                    if(wasGathering && dest.IsValid && dest!=p.Position && !used.Contains(dest) &&
                       Safe(map,dest) && ((s.Field!=null && s.Field.Descends(p.Position,dest)) ||
                           (dest-s.Anchor).LengthHorizontalSquared<(p.Position-s.Anchor).LengthHorizontalSquared) &&
                       RHFObjectiveRouteV128.FormationLegSafe(p.Position,dest,map,false))cell=dest;
                    else
                    {
                        // The independent approach takes precedence once it
                        // knows this pawn's cell. Old centerline joins remain
                        // useful during the bounded field search from afar.
                        if(s.Field!=null)cell=s.Field.Waypoint(p,used);
                        if(!cell.IsValid)cell=RHFObjectiveRouteV128.RallyWaypointR033(route,p,map,used,s.Anchor);
                        if(!cell.IsValid && (p.Position-s.Anchor).LengthHorizontalSquared>36)
                        {
                            if(s.Field==null)
                            {
                                s.Field=new NJRRallyFieldR037(map,s.Anchor,now);
                                s.Field.ExpandFor(fighters);
                                Log.Message("[NJR][R037][Rally] independent approach field at "+s.Anchor+
                                    " for "+p.LabelShortCap+" from "+p.Position);
                            }
                            cell=s.Field.Waypoint(p,used);
                        }
                    }
                    if(cell.IsValid)used.Add(cell);
                }
                if(cell.IsValid && cell!=p.Position)board.Assign(p,RHFRoleS41.Advance,null,cell,now);
                else board.Assign(p,RHFRoleS41.Hold,null,p.Position,now);
                Job current=p.jobs?.curJob;
                // Only cancel stale movement when it disagrees with the safe
                // assembly order. Never cancel CE suppression/reload or fire.
                if(current!=null && !RHFCombatPlannerV125.IsCombatEmergencyJob(p) &&
                   (current.def==JobDefOf.Goto && current.targetA.Cell!=cell ||
                    current.def==JobDefOf.Wait && cell.IsValid && cell!=p.Position))
                    p.jobs.EndCurrentJob(JobCondition.InterruptForced,false);
            }
            board.Decision="R033 rally "+s.Anchor+" ready="+ready+"/"+fighters.Count+
                " field="+(s.Field?.Count??0)+" pending="+(s.Field!=null && s.Field.NeedsExpansion(fighters))+" stalled="+(now-s.LastRallyProgress);
            return true;
        }
    }
}
