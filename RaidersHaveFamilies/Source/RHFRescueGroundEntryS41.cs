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

        // This is a LOCAL eight-tile flood, not hundreds of expensive native
        // CanReach calls for EACH map-edge candidate. All deployment cells
        // must belong to the same genuinely walkable outdoor pocket.
        private static List<IntVec3> ConnectedCluster(Map map,IntVec3 center)
        {
            var cells=new List<IntVec3>(180);
            if(!SafeArrivalCell(map,center))return cells;
            var seen=new HashSet<IntVec3>();
            var queue=new Queue<IntVec3>();
            queue.Enqueue(center);seen.Add(center);
            while(queue.Count>0)
            {
                IntVec3 here=queue.Dequeue();
                cells.Add(here);
                for(int direction=0;direction<4;direction++)
                {
                    IntVec3 next=here+(direction==0 ? new IntVec3(1,0,0) :
                        direction==1 ? new IntVec3(-1,0,0) :
                        direction==2 ? new IntVec3(0,0,1) : new IntVec3(0,0,-1));
                    int dx=next.x-center.x,dz=next.z-center.z;
                    if(dx*dx+dz*dz>64 || !next.InBounds(map) ||
                       seen.Contains(next) || !SafeArrivalCell(map,next))continue;
                    seen.Add(next);queue.Enqueue(next);
                }
            }
            return cells;
        }

        internal static bool GroundEntryClusterSafe(Map map, IntVec3 center)
        {
            if(!SafeArrivalCell(map,center))return false;
            int total=0;
            for(int dx=-8;dx<=8;dx++)for(int dz=-8;dz<=8;dz++)
            {
                if(dx*dx+dz*dz>64)continue;
                IntVec3 c=center+new IntVec3(dx,0,dz);
                if(!c.InBounds(map))continue;
                total++;
                RoofDef roof=map.roofGrid.RoofAt(c);
                if(roof!=null && roof.isThickRoof)return false;
                // Never scatter the first four ranks into water, caves, or
                // rock even when the outer half of the formation is valid.
                if(dx*dx+dz*dz<=16 && !SafeArrivalCell(map,c))return false;
            }
            if(total<45)return false;
            int connected=ConnectedCluster(map,center).Count;
            return connected>=24 && connected*5>=total*4;
        }

        // Probe both near- and medium-range outdoor approaches. An enclosed
        // prison does not need to be walkable from the edge before a breach;
        // at least an EXTERIOR region near the objective must be reachable.
        // Each probe is real vanilla reachability, no geometric teleport.
        internal static IntVec3 ReachableApproach(Map map,IntVec3 from,Pawn prisoner)
        {
            if(map==null || prisoner==null || !from.IsValid ||
               !from.InBounds(map))return IntVec3.Invalid;
            int maxRadius=Math.Min(64,Math.Max(map.Size.x,map.Size.z)/3);
            int[] sampleRadii={1,2,3,4,5,6,8,10,12,16,20,24,32,40,48,56,64};
            for(int i=0;i<sampleRadii.Length;i++)
            {
                int radius=sampleRadii[i];
                if(radius>maxRadius)break;
                for(int direction=0;direction<8;direction++)
                {
                    int dx=direction==0||direction==4?0:
                        direction==1||direction==2||direction==3?radius:-radius;
                    int dz=direction==2||direction==6?0:
                        direction==3||direction==4||direction==5?radius:-radius;
                    IntVec3 outside=prisoner.Position+new IntVec3(dx,0,dz);
                    if(!SafeArrivalCell(map,outside))continue;
                    if(map.reachability.CanReach(from,outside,PathEndMode.OnCell,
                        TraverseMode.PassDoors,Danger.Deadly))return outside;
                }
            }
            return IntVec3.Invalid;
        }

        private static bool ReachableExterior(Map map,IntVec3 edge,Pawn captive)
        {return ReachableApproach(map,edge,captive).IsValid;}

        private struct EntryCandidate
        {
            internal IntVec3 Edge,Center;
            internal int Distance,Score,Danger,Side;
            internal EntryCandidate(IntVec3 edge,IntVec3 center,int distance,int side)
            {Edge=edge;Center=center;Distance=distance;Score=distance;Danger=0;Side=side;}
        }
        // Evaluate a few of the CLOSEST cells on EACH side, not every
        // single edge cell with hundreds of native reachability tests.
        private static void InsertCandidate(List<EntryCandidate> list,
            EntryCandidate item,int limit)
        {
            int i=0;
            while(i<list.Count && list[i].Distance<=item.Distance)i++;
            if(i>=limit)return;
            list.Insert(i,item);
            if(list.Count>limit)list.RemoveAt(limit);
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
            var candidateEdges=new List<EntryCandidate>(384);
            for(int side=0;side<4;side++)
            {
                var nearSide=new List<EntryCandidate>(96);
                int span=side<2 ? map.Size.z : map.Size.x;
                for(int t=5;t<span-5;t++)
                {
                    IntVec3 edge=side==0 ? new IntVec3(0,0,t) :
                        side==1 ? new IntVec3(map.Size.x-1,0,t) :
                        side==2 ? new IntVec3(t,0,0) :
                                  new IntVec3(t,0,map.Size.z-1);
                    IntVec3 center=edge+(side==0 ? new IntVec3(6,0,0) :
                        side==1 ? new IntVec3(-6,0,0) :
                        side==2 ? new IntVec3(0,0,6) :
                                  new IntVec3(0,0,-6));
                    if(!SafeArrivalCell(map,edge) ||
                       !SafeArrivalCell(map,center))continue;
                    InsertCandidate(nearSide,new EntryCandidate(edge,center,
                        (center-captive.Position).LengthHorizontalSquared,side),96);
                }
                candidateEdges.AddRange(nearSide);
            }
            var valid=new List<EntryCandidate>(32);
            int[] validPerSide=new int[4];
            foreach(EntryCandidate proposed in candidateEdges)
            {
                if(validPerSide[proposed.Side]>=8)continue;
                IntVec3 edge=proposed.Edge,center=proposed.Center;
                if(!GroundEntryClusterSafe(map,center))continue;
                if(!map.reachability.CanReach(edge,center,PathEndMode.OnCell,
                    TraverseMode.PassDoors,Danger.Deadly))continue;
                if(!ReachableExterior(map,center,captive))continue;
                int danger=0;
                for(int depth=0;depth<=8;depth+=4)
                {
                    // Score threatening guns at the ACTUAL proposed center;
                    // never alter the raid target to get more spawn variance.
                    IntVec3 sample=center+new IntVec3(depth==0?0:
                        Math.Sign(captive.Position.x-center.x)*depth,0,
                        depth==0?0:Math.Sign(captive.Position.z-center.z)*depth);
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
                EntryCandidate evaluated=proposed;
                evaluated.Danger=danger;
                evaluated.Score=danger/4+proposed.Distance;
                valid.Add(evaluated);
                validPerSide[proposed.Side]++;
            }
            if(valid.Count==0)return IntVec3.Invalid;
            valid.Sort((a,b)=>a.Score.CompareTo(b.Score));
            // An actual raid must not reuse a single hardcoded best cell on
            // every test. Randomize ONLY between comparably safe/near entries.
            int eligible=1;
            int tolerance=Math.Max(2500,valid[0].Score/4);
            while(eligible<valid.Count && eligible<8 &&
                valid[eligible].Danger<=valid[0].Danger &&
                valid[eligible].Score-valid[0].Score<=tolerance)
                eligible++;
            int choice=eligible>1 ? Rand.Range(0,eligible) : 0;
            IntVec3 selected=valid[choice].Center;
            Log.Message("[NJR][EntryR023] candidates="+valid.Count+
                " comparable="+eligible+" chosen="+selected+
                " score="+valid[choice].Score+" danger="+valid[choice].Danger);
            return selected;
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
            // The local connected flood also prevents scattering onto a
            // separate island behind a one-tile wall or impassable terrain.
            result.AddRange(ConnectedCluster(map,center));
            result.Sort((a,b)=>
                (a-center).LengthHorizontalSquared.CompareTo(
                    (b-center).LengthHorizontalSquared));
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
            // The native combat group is now fully known but has not arrived.
            // Fill missing members BEFORE native spawn, letters and Lord creation.
            if(parms?.target is Map incidentMap)
            {
                RHFOperation mission=RHFMissionIntegrityV130.ActiveRaidLetterOperation;
                RHFStrikeParty wing=RHFOperationController.ActiveRaidParty(incidentMap,mission);
                NJRForceStrengthR017.CompleteGeneratedRoster(mission,wing,pawns);
            }
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
