// RHF S2.2: one bounded pre-contact assembly, not a recurring 90-tick regroup.
// The rescue commander remains the only issuer of jobs; this class provides
// slots and a timed/quorum release signal, never commandeers pawns or Lords.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;
using Verse.AI;

namespace RaidersHaveFamilies
{
    internal sealed class RHFAssemblyStateV139
    {
        internal Pawn Captive;
        internal bool Started;
        internal bool Finished;
        internal int StartTick;
        internal readonly Dictionary<int,IntVec3> Slots=new Dictionary<int,IntVec3>();
        internal string Decision="нет контакта";
    }

    internal static class RHFStagedAdvanceV139
    {
        private static readonly ConditionalWeakTable<RHFOperation,RHFAssemblyStateV139> States=
            new ConditionalWeakTable<RHFOperation,RHFAssemblyStateV139>();
        private const int AssemblyDeadline=2400;

        // Threat-aware spacing: the pre-contact assembly must be outside the
        // weapons' practical range, not a fixed twelve cells before a turret.
        private static float ContactRange(Thing target)
        {
            if(target is Pawn pawn)
                return Math.Max(9f,RHFCombatPlannerV125.WeaponRange(pawn)+3f);
            if(target is Building turret)
            {
                float range=30f;
                List<VerbProperties> verbs=turret.def?.building?.turretGunDef?.Verbs;
                if(verbs!=null)
                    foreach(VerbProperties verb in verbs)
                        if(verb!=null && verb.range>range)range=verb.range;
                return Math.Min(75f,range)+3f;
            }
            return 18f;
        }

        internal static string Diagnostic(RHFOperation op)
        {
            if(op==null || !States.TryGetValue(op,out RHFAssemblyStateV139 state))
                return "предконтактное построение не требовалось";
            return "предконтактное построение: "+
                (state.Finished?"завершено":state.Started?"сбор идёт":"не начато")+
                "; позиций="+state.Slots.Count+"; "+state.Decision;
        }

        internal static bool Holding(RHFOperation op,Map map,RHFRoutePlanV128 route,
            Pawn leader,Pawn captive,HashSet<Pawn> covering,int now,
            out Dictionary<int,IntVec3> slots)
        {
            slots=null;
            if(op?.Squad==null || route==null || !route.Valid ||
               route.Cells.Count<22 || map==null || leader==null || captive==null ||
               op.Squad.Count<5)return false;
            RHFAssemblyStateV139 state=States.GetValue(op,_=>new RHFAssemblyStateV139());
            if(state.Captive!=captive)
            {
                state.Captive=captive;state.Started=false;state.Finished=false;
                state.Slots.Clear();
            }
            if(state.Finished)return false;
            if(!state.Started)
            {
                // Do not order a counter-march while the leader is already
                // pinned down by CE or executing an emergency cover job.
                if(RHFCombatPlannerV125.IsCombatEmergencyJob(leader))return false;
                // Contact may be a colonist, a player mech/animal combatant,
                // or a working turret. Restrict this scan to visible hostile
                // targets; never stage against the protected captive.
                Thing contact=null;
                int best=int.MaxValue;
                int checkedPawns=0;
                foreach(Pawn defender in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
                {
                    if(defender==null || defender.Dead || defender.Downed ||
                       !defender.Spawned || defender==captive ||
                       (op.Targets!=null && op.Targets.Contains(defender)) ||
                       (op.Freed!=null && op.Freed.Contains(defender)) ||
                       !GenHostility.HostileTo(leader,defender))continue;
                    int distance=(leader.Position-defender.Position).LengthHorizontalSquared;
                    if(distance<196 || distance>1600 || distance>=best ||
                       !GenSight.LineOfSight(leader.Position,defender.Position,map))continue;
                    contact=defender;best=distance;
                }
                foreach(Building turret in map.listerBuildings.allBuildingsColonist)
                {
                    // Do not ignore defenses appearing late in the registry.
                    if(!RHFCombatPlannerV125.IsTurret(turret))continue;
                    int distance=(leader.Position-turret.Position).LengthHorizontalSquared;
                    if(distance<196 || distance>3600 || distance>=best ||
                       !GenSight.LineOfSight(leader.Position,turret.Position,map))continue;
                    contact=turret;best=distance;
                }
                if(contact==null)return false;
                // Never order a counter-march or staging under direct turret fire.
                if(RHFObjectiveRouteV128.StagingExposedToTurret(map,leader.Position))
                    return false;
                float contactRange=ContactRange(contact);
                int nearest=-1;
                int distanceToContact=int.MaxValue;
                int upto=route.Cells.Count-1;
                for(int i=2;i<=upto;i++)
                {
                    int d=(route.Cells[i]-contact.Position).LengthHorizontalSquared;
                    if(d<distanceToContact){distanceToContact=d;nearest=i;}
                }
                int corridorAllowance=contact is Building ? 3600 : 625;
                if(nearest<15 || distanceToContact>corridorAllowance ||
                   (route.FirstBarrierIndex>=0 && route.FirstBarrierIndex<nearest-7))
                {state.Finished=true;return false;}
                int contactBuffer=Math.Max(12,(int)Math.Ceiling(contactRange)+6);
                int anchorIndex=Math.Max(4,nearest-contactBuffer);
                int safeLimit=RHFObjectiveRouteV128.SafeAdvanceLimit(route);
                if(anchorIndex>=safeLimit || anchorIndex>=route.Cells.Count-1)
                {state.Finished=true;return false;}
                IntVec3 anchor=route.Cells[anchorIndex];
                IntVec3 previous=route.Cells[anchorIndex-3];
                IntVec3 advance=anchor-previous;
                bool alongX=Math.Abs(advance.x)>=Math.Abs(advance.z);
                IntVec3 forward=alongX ?
                    (advance.x>=0?IntVec3.East:IntVec3.West) :
                    (advance.z>=0?IntVec3.North:IntVec3.South);
                IntVec3 side=alongX?IntVec3.North:IntVec3.East;
                var reserved=new HashSet<IntVec3>();
                int eligible=0,assigned=0;
                for(int i=0;i<op.Squad.Count;i++)
                {
                    Pawn soldier=op.Squad[i];
                    if(soldier==null || !soldier.Spawned || soldier.Dead ||
                       soldier.Downed || soldier.Map!=map ||
                       covering?.Contains(soldier)==true)continue;
                    eligible++;
                    int slot=assigned;
                    int column=slot%5-2;
                    int rank=slot/5;
                    IntVec3 candidate=anchor+side*(column*2)-forward*(rank*3);
                    float enemyRange=contactRange;
                    if(!candidate.InBounds(map) || !candidate.Standable(map) ||
                       reserved.Contains(candidate) ||
                       RHFObjectiveRouteV128.StagingExposedToTurret(map,candidate) ||
                       ((candidate-contact.Position).LengthHorizontalSquared<=
                           enemyRange*enemyRange &&
                        GenSight.LineOfSight(contact.Position,candidate,map)) ||
                       !soldier.CanReach(candidate,PathEndMode.OnCell,Danger.Deadly))
                        continue;
                    reserved.Add(candidate);
                    state.Slots[soldier.thingIDNumber]=candidate;
                    assigned++;
                }
                if(assigned<Math.Max(3,eligible/2))
                {state.Finished=true;state.Slots.Clear();return false;}
                state.Started=true;state.StartTick=now;
                state.Decision="сбор в разреженный фронт "+assigned+
                    " человек; контакт "+contact.LabelCap;
                Log.Message("[RHF][FormationS22] "+state.Decision+
                    "; anchor="+anchor+"; timeout="+AssemblyDeadline);
            }
            int ready=0,live=0;
            foreach(Pawn p in op.Squad)
            {
                if(p==null || p.Map!=map || p.Dead || p.Downed ||
                   !state.Slots.TryGetValue(p.thingIDNumber,out IntVec3 cell))continue;
                live++;
                if((p.Position-cell).LengthHorizontalSquared<=4)ready++;
            }
            if(live==0 || ready>=Math.Max(3,(live*3+3)/4) ||
               now-state.StartTick>=AssemblyDeadline)
            {
                state.Finished=true;
                Log.Message("[RHF][FormationS22] Assault released together; "+
                    ready+"/"+live+" in formation; elapsed="+(now-state.StartTick));
                return false;
            }
            slots=state.Slots;
            return true;
        }
    }
}
