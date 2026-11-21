from pathlib import Path
root=Path(__file__).resolve().parents[1]
ops=(root/'RaidersHaveFamilies/Source/RHFOperations.cs').read_text(encoding='utf-8')
tactical=(root/'RaidersHaveFamilies/Source/RHFTactical.cs').read_text(encoding='utf-8')
for key in ['HuntLastSeenCell', 'HuntLastSeenTick', 'rhfHuntLastSeenCellV121',
            'rhfHuntLastSeenTickV121','GenSight.LineOfSight', 'HoldFreedUntilExtraction(op)',
            'private void HoldFreedUntilExtraction(', 'if (op.PodLaunchCompleted && captive == op.Vip) continue;']:
    assert key in ops, f'missing tactical fix: {key}'
assert ops.index('if (op.PodLaunchCompleted && captive == op.Vip) continue;') < ops.index('if (captive == null || captive.Dead || captive.MapHeld != map) continue;'), 'VIP skyfaller regression: should check before MapHeld'
assert ops.count('HuntLastSeenCell') >= 5
assert 'GenSight.LineOfSight(fighter.Position, possible.Position, map)' in tactical
assert 'MaxSquad = 96' in ops and 'DecisionInterval = 450' in ops
for key in ['Scribe_References.Look(ref HuntTarget', 'Scribe_Collections.Look(ref Escorts',
            'PodLaunchCompleted', 'PodBudgetPaid', 'RHFReprisalSystem.OnOperationDeployed']:
    assert key in ops, 'missing persistence/missions: '+key
wf=(root/'.github/workflows/build.yml').read_text()
assert 'Build Raiders Have Families 0.12.1' in wf
assert 'tests/rhf_121_coherent_ops.py' in wf
assert 'RaidersHaveFamilies-RimWorld-1.7-v0.12.1-TACTICAL-CAMPAIGNS-TEST' in wf
assert 'REQUIREMENTS_AUDIT_MASTER_RU.md' in {x.name for x in root.iterdir()}
print('PASS: v0.12.1 rescued VIP cargo, multi-hostage staging, non-omniscient hunt, scoped LOS support, saved state, CI')
