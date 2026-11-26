"""R051: recorded R050 failure facts, local topology and source contracts, not engine tests."""
import json
from pathlib import Path
from collections import deque
r=Path(__file__).resolve().parents[1]; s=r/'RaidersHaveFamilies/Source'
a=json.loads((r/'tests/fixtures/r051_runtime.json').read_text())
assert a['hits']==[[40998,19434,36726],[41802,19434,36726],[41878,19434,36726]]
assert a['failed_deconstruct_count']==175 and a['last_phase']==2
assert a['last_barricades_built']==1
# Minimal two-compartment regression: an internal door without exterior proof
# is a defensive gate, and the launcher side is an independent anchor.
free={(0,0),(1,0),(2,0),(3,0),(0,2),(1,2),(2,2),(3,2)}
doors={(3,0),(3,2)}; exterior={(3,0)}
def gates(seeds,wall=None):
 q=deque(c for c in seeds if c!=wall); seen=set(q); result=set()
 while q:
  x,y=q.popleft()
  for n in ((x-1,y),(x+1,y),(x,y-1),(x,y+1)):
   if n==wall or n not in free:continue
   if n in doors or n in exterior:result.add(n);continue
   if n not in seen:seen.add(n);q.append(n)
 return result
assert gates({(0,0)})=={(3,0)}
assert gates({(0,0),(0,2)})==doors
assert gates({(0,0),(0,2)})-gates({(0,0),(0,2)},(2,2))=={(3,2)}
read=lambda name:(s/name).read_text()
g=read('SealGeometryR049.cs');v=read('RHFVipEvacS311.cs');d=read('NJRPrisonerDefenseR047.cs');o=read('RHFOperations.cs');j=read('JobDriver_NJRReopenOwnSealR051.cs');role=read('RHFRescueRoleJobGiverS41.cs')
assert 'gates.RemoveWhere(g=>!doors.Contains(g)' in g and 'GenAdj.OccupiedRect(op.PodLauncherCell' in g
assert 'candidate.Freed?.Contains(victim)==true' in d and 'EndCurrentJob(JobCondition.InterruptForced)' in d
assert 'NJRPrisonerDefenseR047.Pulse(op,now)' in o and 'NativePrepareBeforeReleaseR051(op,now)' in o
assert 'TryEarlyPadR051' in v and 'EarlySealSafeR051' in v and 'member==board.Breacher' in v
assert 'NJR_ReopenOwnSealR051' in role and 'Wait(300,TargetIndex.A)' in j and 'IsOwnSealR043(op,wall)' in j
print('PASS R051: recorded failure facts, internal gate model and source contracts')
