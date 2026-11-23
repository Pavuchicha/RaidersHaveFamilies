// S4.1-A/O: grounded, exhaustive insertion planner for RHF ground rescue only.
// Preserves vanilla EdgeWalkIn and real pathfinding. No teleport or ghost spawn.
using System;
using RimWorld;
using Verse;
using Verse.AI;

namespace RaidersHaveFamilies
{
    internal static class RHFRescueGroundEntryS41
    {
        // Vanilla EdgeWalkIn scatters every soldier up to eight cells from
        // spawnCenter. Checking ONLY the chosen center allowed arrival inside
        // mountain caves. Require a broad, naturally open, connected staging
        // area with a mountain-free 3-tile inner radius.
        internal static bool GroundEntryClusterSafe(Map map,IntVec3 center)
        {
            if(map==null || !center.IsValid || !center.InBounds(map) ||
               !center.Standable(map) || center.GetEdifice(map)!=null ||
               center.Roofed(map))return false;
            int open=0,total=0;
            for(int dx=-8;dx<=8;dx++)
            for(int dz=-8;dz<=8;dz++)
            {
                if(dx*dx+dz*dz>64)continue;
                IntVec3 c=center+new IntVec3(dx,0,dz);
                if(!c.InBounds(map))continue;
                total++;
                RoofDef roof=map.roofGrid.RoofAt(c);
                if(roof!=null && roof.isThickRoof)return false;
                // Fully inspect the native spawn scatter area; never omit a
                // narrow rock passage merely because it falls between samples.
                bool clear=c.Standable(map) && c.GetEdifice(map)==null && roof==null;
                if(clear)open++;
                if(dx*dx+dz*dz<=16 && !clear)return false;
            }
            if(total<24 || open*5<total*4)return false;
            // Check all real deployment cells, not eight lucky samples: the
            // group must not scatter into isolated rock pockets or mountain.
            for(int dx=-8;dx<=8;dx++)
            for(int dz=-8;dz<=8;dz++)
            {
                if(dx*dx+dz*dz>64)continue;
                IntVec3 c=center+new IntVec3(dx,0,dz);
                if(!c.InBounds(map) || !c.Standable(map) ||
                   c.GetEdifice(map)!=null || c.Roofed(map))continue;
                if(!map.reachability.CanReach(center,c,PathEndMode.OnCell,
                    TraverseMode.PassDoors,Danger.Deadly))return false;
            }
            return true;
        }

        // A path that destroys every obstacle is not a feasible entrance to a
        // mountain base. Find a walkable exterior staging tile near the cell.
        // PassDoors permits ordinary doors, but NEVER routes through rock/walls.
        private static bool ReachableExterior(Map map,IntVec3 edge,Pawn prisoner)
        {
            // Inspect every possible accessible exterior near the prison,
            // progressing until the physical map boundary; no sample cap.
            int maxRadius=Math.Max(map.Size.x,map.Size.z);
            for(int radius=1;radius<=maxRadius;radius++)
            for(int dx=-radius;dx<=radius;dx++)
            for(int dz=-radius;dz<=radius;dz++)
            {
                if(Math.Max(Math.Abs(dx),Math.Abs(dz))!=radius)continue;
                IntVec3 outside=prisoner.Position+new IntVec3(dx,0,dz);
                if(!outside.InBounds(map) || !outside.Standable(map) ||
                   outside.GetEdifice(map)!=null || outside.Roofed(map))continue;
                if(map.reachability.CanReach(edge,outside,PathEndMode.OnCell,
                    TraverseMode.PassDoors,Danger.Deadly))return true;
            }
            return false;
        }

        // Only incident initialization: full perimeter and approach probes.
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
                int span=(side<2 ? map.Size.z : map.Size.x);
                for(int t=5;t<span-5;t++)
                {
                    IntVec3 edge=side==0 ? new IntVec3(0,0,t) :
                        side==1 ? new IntVec3(map.Size.x-1,0,t) :
                        side==2 ? new IntVec3(t,0,0) :
                                  new IntVec3(t,0,map.Size.z-1);
                    if(!GroundEntryClusterSafe(map,edge))continue;
                    // Does not require an already walkable prison path: a
                    // rescue raid exists precisely to breach closed barriers.
                    // Still prefer cells that can approach the prison without
                    // crossing unbreakable impassable terrain.
                    if(!ReachableExterior(map,edge,captive))continue;
                    int danger=0;
                    for(int depth=0;depth<=8;depth+=4)
                    {
                        IntVec3 sample=edge + (side==0 ? new IntVec3(depth,0,0) :
                            side==1 ? new IntVec3(-depth,0,0) :
                            side==2 ? new IntVec3(0,0,depth) :
                                      new IntVec3(0,0,-depth));
                        if(!sample.InBounds(map))break;
                        // Spawn safety cannot depend on the ordinal position
                        // of a turret among hundreds of ordinary colony walls.
                        // Scan all actual guns once per candidate stage, never
                        // only the first 200 buildings from the list.
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
                               GenSight.LineOfSight(defender.Position,sample,map))
                                danger+=3000;
                        }
                    }
                    // Prefer the safest usable entry; distance is a secondary
                    // cost and may not outweigh one actual turret firing lane.
                    // Put objective distance ahead of a speculative threat
                    // penalty: an entire idle squad at the farthest corner
                    // is not safer when it cannot influence the operation.
                    int cost=danger/4+(edge-captive.Position).LengthHorizontalSquared;
                    if(cost<bestScore){best=edge;bestScore=cost;}
                }
            }
            return best;
        }
    }
}
