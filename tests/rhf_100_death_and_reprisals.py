"""Static contracts for v0.10+ dead-hostage and reprisal features.
Updated for v0.12.2 remote family communication (no local teleportation).
"""
from pathlib import Path
r=Path(__file__).resolve().parents[1]
src=r/'RaidersHaveFamilies/Source'
h=(src/'RHFHostageSystems.cs').read_text()
d=(src/'RHFDialogue.cs').read_text()
p=(src/'RHFPatches.cs').read_text()
g=(src/'RaidersHaveFamilies.cs').read_text()
o=(src/'RHFOperations.cs').read_text()
x=(src/'RHFReprisals.cs').read_text()
assert 'public bool PlayerCausedDeath;' in h
assert 'public bool BodyButchered;' in h and 'BodyConsumedDirectly' in h
assert 'public static bool IsDeadCase(' in h
assert 'if (IsDeadCase(r)) continue;' in h
assert 'ResolveDeadHostage(RHFHostageRecord r, int answer)' in h
assert 'ledger.Hostages.Remove(r);' in h
assert 'RHFReprisalCause.ConcealedVip' in h and 'RHFReprisalCause.Cannibalism' in h
assert 'ReportExecution(' not in h
assert 'PlayerCausedDeath = causedByPlayer || execution' in h
assert 'if (r.Importance >= 2 && causedByPlayer) QueueRetaliation(r);' not in h
assert 'CanConfessCannibalism(r)' in d
assert 'Промолчать и закрыть дело RHF' in d
assert 'if (RHFHostageDiplomacy.IsDeadCase(r)) return DeadPerson' in d
assert 'Признаться, что мы умышленно убили пленника' in d
# Actual remote connection via faction operator, not kin standing in an office.
assert 'Оператор фракции устанавливает удалённую связь' in d
assert 'RHFDeadButcheredEvent' in p and 'Corpse.ButcherProducts' in p
assert 'RHFDeadConsumedEvent' in p and 'Thing.Ingested' in p
assert 'RecordHumanMeatConsumption' in p and 'CompIngredients' in h
assert 'LastHumanMeatConsumptionTick' in g and 'ReprisalCases' in g
assert 'RHFReprisalSystem.Update(this, tick)' in g
assert 'Scribe_Collections.Look(ref ReprisalCases' in g
assert 'Scribe_Values.Look(ref LastHeldCell' in h
assert 'Scribe_Values.Look(ref CaseKeyV10' in o
assert 'RHFReprisalSystem.OnOperationDeployed(op)' in o
assert 'RHFReprisalSystem.OnOperationFailed(op)' in o
assert 'SearchMissingVip(op, now)' in o
assert 'FindRescueFor(string victimId)' in o
assert 'ConfirmDisappearance(c' in x
assert 'c.DueTick = now + Rand.Range(15000, 135000)' in x
assert 'RHFStrikeRole.Diversion' in x and 'RHFStrikeRole.Vengeance' in x
assert '15000f' in x and 'StorytellerUtility.DefaultThreatPointsNow(map)' in x
assert 'Stage == 4' in x
assert 'Mathf.Log(1f + newCruelty' not in h
for threshold in ('18f','40f','85f','170f','310f'):
    assert threshold in h
assert 'kind == RHFPublicityKind.Cannibalism' in h
assert 'MemoryLevel(newCruelty, importance)' in h
assert '22 — Аудит смерти' in (src/'RHFDebugActions.cs').read_text()
assert '23 — Аудит тайных дел' in (src/'RHFDebugActions.cs').read_text()
def memory(cruelty, rank):
    score = max(0,cruelty) * (1+min(max(rank,0),8)*.20)
    return next((i for i,threshold in enumerate((18,40,85,170,310),start=1) if score < threshold),6)
assert memory(6,0)==1
assert memory(6,8)==1
assert memory(12,8)==2
assert memory(30,8)==3
assert memory(60,8)==4
assert memory(135,8)==6
assert memory(340,0)==6
print('PASS: dead hostage dialogue, exclusive resolution, cannibal proof, and remote family communication')
