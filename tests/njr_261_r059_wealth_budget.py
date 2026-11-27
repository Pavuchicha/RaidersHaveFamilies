"""R059: recorded equal-budget R058 raids, new strict storyteller budget contracts.
Python arithmetic and static C# contracts, NOT an engine integration test."""
from pathlib import Path
import json
r=Path(__file__).resolve().parents[1];s=r/'RaidersHaveFamilies/Source'
a=json.loads((r/'tests/fixtures/r059_runtime.json').read_text())
assert a['revision']=='R058'
assert [i['party_points'] for i in a['pod_raids']]==[1395.5,1395.5]
assert [i['members'] for i in a['pod_raids']]==[5,5]
assert [i['native_points'] for i in a['pod_raids']]==[350,350]
assert all(i['wealth'] is None for i in a['pod_raids'])
# Reproduce the old plateau using the recorded material costs, before generation.
def old_pod(base):
    gross=max(1200,base);supply=gross*.12;gear=gross*.23
    remaining=gross-supply-gear
    topup=max(0,625.5+350-remaining)
    return gross+topup,remaining+topup-625.5
assert old_pod(300)==old_pod(1100)==(1395.5,350)
# Same rank/faction/settings; no hidden budget inflation or NJR ceiling.
def new_budget(vanilla,rank=0,captives=1,pressure=0,strength=1):
    return max(1,vanilla*(1+.1*min(8,max(0,rank))+.12*min(6,max(0,captives-1))+.005*min(100,max(0,-pressure)))*min(2,max(.5,strength)))
def reserve(gross):
    supply=min(300,.12*gross)
    gear=min(.23*gross,3600,max(0,gross-supply-350)) if gross>=650 else 0
    return supply,gear,gross-supply-gear
for p in (100,300,600,1100,1200,1500,3000,9000,15000,30000):
    gross=new_budget(p,rank=4);sup,gear,native=reserve(gross)
    pod_cost=625.5 if native>=625.5+350 and gross*.1>=7.5 else 0
    available=native-pod_cost
    for cp in (35,60,120,700):
        roster=int(available//cp)
        assert roster*cp+pod_cost+sup+gear<=gross+1e-7
assert new_budget(300)<new_budget(1100)
assert reserve(new_budget(30000))[2]>15000
force=(s/'NJRForceStrengthR017.cs').read_text();ops=(s/'RHFOperations.cs').read_text();debug=(s/'RHFDebugActions.cs').read_text();threat=(s/'NJRThreatBudgetS41.cs').read_text();seals=(s/'NJRSealBudgetR052.cs').read_text()
assert 'StorytellerUtility.DefaultThreatPointsNow(map)' in threat
assert 'total=Mathf.Max(total,1200f)' not in debug
assert 'CommitMissionBudget' not in force+ops
assert 'party.Points+=' not in force+ops and 'op.ThreatPoints+=' not in force+ops
assert 'if(cost>unused)break;' in force and 'if(cost>spare)break;' in force and 'if(kind.combatPower>unused)' in force
assert 'effectivePoints>=required' in ops and 'effectivePoints-=cost' in ops
assert 'points = effectivePoints' in ops and 'Mathf.Min(effectivePoints, 15000f)' not in ops
assert 'if(cost>discretionary)continue;' in seals and 'cheapest' not in seals
assert '[R059][Threat]' in threat and '[R059][Budget]' in ops
print('PASS R059: recorded equal budget, strict allocation, affordable roster and engineering supplies; native runtime UNVERIFIED')
