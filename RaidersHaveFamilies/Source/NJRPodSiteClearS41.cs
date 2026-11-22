// NJR beta2: bounded *physical* demolition of up to two small constructed
// walls to enlarge a prison-room launch site. Ordinary melee jobs cause damage.
// Never clear mineable mountain, roofs supporting the VIP, or other buildings.
using System.Collections.Generic;
using RimWorld;
using Verse;
namespace RaidersHaveFamilies
{
    internal static class NJRPodSiteClearS41
    {
        private static bool SafeWall(Building b,Pawn prisoner,Map map)
        {
            return b!=null && !b.Destroyed && b.def==ThingDefOf.Wall &&
                b.Faction==Faction.OfPlayer && prisoner!=null &&
                (b.Position-prisoner.Position).LengthHorizontalSquared>=9 &&
                (map.roofGrid.RoofAt(b.Position)==null ||
                 (b.Position-prisoner.Position).LengthHorizontalSquared>=25) &&
                map.roofGrid.RoofAt(b.Position)?.isThickRoof!=true;
        }
        internal static int Needed(Map map,IntVec3 launcher,IntVec3 pod,
            ThingDef launcherDef,ThingDef podDef,Pawn vip)
        {
            if(map==null || launcherDef==null || podDef==null || vip==null)return -1;
            var walls=new HashSet<Building>();
            if(!Check(launcher,launcherDef) || !Check(pod,podDef))return -1;
            return walls.Count;
            bool Check(IntVec3 cell,ThingDef def)
            {
                for(int x=0;x<def.size.x;x++)for(int z=0;z<def.size.z;z++)
                {
                    IntVec3 c=cell+new IntVec3(x,0,z);
                    if(!c.InBounds(map) || c==vip.Position ||
                       map.roofGrid.RoofAt(c)?.isThickRoof==true)return false;
                    Building obstacle=c.GetEdifice(map);
                    if(obstacle!=null)
                    {
                        if(!SafeWall(obstacle,vip,map))return false;
                        walls.Add(obstacle);
                        if(walls.Count>2)return false;
                    }
                    else if(!c.Standable(map) || c.GetFirstBuilding(map)!=null)
                        return false;
                    if(c.GetFirstPawn(map)!=null && c!=vip.Position)return false;
                }
                return true;
            }
        }
        internal static Thing Blocker(RHFOperation op,Map map,ThingDef launcher,
            ThingDef pod,Pawn vip)
        {
            if(op==null || Needed(map,op.PodLauncherCell,op.PodCell,launcher,pod,vip)<1)
                return null;
            for(int pass=0;pass<2;pass++)
            {
                IntVec3 cell=pass==0?op.PodLauncherCell:op.PodCell;
                ThingDef def=pass==0?launcher:pod;
                for(int x=0;x<def.size.x;x++)for(int z=0;z<def.size.z;z++)
                {
                    IntVec3 c=cell+new IntVec3(x,0,z);
                    if(!c.InBounds(map))continue;
                    Building wall=c.GetEdifice(map);
                    if(SafeWall(wall,vip,map))return wall;
                }
            }
            return null;
        }
    }
}
