// S4.1-A/O: grounded, bounded insertion planner for RHF ground rescue only.
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
            for(int dx=-8;dx<=8;dx+=2)
            for(int dz=-8;dz<=8;dz+=2)
            {
                if(dx*dx+dz*dz>64)continue;
                IntVec3 c=center+new IntVec3(dx,0,dz);
                if(!c.InBounds(map))continue;
                total++;
                RoofDef roof=map.roofGrid.RoofAt(c);
                if(roof!=null && roof.isThickRoof)return false;
                // No dozens of expensive A* queries per edge candidate.
                // The inner radius has a hard all-clear check below.
                bool clear=c.Standable(map) && c.GetEdifice(map)==null && roof==null;
                if(clear)open++;
                if(dx*dx+dz*dz<=9 && !clear)return false;
            }
            return total>=24 && open*5>=total*4;
        }

        // Only incident initialization: at most ~100 perimeter candidates.
        internal static IntVec3 Choose(Map map,RHFOperation op)
        {
            if(map==null || op?.Targets==null)return IntVec3.Invalid;
            Pawn captive=op.Vip;
            if(captive==null || !captive.Spawned || captive.Map!=map)
            {
                captive=null;
                for(int i=0;i<op.Targets.Count && i<24;i++)
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
                for(int t=5;t<span-5;t+=11)
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
                    if(!map.reachability.CanReach(edge,captive.Position,
                           PathEndMode.Touch,TraverseMode.PassAllDestroyableThings,
                           Danger.Deadly))continue;
                    int danger=0;
                    for(int depth=0;depth<=8;depth+=4)
                    {
                        IntVec3 sample=edge + (side==0 ? new IntVec3(depth,0,0) :
                            side==1 ? new IntVec3(-depth,0,0) :
                            side==2 ? new IntVec3(0,0,depth) :
                                      new IntVec3(0,0,-depth));
                        if(!sample.InBounds(map))break;
                        int checkedTurrets=0;
                        foreach(Building turret in map.listerBuildings.allBuildingsColonist)
                        {
                            if(++checkedTurrets>200)break;
                            if(!RHFCombatPlannerV125.IsTurret(turret))continue;
                            if((sample-turret.Position).LengthHorizontalSquared<=1600 &&
                               GenSight.LineOfSight(turret.Position,sample,map))
                                danger+=12000;
                        }
                        int checkedDefenders=0;
                        foreach(Pawn defender in map.mapPawns.FreeColonistsSpawned)
                        {
                            if(++checkedDefenders>96)break;
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
                    int cost=danger+(edge-captive.Position).LengthHorizontalSquared;
                    if(cost<bestScore){best=edge;bestScore=cost;}
                }
            }
            return best;
        }
    }
}
