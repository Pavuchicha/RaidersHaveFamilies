using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace RaidersHaveFamilies
{
    // Local orders remain individually executable after the strategic route
    // is shortened to the frontmost member. No demolition and no second Lord.
    // A reverse cardinal flood from the fixed rally anchor is resumed over
    // planning pulses, not restarted per soldier or per failed waypoint.
    internal sealed class NJRRallyFieldR037
    {
        private static readonly IntVec3[] Directions={new IntVec3(1,0,0),new IntVec3(-1,0,0),
            new IntVec3(0,0,1),new IntVec3(0,0,-1)};
        private readonly Map map;
        private readonly Func<IntVec3,bool> exposed;
        private readonly Dictionary<IntVec3,int> distance=new Dictionary<IntVec3,int>();
        private readonly HashSet<IntVec3> rejected=new HashSet<IntVec3>();
        private readonly Queue<IntVec3> frontier=new Queue<IntVec3>();
        private int lastExpansionTick=-1;
        internal readonly int Created;
        internal int Count=>distance.Count;
        internal bool Pending=>frontier.Count>0;
        internal bool NeedsExpansion(List<Pawn> fighters)
        {foreach(Pawn p in fighters)if(!Covered(p.Position))return Pending;return false;}
        internal NJRRallyFieldR037(Map m,IntVec3 anchor,int now)
        {
            map=m;Created=now;exposed=RHFObjectiveRouteV128.RallySafetySnapshotR037(m,anchor);
            distance[anchor]=0;frontier.Enqueue(anchor);
        }
        internal bool TryDistance(IntVec3 c,out int d)=>distance.TryGetValue(c,out d);
        internal bool Descends(IntVec3 from,IntVec3 to)=>distance.TryGetValue(from,out int a) &&
            distance.TryGetValue(to,out int b) && b<a;
        private bool Covered(IntVec3 c)
        {
            if(distance.ContainsKey(c))return true;
            // A soldier already exposed may step directly out of the lane.
            foreach(IntVec3 delta in Directions)if(distance.ContainsKey(c+delta))return true;
            return false;
        }
        internal void ExpandFor(List<Pawn> fighters)
        {
            int now=Find.TickManager.TicksGame;
            if(lastExpansionTick==now || !Pending)return;
            bool need=false;
            foreach(Pawn p in fighters)if(!Covered(p.Position)){need=true;break;}
            if(!need)return;
            lastExpansionTick=now;
            // Hard work budget per operation per pulse, not a total map cap.
            for(int n=0;n<1600 && frontier.Count>0;n++)
            {
                IntVec3 c=frontier.Dequeue();int d=distance[c]+1;
                foreach(IntVec3 delta in Directions)
                {
                    IntVec3 next=c+delta;
                    if(!next.InBounds(map) || distance.ContainsKey(next) || rejected.Contains(next))continue;
                    if(!next.Standable(map) || exposed(next)){rejected.Add(next);continue;}
                    distance[next]=d;frontier.Enqueue(next);
                }
            }
        }
        private bool FreeDestination(Pawn p,IntVec3 c,HashSet<IntVec3> used)
        {
            if(c==p.Position || used.Contains(c) || !c.Standable(map))return false;
            Pawn occupant=c.GetFirstPawn(map);
            return (occupant==null || occupant==p) &&
                !RHFObjectiveRouteV128.StagingExposedToTurret(map,c) &&
                RHFObjectiveRouteV128.FormationLegSafe(p.Position,c,map,false);
        }
        internal IntVec3 Waypoint(Pawn p,HashSet<IntVec3> used)
        {
            int initial=distance.TryGetValue(p.Position,out int d)?d:int.MaxValue;
            var candidates=new List<IntVec3>(32);
            // Examine each downhill neighbor. A short straight section may
            // be issued as one native Goto; never cut the corner of a wall
            // or shortcut the safety field across turret exposure.
            foreach(IntVec3 delta in Directions)
            {
                IntVec3 c=p.Position+delta;
                for(int length=1;length<=8;length++,c+=delta)
                {
                    if(!distance.TryGetValue(c,out int remaining) || remaining>=initial)break;
                    if(FreeDestination(p,c,used))candidates.Add(c);
                }
            }
            candidates.Sort((a,b)=>distance[a].CompareTo(distance[b]));
            for(int i=0;i<candidates.Count && i<8;i++)
                if(p.CanReach(candidates[i],PathEndMode.OnCell,Danger.Deadly))return candidates[i];
            return IntVec3.Invalid;
        }
    }
}
