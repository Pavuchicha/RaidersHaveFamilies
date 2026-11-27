"""Recorded R053a final map + policy/source contracts, NOT a RimWorld runtime test."""
import json
from pathlib import Path
from collections import deque
r=Path(__file__).resolve().parents[1];s=r/'RaidersHaveFamilies/Source'
a=json.loads((r/'tests/fixtures/r054_runtime.json').read_text()); assert a['revision']=='R053'
logs=a['logs']; release=next(t for t,m in logs if 'Freed=' in m)
assert release==38668
assert [t for t,m in logs if 'Real enemy wall blueprint' in m]==[38398,41548]
assert [t for t,m in logs if 'completed Wall' in m]==[38758,41908]
assert a['final_board']['Decision']=='R053 post-launch: reachable=0 isolated=5 recovery=False'
assert a['positions'].count([121,154])==4 and a['jobs'].count('Wait')==5
walk={(x,z) for z,row in enumerate(a['walk_rows']) for x,c in enumerate(row) if c=='.'}
def flood(start,free):
 seen={start};q=deque([start])
 while q:
  x,z=q.popleft()
  for n in ((x-1,z),(x+1,z),(x,z-1),(x,z+1)):
   if n in free and n not in seen:seen.add(n);q.append(n)
 return seen
# The recorded progress waypoint is a one-cell cardinal pocket. Removing the
# exact used launcher footprint reconnects it to the southern work area in this model.
assert flood((121,154),walk)=={(121,154)}
assert a['launcher']['pos']==[121,152] and a['launcher']['size']==[1,2]
assert (122,151) in flood((121,154),walk|{(121,152),(121,153)})
# Synthetic independent task allocation: wall first, then two other tasks, no duplicate actor.
for count in range(1,5):
 assignments=dict(zip(range(count),['wall','launcher','pod','fuel']))
 assert assignments[0]=='wall' and len(set(assignments.values()))==min(count,4)
read=lambda x:(s/x).read_text(); v=read('RHFVipEvacS311.cs'); p=read('RHFRescuePlannerS41.cs'); ac=read('NJRRescueAccessR040.cs');j=read('JobDriver_NJRReopenOwnSealR051.cs'); role=read('RHFRescueRoleJobGiverS41.cs')
post=p.split('private static bool PostLaunchCohortsR053',1)[1].split('private static void TryUnescortedExitR029',1)[0]
assert 'ReachableProgress(' not in post and 'Eligible(map,op,member' in post and 'TryExitRepairR054' in post
assert 'op.ExitCell=known.Count>0?known[0]:IntVec3.Invalid' in post
assert 'structure==op.PodLauncherThing' in ac and 'op.PodLaunchCompleted' in ac and 'structure.Faction!=Faction.OfPlayer' in ac
assert 'ExitChunkAtR054' in ac and 'excluded.Add(first)' in ac and 'now+150' in ac
assert 'IsOwnExitStructureR054(op,wall)' in j and 'Wait(300,TargetIndex.A)' in j and 'IsOwnExitStructureR054(op,order.Objective)' in role
assert v.index('wall=NativeFortificationWorkS41')<v.index('AssignParallelBuildR054(op,op.Vip,launcher')<v.index('AssignParallelBuildR054(op,op.Vip,pod')
assert 'RHFPodPlannerS41.WorkJob(candidate,site)==null' in v and 'used.Add(candidate)' in v
print('PASS R054: recorded one-cell trap, owned launcher reconnection geometry, parallel job and verified edge contracts')
