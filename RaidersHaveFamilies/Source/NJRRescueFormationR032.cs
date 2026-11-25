using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RaidersHaveFamilies
{
    // Ephemeral per-plan allocation. No pawn tick patch or save state.
    internal static class NJRRescueFormationR032
    {
        internal static readonly IntVec3[] Adjacent = {
            IntVec3.North,IntVec3.South,IntVec3.East,IntVec3.West,
            IntVec3.North+IntVec3.East,IntVec3.North+IntVec3.West,
            IntVec3.South+IntVec3.East,IntVec3.South+IntVec3.West};

        // Use the installed game's actual immediate Touch rule when exposed.
        // Reflection isolates the unstable 1.7 API; a missing API is not a
        // license to hit through corners. Fall back to conservative geometry.
        private static readonly MethodInfo ImmediateTouch=FindImmediateTouch();
        private static MethodInfo FindImmediateTouch()
        {
            try
            {
                // Namespace differs between reference builds. Resolve both, not just Verse.AI.
                MethodInfo method=null;
                foreach(string name in new[]{"Verse.ReachabilityImmediate","Verse.AI.ReachabilityImmediate"})
                {
                    Type type=AccessTools.TypeByName(name);
                    if(type==null)continue;
                    method=AccessTools.Method(type,"CanReachImmediate",new Type[]{
                        typeof(IntVec3),typeof(LocalTargetInfo),typeof(Map),typeof(PathEndMode),typeof(Pawn)});
                    if(method!=null && method.IsStatic)break;
                    method=null;
                }
                Log.Message("[NJR][R032][Formation] diagonal native Touch="+(method!=null?"available":"conservative fallback"));
                return method;
            }
            catch{return null;}
        }
        internal static bool StrikeCell(Map map,Thing barrier,IntVec3 cell,Pawn pawn=null)
        {
            if(map==null || barrier==null || !cell.InBounds(map) ||
               !cell.Standable(map) || cell.GetEdifice(map)!=null)return false;
            IntVec3 delta=cell-barrier.Position;
            int d=delta.LengthHorizontalSquared;
            if(d!=1 && d!=2)return false;
            if(d==2)
            {
                if(ImmediateTouch!=null && pawn!=null)
                {
                    try
                    {
                        return ImmediateTouch.Invoke(null,new object[]{cell,new LocalTargetInfo(barrier),
                            map,PathEndMode.Touch,pawn}) is bool allowed && allowed;
                    }
                    catch{} // exact signature may differ on future 1.7 builds
                }
                // Do not force a diagonal melee through a solid corner.
                // Native AttackMelee remains responsible for the actual hit.
                IntVec3 x=barrier.Position+new IntVec3(delta.x,0,0);
                IntVec3 z=barrier.Position+new IntVec3(0,0,delta.z);
                // A wall FACE has one closed orthogonal neighbour and one open
                // neighbour. Reject only a completely sealed corner. The real
                // AttackMelee driver, not this estimate, decides whether it hits.
                if(!x.InBounds(map) || !z.InBounds(map) ||
                   (!x.Standable(map) && !z.Standable(map)))return false;
            }
            return true;
        }

        internal static IntVec3 BreachSlot(Pawn pawn,Thing barrier,Map map,
            HashSet<IntVec3> used,List<Pawn> squad, bool risk,IntVec3 previous, bool allowInteriorTurn=false)
        {
            IntVec3 best=IntVec3.Invalid;int score=int.MaxValue;
            foreach(IntVec3 offset in Adjacent)
            {
                IntVec3 cell=barrier.Position+offset;
                if(used.Contains(cell) || !StrikeCell(map,barrier,cell,pawn))continue;
                Pawn occupant=cell.GetFirstPawn(map);
                // An idle teammate may be explicitly relocated by this plan;
                // a hostile or unrelated pawn cannot be allocated over.
                if(occupant!=null && occupant!=pawn && !squad.Contains(occupant))continue;
                if(!pawn.CanReach(cell,PathEndMode.OnCell,Danger.Deadly))continue;
                if(!RHFObjectiveRouteV128.FormationLegSafe(pawn.Position,cell,map,risk) &&
                   !(allowInteriorTurn && NJRRescueAccessR040.SafeInteriorReach(pawn,map,cell)))continue;
                int cost=(pawn.Position-cell).LengthHorizontalSquared*4+
                    (offset.LengthHorizontalSquared==2?6:0)+
                    (occupant!=null && occupant!=pawn?12:0);
                if(cell==previous)cost-=10000;
                if(cost<score){score=cost;best=cell;}
            }
            return best;
        }

        internal static IntVec3 ContactSlot(Pawn pawn,Pawn captive,Map map,HashSet<IntVec3> used)
        {
            // Native reachability already proved the prisoner accessible.
            // Prioritize touch distance, then distribute supporting members.
            var candidates=new List<IntVec3>();
            for(int x=-4;x<=4;x++)for(int z=-4;z<=4;z++)
            {
                int d=x*x+z*z;
                if(d==0 || d>16)continue;
                IntVec3 cell=captive.Position+new IntVec3(x,0,z);
                if(used.Contains(cell) || !cell.InBounds(map) || !cell.Standable(map) ||
                   cell.GetEdifice(map)!=null || !GenSight.LineOfSight(cell,captive.Position,map))continue;
                Pawn occupant=cell.GetFirstPawn(map);
                if(occupant!=null && occupant!=pawn)continue;
                candidates.Add(cell);
            }
            candidates.Sort((a,b)=>((a-captive.Position).LengthHorizontalSquared*1000+
                (a-pawn.Position).LengthHorizontalSquared).CompareTo(
                (b-captive.Position).LengthHorizontalSquared*1000+(b-pawn.Position).LengthHorizontalSquared));
            int probes=0;
            foreach(IntVec3 cell in candidates)
            {
                if(++probes>16)break;
                if(pawn.CanReach(cell,PathEndMode.OnCell,Danger.Deadly))return cell;
            }
            return IntVec3.Invalid;
        }

        internal static IntVec3 SpreadNear(Pawn pawn,IntVec3 anchor,Map map,
            HashSet<IntVec3> used,int minSquared,int maxRadius,bool risk)
        {
            var choices=new List<IntVec3>();
            for(int x=-maxRadius;x<=maxRadius;x++)for(int z=-maxRadius;z<=maxRadius;z++)
            {
                IntVec3 cell=anchor+new IntVec3(x,0,z);
                int d=x*x+z*z;
                if(d<minSquared || d>maxRadius*maxRadius || used.Contains(cell) ||
                   !cell.InBounds(map) || !cell.Standable(map) || cell.GetEdifice(map)!=null)continue;
                Pawn occupant=cell.GetFirstPawn(map);
                if(occupant!=null && occupant!=pawn)continue;
                if(cell.GetRoom(map)!=pawn.Position.GetRoom(map))continue;
                choices.Add(cell);
            }
            choices.Sort((a,b)=>((pawn.Position-a).LengthHorizontalSquared+
                (anchor-a).LengthHorizontalSquared*2).CompareTo(
                (pawn.Position-b).LengthHorizontalSquared+(anchor-b).LengthHorizontalSquared*2));
            int checkedCells=0;
            foreach(IntVec3 c in choices)
            {
                if(++checkedCells>16)break;
                if(!RHFObjectiveRouteV128.FormationLegSafe(pawn.Position,c,map,risk) ||
                   !pawn.CanReach(c,PathEndMode.OnCell,Danger.Deadly))continue;
                return c;
            }
            return IntVec3.Invalid;
        }
    }
}
