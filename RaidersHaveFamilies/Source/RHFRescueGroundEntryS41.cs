// NJR R016: Ground rescue entry must be physically connected to the prison
// approaches and must never place any member in roofed/fogged/unsafe terrain.
// All checks run only at a rescue incident, never in passive colony ticks.
using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RaidersHaveFamilies
{
    internal static class RHFRescueGroundEntryS41
    {
        // Vanilla EdgeWalkIn.RandomClosewalkCellNear uses a standability predicate,
        // not a roof/fog/water validator. Never use it unmodified for NJR rescue.
        internal static bool SafeArrivalCell(Map map, IntVec3 cell)
        {
            return map != null && cell.IsValid && cell.InBounds(map) &&
                cell.Standable(map) && cell.GetEdifice(map) == null &&
                !cell.Roofed(map) && !cell.Fogged(map) &&
                cell.GetTerrain(map) != null && !cell.GetTerrain(map).IsWater;
        }

        internal static bool GroundEntryClusterSafe(Map map, IntVec3 center)
        {
            if(!SafeArrivalCell(map,center)) return false;
            int clear=0, total=0;
            for(int dx=-8;dx<=8;dx++)
            for(int dz=-8;dz<=8;dz++)
            {
                if(dx*dx+dz*dz>64)continue;
                IntVec3 c=center+new IntVec3(dx,0,dz);
                if(!c.InBounds(map))continue;
                total++;
                RoofDef roof=map.roofGrid.RoofAt(c);
                // Unseen thick-roof caves should disqualify this whole staging
                // area; scattering onto one of their walkable floors is unsafe.
                if(roof!=null && roof.isThickRoof)return false;
                bool pass=SafeArrivalCell(map,c);
                if(dx*dx+dz*dz<=16 && !pass)return false;
                if(!pass)continue;
                if(!map.reachability.CanReach(center,c,PathEndMode.OnCell,
                    TraverseMode.PassDoors,Danger.Deadly))continue;
                clear++;
            }
            // On a map edge a portion of the 8-cell disc is out of bounds.
            // Count only in-bounds cells; reserve enough REAL deployment space.
            return total>=45 && clear>=24 && clear*5>=total*4;
        }

        // A nearby real approach, not an arbitrary outdoor tile on the far
        // side of a mountain. The pre-R016 scan expanded to the WHOLE map,
        // making every disconnected mountain perimeter look "reachable".
        internal static IntVec3 ReachableApproach(Map map,IntVec3 from,Pawn prisoner)
        {
            if(map==null || prisoner==null || !from.IsValid ||
               !from.InBounds(map))return IntVec3.Invalid;
            // The objective is a nearby accessible OUTSIDE approach, not an
            // arbitrary other location on the map; prisoners may be enclosed
            // by walls/locked doors that the team will breach later.
            int maxRadius=Math.Min(64,Math.Max(map.Size.x,map.Size.z)/3);
            for(int radius=1;radius<=maxRadius;radius++)
            for(int dx=-radius;dx<=radius;dx++)
            for(int dz=-radius;dz<=radius;dz++)
            {
                if(Math.Max(Math.Abs(dx),Math.Abs(dz))!=radius)continue;
                IntVec3 outside=prisoner.Position+new IntVec3(dx,0,dz);
                if(!SafeArrivalCell(map,outside))continue;
                if(map.reachability.CanReach(from,outside,PathEndMode.OnCell,
                    TraverseMode.PassDoors,Danger.Deadly))return outside;
            }
            return IntVec3.Invalid;
        }

        private static bool ReachableExterior(Map map,IntVec3 edge,Pawn prisoner)
        {
            return ReachableApproach(map,edge,prisoner).IsValid;
        }

        internal static IntVec3 Choose(Map map,RHFOperation op)
        {
            if(map==null || op?.Targets==null)return IntVec3.Invalid;
            Pawn captive=op.Vip;
            if(captive==null || !captive.Spawned || captive.Map!=map)
            {
                captive=null;
                for(int i=0;i<op.Targets.Count;i++)
                    if(op.Targets[i]!=null && op.Targets[i].Map==map &&
                       op.Targets[i].Spawned && !op.Targets[i].Dead)
                    {captive=op.Targets[i];break;}
            }
            if(captive==null)return IntVec3.Invalid;
            IntVec3 best=IntVec3.Invalid;
            int bestScore=int.MaxValue;
            for(int side=0;side<4;side++)
            {
                int span=side<2 ? map.Size.z : map.Size.x;
                for(int t=5;t<span-5;t++)
                {
                    IntVec3 edge=side==0 ? new IntVec3(0,0,t) :
                        side==1 ? new IntVec3(map.Size.x-1,0,t) :
                        side==2 ? new IntVec3(t,0,0) :
                                  new IntVec3(t,0,map.Size.z-1);
                    // Place the formation six cells INSIDE the map. Vanilla
                    // scattering around an exact boundary center previously
                    // produced precarious shoreline/mountain insertions.
                    IntVec3 center=edge+(side==0 ? new IntVec3(6,0,0) :
                        side==1 ? new IntVec3(-6,0,0) :
                        side==2 ? new IntVec3(0,0,6) :
                                  new IntVec3(0,0,-6));
                    if(!SafeArrivalCell(map,edge) ||
                       !GroundEntryClusterSafe(map,center))continue;
                    if(!map.reachability.CanReach(edge,center,PathEndMode.OnCell,
                        TraverseMode.PassDoors,Danger.Deadly))continue;
                    if(!ReachableExterior(map,center,captive))continue;
                    int danger=0;
                    // Evaluate actual defenders at this staging area. Avoid
                    // ordinal list limits on turrets or pawn defenders.
                    for(int depth=0;depth<=8;depth+=4)
                    {
                        IntVec3 sample=center+(side==0 ? new IntVec3(depth,0,0) :
                            side==1 ? new IntVec3(-depth,0,0) :
                            side==2 ? new IntVec3(0,0,depth) :
                                      new IntVec3(0,0,-depth));
                        if(!sample.InBounds(map))break;
                        foreach(Building turret in map.listerBuildings.allBuildingsColonist)
                        {
                            if(!RHFCombatPlannerV125.IsTurret(turret))continue;
                            if((sample-turret.Position).LengthHorizontalSquared<=1600 &&
                               GenSight.LineOfSight(turret.Position,sample,map))
                                danger+=12000;
                        }
                        foreach(Pawn defender in map.mapPawns.FreeColonistsSpawned)
                        {
                            if(defender==null || defender.Dead || defender.Downed ||
                               defender.equipment?.Primary?.def?.IsRangedWeapon!=true)continue;
                            float range=RHFCombatPlannerV125.WeaponRange(defender);
                            if(range<2f)continue;
                            if((sample-defender.Position).LengthHorizontalSquared<=range*range &&
                               GenSight.LineOfSight(defender.Position,sample,map))danger+=3000;
                        }
                    }
                    int cost=danger/4+(center-captive.Position).LengthHorizontalSquared;
                    if(cost<bestScore){best=center;bestScore=cost;}
                }
            }
            return best;
        }

        internal static bool IsGroundRescueIncident(IncidentParms parms)
        {
            RHFOperation op=RHFMissionIntegrityV130.ActiveRaidLetterOperation;
            return op!=null && !op.Finished &&
                op.Type==(int)RHFOperationType.Rescue &&
                parms!=null && parms.target is Map &&
                parms.raidArrivalMode==PawnsArrivalModeDefOf.EdgeWalkIn;
        }

        internal static List<IntVec3> DeploymentCells(Map map,IntVec3 center)
        {
            var result=new List<IntVec3>();
            if(!GroundEntryClusterSafe(map,center))return result;
            for(int radius=0;radius<=8;radius++)
            for(int dx=-radius;dx<=radius;dx++)
            for(int dz=-radius;dz<=radius;dz++)
            {
                if(dx*dx+dz*dz>64 || Math.Max(Math.Abs(dx),Math.Abs(dz))!=radius)continue;
                IntVec3 c=center+new IntVec3(dx,0,dz);
                if(!SafeArrivalCell(map,c))continue;
                if(!map.reachability.CanReach(center,c,PathEndMode.OnCell,
                    TraverseMode.PassDoors,Danger.Deadly))continue;
                result.Add(c);
            }
            return result;
        }
    }

    // The native worker otherwise trusts an attackTargets override and may
    // replace the safe entry after NJR already approved it. Validate the
    // *final* center inside the native incident dispatch, before pawn generation.
    [HarmonyPatch(typeof(PawnsArrivalModeWorker_EdgeWalkIn),
        nameof(PawnsArrivalModeWorker_EdgeWalkIn.TryResolveRaidSpawnCenter))]
    internal static class NJRGroundRescueCenterPatchR016
    {
        [HarmonyPrefix]
        private static bool Prefix(IncidentParms parms,ref bool __result)
        {
            if(!RHFRescueGroundEntryS41.IsGroundRescueIncident(parms))return true;
            Map map=(Map)parms.target;
            if(!RHFRescueGroundEntryS41.GroundEntryClusterSafe(map,parms.spawnCenter))
            {
                parms.spawnCenter=RHFRescueGroundEntryS41.Choose(map,
                    RHFMissionIntegrityV130.ActiveRaidLetterOperation);
            }
            __result=parms.spawnCenter.IsValid &&
                RHFRescueGroundEntryS41.GroundEntryClusterSafe(map,parms.spawnCenter);
            if(__result)
                parms.spawnRotation=Rot4.FromAngleFlat(
                    (map.Center-parms.spawnCenter).AngleFlat);
            else
                Log.Error("[NJR][GroundEntryR016] Refused unsafe native ground " +
                    "spawn center. No pawns placed inside mountain/water/fog.");
            return false;
        }
    }

    // Place every member on an explicitly validated ground tile. Vanilla's
    // RandomClosewalkCellNear checks only Standable and can pick cave/water
    // cells within eight tiles even if the chosen spawnCenter is safe.
    [HarmonyPatch(typeof(PawnsArrivalModeWorker_EdgeWalkIn),
        nameof(PawnsArrivalModeWorker_EdgeWalkIn.Arrive))]
    internal static class NJRGroundRescueArrivalPatchR016
    {
        [HarmonyPrefix]
        private static bool Prefix(List<Pawn> pawns,IncidentParms parms)
        {
            if(!RHFRescueGroundEntryS41.IsGroundRescueIncident(parms))return true;
            Map map=(Map)parms.target;
            List<IntVec3> cells=RHFRescueGroundEntryS41.DeploymentCells(map,parms.spawnCenter);
            // This invariant was already checked in TryResolveRaidSpawnCenter.
            // Never allow an unsafe vanilla fallback if another mod changed the
            // map during the same incident. Re-evaluate before placement.
            if(cells.Count==0)
            {
                Log.Error("[NJR][GroundEntryR016] No safe placement cells after " +
                    "raid creation; refusing random cave placement.");
                return false;
            }
            for(int i=0;i<pawns.Count;i++)
            {
                Pawn pawn=pawns[i];
                if(pawn==null)continue;
                IntVec3 loc=cells[i % cells.Count];
                GenSpawn.Spawn(pawn,loc,map,parms.spawnRotation);
            }
            Log.Message("[NJR][GroundEntryR016] Spawned " + pawns.Count +
                " ground rescuers at " + parms.spawnCenter +
                "; verified exterior cells=" + cells.Count + ".");
            return false;
        }
    }
}
