"""R025 user incidents. Static contracts + executable abstract route oracle.

Does not instantiate RimWorld or claim the shipped C# was compiled or played.
"""
from pathlib import Path
from heapq import heappop, heappush
import re, xml.etree.ElementTree as ET
P=Path(__file__).resolve().parents[1]
S=P/'RaidersHaveFamilies/Source'
route=(S/'RHFObjectiveRouteV128.cs').read_text()
rescue=(S/'RHFRescuePlannerS41.cs').read_text()
combat=(S/'NJRCombatContactS44.cs').read_text()
entry=(S/'RHFRescueGroundEntryS41.cs').read_text()
duty=ET.parse(P/'RaidersHaveFamilies/Defs/DutyDefs/RHF_MissionHold.xml')
ops=(S/'RHFRescueRoleJobGiverS41.cs').read_text()

# Contract guards for actually active code. Make a failed check fail the CI.
assert 'int work=RHFCombatPlannerV125.BreachWorkCost(building);' in route
assert 'if(building is Building_Door openDoor && openDoor.Open)return 10;' in route
assert 'if(IsRescueStructuralBarrier(building))' in route
assert 'plan.RiskFallback=plan.FirstTurretExposureIndex>=0;' in route
assert 'CancelObsoleteStructureJobs(op,map,board);' in rescue
assert 'soldier.jobs.EndCurrentJob(JobCondition.InterruptForced);' in rescue
assert 'existing.Escort.CanReach(captive,PathEndMode.ClosestTouch,Danger.Deadly)' in rescue
assert '!candidate.CanReach(captive,PathEndMode.ClosestTouch,Danger.Deadly)' in rescue
assert 'rescuer.CanReach(captive,PathEndMode.Touch,Danger.Deadly)' in rescue
assert 'if(op.Phase==2)Extract(controller,op,now);' in rescue
assert 'FirstBlockedExitDoor(map,op,navigator)' in rescue
assert 'board.Breacher=soldier;' in rescue
assert 'if(op.Phase==2 && !(obstacle is Building_Door))return null;' in ops
assert 'board.Barrier=null;board.Breacher=null;' in rescue
assert 'FirstBlockedExitDoor(Map map,\n            RHFOperation op,Pawn navigator)' in (S/'NJRRescueExitS41.cs').read_text()
assert 'distance>range*range' in combat
assert 'enemyJob.targetA.Thing==soldier' in combat
assert 'FriendlyFiringLane(op,soldier,enemy,cell)' in combat
assert '!RHFRescueFireDisciplineV136.UnsafeLine(op,soldier,enemy)' in combat
assert 'int scoreCeiling=valid[0].Score+Math.Max(40000,valid[0].Score/2);' in entry
assert '(c.Center-history.Last).LengthHorizontalSquared>=225' in entry
assert 'System.Random debugRandom=op.DebugForced ?' in entry
assert 'RecentEntries.GetValue(map' in entry
assert 'if(op?.Type==(int)RHFOperationType.Rescue)return null;' in combat
assert 'if(op.BoardS41?.Breacher!=pawn ||' in ops
nodes=[n.attrib.get('Class') for n in duty.findall('.//li')]
assert nodes.index('RaidersHaveFamilies.RHFJobGiverMissionDefenseV136') < nodes.index('RaidersHaveFamilies.RHFRescueRoleJobGiverS41')

# Extract source weights so this model fails if an old wall-heavy tariff returns.
def constant(name):
 m=re.search(rf'private const int {name} = (\d+);',route)
 assert m,name
 return int(m.group(1))
W=constant('WallBreachBaseR024')
D=constant('DoorBreachBaseR024')
assert W>100*D

def step(kind,hp=150,fire=0):
 work=16+hp*2
 if kind=='generator':return None
 return 10+(W+work*6 if kind=='wall' else D+work if kind=='door' else 0)+min(1600,fire*600)

def shortest(graph):
 q=[(0,'spawn',())];dist={}
 while q:
  spent,node,via=heappop(q)
  if node in dist:continue
  dist[node]=spent
  if node=='vip':return via,spent
  for neighbor,kind,hp,fire in graph.get(node,[]):
   c=step(kind,hp,fire)
   if c is not None and neighbor not in dist:
    heappush(q,(spent+c,neighbor,via+(kind,)))
 return None

# Problem visible in screenshots: nearest wall versus an actual accessible door.
g={'spawn':[('A','wall',280,0),('B','walk',0,0)],
   'A':[('vip','walk',0,0)],
   'B':[('vip','door',300,2)]}
assert shortest(g)[0]==('walk','door'),shortest(g)
# Explicit: three walls versus wall + door, with a short turret lane.
g={'spawn':[('A','wall',250,0),('B','walk',0,0)],
   'A':[('C','wall',250,0)],'C':[('vip','wall',250,0)],
   'B':[('D','wall',300,2)],'D':[('vip','door',200,2)]}
assert shortest(g)[0]==('walk','wall','door'),shortest(g)
# Turrets on every physically available route: still search/attack.
g={'spawn':[('A','walk',0,5)],'A':[('vip','door',200,5)]}
assert shortest(g) is not None
# Machinery must never become a breach objective.
g={'spawn':[('A','generator',50,0)],'A':[('vip','walk',0,0)]}
assert shortest(g) is None
print('PASS R025: doors over walls, all-turret route, no generators; contact, escort, demolition, spawn and threat priority contracts. NOT gameplay.')
