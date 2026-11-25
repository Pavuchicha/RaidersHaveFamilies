// NJR beta2: actual room topology, not guessed wall-count as connectivity.
using System;
using RimWorld;
using Verse;
namespace RaidersHaveFamilies
{
    internal static class NJRRescueInteriorS41
    {
        internal static bool ShelteredPadR043(Map map,IntVec3 launcher,IntVec3 pod)
        {
            if(!launcher.InBounds(map) || !pod.InBounds(map))return false;
            Room room=launcher.GetRoom(map);
            return (room!=null && !room.PsychologicallyOutdoors && pod.GetRoom(map)==room) ||
                (launcher.Roofed(map) && pod.Roofed(map));
        }
        internal static int PodTierR043(Map map,IntVec3 anchor,IntVec3 launcher,IntVec3 pod)
        {
            if(!ShelteredPadR043(map,launcher,pod))return 3;
            Room prison=anchor.GetRoom(map),room=launcher.GetRoom(map);
            if(prison!=null && room==prison && (!prison.PsychologicallyOutdoors ||
                (launcher-anchor).LengthHorizontalSquared<=36))return 0;
            if(DirectDoor(map,prison,room,anchor))return 1;
            return 2;
        }
        internal static bool DirectDoor(Map map,Room prison,Room other,IntVec3 center)
        {
            if(map==null || prison==null || other==null || prison==other)return false;
            for(int x=Math.Max(0,center.x-22);x<=Math.Min(map.Size.x-1,center.x+22);x++)
            for(int z=Math.Max(0,center.z-22);z<=Math.Min(map.Size.z-1,center.z+22);z++)
            {
                IntVec3 at=new IntVec3(x,0,z);
                if(!(at.GetEdifice(map) is Building_Door))continue;
                bool touchesPrison=false,touchesOther=false;
                foreach(IntVec3 dir in GenAdj.CardinalDirections)
                {
                    IntVec3 near=at+dir;
                    if(!near.InBounds(map))continue;
                    Room room=near.GetRoom(map);
                    if(room==prison)touchesPrison=true;
                    if(room==other)touchesOther=true;
                }
                if(touchesPrison && touchesOther)return true;
            }
            return false;
        }
        internal static int Exposure(Map map,IntVec3 cell)
        {
            int risk=0;
            // Evaluate EVERY real turret. The old 240-building limit counted
            // ordinary walls/furniture as well, so large bases hid turrets
            // farther down the building registry from door-room planning.
            foreach(Building defense in map.listerBuildings.allBuildingsColonist)
            {
                if(!RHFCombatPlannerV125.IsTurret(defense))continue;
                if((defense.Position-cell).LengthHorizontalSquared<1600 &&
                   GenSight.LineOfSight(defense.Position,cell,map))risk+=1;
            }
            return risk;
        }
    }
}
