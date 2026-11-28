"""R061 recorded R060 plateau, one fresh native baseline, mandatory kit first.
Arithmetic and source integration contracts only; not execution of Verse APIs."""
from pathlib import Path
import json, math
p=Path(__file__).resolve().parents[1];s=p/'RaidersHaveFamilies'/'Source'
a=json.loads((p/'tests/fixtures/r061_runtime.json').read_text())
assert a['actual_wealth'] is None
for v in a['vip']:
    assert abs(v['vanilla']*1.92-v['allocated'])<.1
    assert abs(v['allocated']-v['supplies']-v['gear']-v['native'])<.1
    assert v['native']<v['pod_required'] and not v['sealsAffordable']

def new(gross,core,kit):
    # Simplified eligible kit incl. seals; no free equipment or extra points.
    if gross<core+kit:return None
    supply=min(300,.12*gross,gross-core-kit)
    gear=min(.23*gross,3600,max(0,gross-kit-supply-max(350,core))) if gross>=650 else 0
    native=gross-kit-supply-gear
    return supply,gear,native
# The old planner rejects an affordable mission by reserving optional gear first.
for gross in (1000,1100,1200):
    assert gross*.65<968
    result=new(gross,350,640)
    assert result is not None and result[2]>=350-1e-7
    assert math.isclose(sum(result)+640,gross)
assert new(922.3,350,640) is None # no disguised test/point top-up
for gross in (990,1100,1500,5000,30000):
    result=new(gross,350,640)
    assert result[2]>=350-1e-7 and math.isclose(sum(result)+640,gross)
ops=(s/'RHFOperations.cs').read_text();threat=(s/'NJRThreatBudgetS41.cs').read_text();comp=(s/'NJRForceCompositionS42.cs').read_text()
refresh=threat.split('internal static float RefreshVanillaThreat',1)[1].split('internal static float FactionAllocation',1)[0]
assert refresh.index('DefaultThreatPointsNow(map)')<refresh.index('ForceRecount(false)')<refresh.rindex('DefaultThreatPointsNow(map)')
assert 'wealthBefore=' in refresh and 'wealthAfter=' in refresh and 'storytellerWealth=' in refresh and 'wealthIndependent=' in refresh
assert ops.count('NJRThreatBudgetS41.RefreshVanillaThreat(map)')==1
assert ops.index('RefreshVanillaThreat(map)')<ops.index('AllocateOperation(map,op,freshVanilla)')<ops.index('party.Attempted = true;')
assert 'FactionAllocation(map,op,group.Key,vanillaSnapshot)' in threat
assert 'vanillaSnapshot ?? StorytellerUtility.DefaultThreatPointsNow(map)' in threat
kit=ops.index('if (op.TryPod && party.Drop &&'); opt=ops.index('NJRForceCompositionS42.Reserve(op,party);')
assert kit<ops.index('effectivePoints-=cost;',kit)<opt<ops.index('points = effectivePoints')
assert 'party.Points-rosterFloor-party.PodCostR060' in ops
assert 'party.Points-party.PodCostR060-party.SupplyBudget-core' in comp
refusal=ops.split('[R061][PodRejected]',1)[1].split('op.TryPod=false;op.FortifyPrison=false;',1)[0]
assert 'if(op.DebugForced)' in refusal and 'budgetRejected=true;' in refusal and 'continue;' in refusal
assert 'AuditArrival(party,pawns,parms)' in (s/'NJRForceStrengthR017.cs').read_text()
assert 'AuditArrival(wing,pawns,parms)' in (s/'RHFRescueGroundEntryS41.cs').read_text()
assert 'public static void AuditThreatR061()' in (s/'RHFDebugActions.cs').read_text()
print('PASS R061: logged equal vanilla baseline, fresh wealth before allocation, mandatory kit before gear, explicit rejected test, native arrival costs; game execution UNVERIFIED')
