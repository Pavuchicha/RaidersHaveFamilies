// NJR beta2: bounded *physical* demolition of up to two small constructed
// walls to enlarge a prison-room launch site. Ordinary melee jobs cause damage.
// Never clear mineable mountain, roofs supporting the VIP, or other buildings.
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI.Group;
namespace RaidersHaveFamilies
{
    internal static class NJRPodSiteClearS41
    {
        internal static readonly Rot4[] Rotations={Rot4.North,Rot4.East,Rot4.South,Rot4.West};
        internal static Rot4 RotationFor(IntVec3 launcher,IntVec3 pod)
        {
            foreach(Rot4 rotation in Rotations)
                if(FuelingPortUtility.GetFuelingPortCell(launcher,rotation)==pod)return rotation;
            return Rot4.North;
        }
        internal static bool Contains(IntVec3 cell,IntVec3 origin,ThingDef def,Rot4 rotation)
        {
            return origin.IsValid && def!=null && GenAdj.OccupiedRect(origin,rotation,def.size).Contains(cell);
        }
        internal static bool ReservedFootprint(RHFOperation op,IntVec3 cell)
        {
            return Contains(cell,op.PodLauncherCell,DefDatabase<ThingDef>.GetNamedSilentFail("PodLauncher"),
                       RotationFor(op.PodLauncherCell,op.PodCell)) ||
                   Contains(cell,op.PodCell,DefDatabase<ThingDef>.GetNamedSilentFail("TransportPod"),Rot4.North);
        }
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
            ThingDef launcherDef,ThingDef podDef,Pawn vip,Rot4 rotation=default)
        {
            if(map==null || launcherDef==null || podDef==null || vip==null)return -1;
            var walls=new HashSet<Building>();
            if(!Check(launcher,launcherDef,rotation) || !Check(pod,podDef,Rot4.North))return -1;
            if(Contains(pod,launcher,launcherDef,rotation))return -1;
            return walls.Count;
            bool Check(IntVec3 cell,ThingDef def,Rot4 rot)
            {
                foreach(IntVec3 c in GenAdj.OccupiedRect(cell,rot,def.size))
                {
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
                    // A mission soldier can step off a proposed footprint.
                    // Treating every arriving raider as permanent terrain
                    // made all indoor sites appear occupied and selected lawns.
                    Pawn occupant=c.GetFirstPawn(map);
                    if(occupant!=null && occupant!=vip &&
                       !(occupant.GetLord()?.LordJob is RHFRescueLordJobV124))
                        return false;
                }
                return true;
            }
        }
        internal static Thing Blocker(RHFOperation op,Map map,ThingDef launcher,
            ThingDef pod,Pawn vip)
        {
            if(op==null || Needed(map,op.PodLauncherCell,op.PodCell,launcher,pod,vip,RotationFor(op.PodLauncherCell,op.PodCell))<1)
                return null;
            for(int pass=0;pass<2;pass++)
            {
                IntVec3 cell=pass==0?op.PodLauncherCell:op.PodCell;
                ThingDef def=pass==0?launcher:pod;
                foreach(IntVec3 c in GenAdj.OccupiedRect(cell,pass==0?RotationFor(op.PodLauncherCell,op.PodCell):Rot4.North,def.size))
                {
                    if(!c.InBounds(map))continue;
                    Building wall=c.GetEdifice(map);
                    if(SafeWall(wall,vip,map))return wall;
                }
            }
            return null;
        }
    }
}
