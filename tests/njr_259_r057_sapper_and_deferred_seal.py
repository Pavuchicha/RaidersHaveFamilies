"""R057: recorded behavior and source contracts; not a RimWorld execution test."""
import json
from pathlib import Path
r=Path(__file__).resolve().parents[1];s=r/'RaidersHaveFamilies/Source';a=json.loads((r/'tests/fixtures/r057_runtime.json').read_text())
assert a['revision']=='R056' and a['weapons']['36765']==['Unay','MeleeWeapon_BreachAxe']
assert a['breach'][0][:2]==[39516,36744] and a['weapons']['36744'][1]=='Gun_HeavySMG'
assert [t for t,m in a['logs'] if 'Real enemy wall blueprint' in m]==[39696,44376]
assert [t for t,m in a['logs'] if 'Freed=' in m]==[41676]
assert [t for t,m in a['logs'] if 'physically cleared drop debris' in m and t==42289]==[42289]
assert [44313,'PodLauncher'] in a['build'] and len({p for _,p in a['exits']} & {int(p) for p in a['weapons']})==5
w=(s/'NJREarlyWingR053.cs').read_text();ac=(s/'NJRRescueAccessR040.cs').read_text();v=(s/'RHFVipEvacS311.cs').read_text();de=(s/'NJRVipDebrisR045.cs').read_text();o=(s/'RHFOperations.cs').read_text()
assert 'score-=100000' in w and 'p.CanReach(op.BoardS41.Barrier' in w
assert '(p==preferred?500:0)' in ac and 'RHFTactical.IsBreachAxe(p)?100000:0' in ac
assert 'clearanceOnly:true' in v and 'if(!clearanceOnly && op.Phase==1 && !EarlySealSafeR051' in v
assert 'op.DeferredSealClearR057=deferred;cargo=chunk;' in v and 'thing.Position==op.DeferredSealClearR057' in de
assert 'Scribe_Values.Look(ref DeferredSealClearR057' in o
assert 'NativeEngineerS311(op,target,engineer,refueler,prepare:false,pocket:readyPocket)' in v
assert 'early && NativeAliveS311(engineer,map) && !pocket.Contains(engineer.Position)' in v
print('PASS R057: recorded gunner breach, delayed second wall, all five exits; sapper priority and deferred clearance source contracts')
