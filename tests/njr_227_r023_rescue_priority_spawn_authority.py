"""R023 regression gate: rescue contact, dynamic entry, strict demolition authority.

Static/algorithmic tests only. Does NOT execute the RimWorld C# assembly or prove TPS.
"""
from pathlib import Path
from collections import deque

S = Path(__file__).resolve().parents[1] / 'RaidersHaveFamilies' / 'Source'
entry=(S/'RHFRescueGroundEntryS41.cs').read_text()
plan=(S/'RHFRescuePlannerS41.cs').read_text()
roles=(S/'RHFRescueRoleJobGiverS41.cs').read_text()
combat=(S/'NJRCombatContactS44.cs').read_text()
command=(S/'RHFRescueCommandV124.cs').read_text()
route=(S/'RHFObjectiveRouteV128.cs').read_text()

# Issue 1: A Room identifier is not evidence of a wall. Open doorway rescue
# requires native reachability AND real physical LOS, at short contact range.
assert 'return GenSight.LineOfSight(rescuer.Position,captive.Position,map) &&' in plan
assert 'rescuer.CanReach(captive,PathEndMode.Touch,Danger.Deadly)' in plan
assert 'if(a!=b)return false;' not in plan
assert '(rescuer.Position-captive.Position).LengthHorizontalSquared<=5' in plan

# Issue 2: A blackboard must grant explicit breach access to ONE soldier and
# ONE CURRENT barrier. Stale job orders cannot authorize another structure.
assert 'op.BoardS41?.Breacher!=pawn' in roles
assert 'op.BoardS41?.Barrier!=order.Objective' in roles
assert 'op.BoardS41?.Barrier!=best' in combat
assert 'op.BoardS41?.Breacher!=soldier' in combat
assert 'op.BoardS41?.Breacher==p' in command
assert 'if(!route.Valid)obstacle=null;' in plan
assert 'IsRescueStructuralBarrier(building)' in route and 'DoorBreachBaseR024+work' in route

# Issue 3: If a real native path to the captive exists, an exhausted tactical
# solver must not send everybody into Hold or invent a random wall.
assert 'IntVec3 fallback=route.Valid ? IntVec3.Invalid :' in plan
assert 'soldier.CanReach(captive,PathEndMode.Touch,Danger.Deadly)' in plan
assert 'board.Assign(soldier,RHFRoleS41.Advance,captive,IntVec3.Invalid,now);' in plan
assert 'if(!route.Valid)obstacle=null;' in plan
assert plan.index('else if(obstacle==null && !route.Valid &&') < plan.index('else if(!route.Valid && obstacle==null && fallback.IsValid')
assert 'int[] radii={2,4,6,9,12,16};' in plan
assert 'for(int radius=3;radius<=Math.Min(16,' not in plan

# Issue 4: R016's single cheapest fixed insertion is gone. The destination
# remains a map edge, but candidates span multiple edges and are selected
# from an admissible near-best shortlist with a map-scoped random draw.
assert 'for(int side=0;side<4;side++)' in entry
assert 'int[] validPerSide=new int[4];' in entry
assert 'if(validPerSide[proposed.Side]>=8)continue;' in entry
assert 'GroundEntryClusterSafe(map,center)' in entry
assert 'ReachableExterior(map,center,captive)' in entry
assert 'valid.Sort((a,b)=>a.Score.CompareTo(b.Score));' in entry
assert 'int[] validPerSide=new int[4];' in entry
assert 'if(existing.Side==proposed.Side &&' in entry
assert 'System.Random debugRandom=op.DebugForced ?' in entry
assert 'Rand.Range(0,options.Count)' in entry
assert 'ConnectedCluster(map,center)' in entry
assert 'map.reachability.CanReach(center,c,PathEndMode.OnCell' not in entry

# Illustrative model: cardinal flood accepts connected landing patches and
# excludes a separated rock pocket that an independent cell-count would admit.
def cluster(grid, center):
    h=len(grid);w=len(grid[0]);q=deque([center]);seen={center}
    while q:
        x,y=q.popleft()
        for nx,ny in ((x+1,y),(x-1,y),(x,y+1),(x,y-1)):
            if 0<=nx<w and 0<=ny<h and grid[ny][nx]=='.' and (nx,ny) not in seen:
                seen.add((nx,ny));q.append((nx,ny))
    return seen
sample=['....#..','....#..','....#..','....#..','....#..']
assert len(cluster(sample,(0,0)))==20
assert (5,0) not in cluster(sample,(0,0))

# An open doorway is physically traversable even if a room bookkeeping layer
# assigned separate room IDs; a wall remains blocking.
open_door=['##.##','##D##','##.##']
closed_door=['##.##','#####','##.##']
def traversable(grid,start,end):
    q=deque([start]);seen={start}
    while q:
        here=q.popleft()
        if here==end:return True
        x,y=here
        for xx,yy in ((x+1,y),(x-1,y),(x,y+1),(x,y-1)):
            if (0<=yy<len(grid) and 0<=xx<len(grid[0])
                and grid[yy][xx] in '.D' and (xx,yy) not in seen):
                seen.add((xx,yy));q.append((xx,yy))
    return False
assert traversable(open_door,(2,0),(2,2))
assert not traversable(closed_door,(2,0),(2,2))

print('PASS R023: contact / demolition permission / native path priority / varied safe entry (STATIC + MODELS; no runtime)')
