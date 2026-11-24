"""Reproduce actual R030 traces + R031 source constraints. Not a C# runtime test."""
from pathlib import Path
import json, xml.etree.ElementTree as ET
R=Path(__file__).resolve().parents[1]; S=R/'RaidersHaveFamilies/Source'
records=json.loads((R/'tests/fixtures/r031_runtime_extract.json').read_text())
assert sum(r['count'] for r in records)==33919
assert [r['release']-r['spawn'] for r in records]==[4230,11970,3240]
assert all(len(r['exit'])==1 for r in records)
assert [len(r['return_jobs']) for r in records]==[1,6,5]
assert all(any(j['source']=='RimWorld.JobGiver_AITrashBuildingsDistant' for j in r['initial_jobs']) for r in records)
trace=records[1]['stuck_breach']; assert len(trace)>50
assert len({(tuple(x['pos']),x['hp'],x['job_id']) for x in trace})==1
assert trace[0]['hp']==300 and trace[-1]['tick']-trace[0]['tick']>7000
# Independent watchdog policy replay. Same position and HP cannot occupy permit forever.
last=trace[0]; expiry=next(x['tick'] for x in trace if x['tick']-last['tick']>=600)
assert expiry-last['tick']<=720
# No structural damage without the original board permit; no returning to duty focus.
xml=ET.parse(R/'RaidersHaveFamilies/Defs/DutyDefs/RHF_MissionHold.xml')
assert all('WanderNearDutyLocation' not in n.get('Class','') for n in xml.iter())
j=(S/'RHFRescueRoleJobGiverS41.cs').read_text(); p=(S/'RHFRescuePlannerS41.cs').read_text(); c=(S/'RHFRescueCommandV124.cs').read_text(); route=(S/'RHFObjectiveRouteV128.cs').read_text()
assert 'return Task(JobDefOf.Wait,pawn.Position,30);' in j.split('private Job MissionJob')[0]
assert 'newlyClaimedR031.Contains(p)' in c and 'running.def.defName=="Ignite"' in c and 'running.def.defName=="UseVerbOnThing"' in c
assert 'state.Position!=actor.Position || state.HP!=obstacle.HitPoints' in p
assert 'if(now-state.Changed<600)return;' in p and 'state.Failed[actor]=now+1800;' in p
assert 'board.Breacher=replacement??actor;' in p
assert 'LengthHorizontalSquared!=1' in j and 'IntVec3[] offsets={IntVec3.North,IntVec3.South,IntVec3.East,IntVec3.West};' in j
assert 'pawn.Position!=order.Cell' in j
assert 'LengthHorizontalSquared>36' in p and 'IntVec3 approach=RHFObjectiveRouteV128.Waypoint(route,soldier,map);' in p
assert 'if(++inspected>48)break;' in route and 'if(++probes>12)break;' in route
print('PASS R031: three actual log extracts, watchdog replay and job/route source constraints; NOT gameplay.')
