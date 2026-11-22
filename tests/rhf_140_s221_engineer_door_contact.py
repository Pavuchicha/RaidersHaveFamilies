"""S2.2.1 contracts: no competing VIP construction jobs; closed prison doors;
fortification contact includes mechanized defenses. T1 only, NOT a game test.
"""
import re
from pathlib import Path
R=Path(__file__).resolve().parents[1]
SOURCE=R/'RaidersHaveFamilies'/'Source'
ops=(SOURCE/'RHFOperations.cs').read_text('utf-8')
stage=(SOURCE/'RHFStagedAdvanceV139.cs').read_text('utf-8')
ci=(R/'.github/workflows/build.yml').read_text('utf-8')

def body(code,method):
    pattern=r'\b'+re.escape(method)+r'\s*\([^)]*\)\s*\{'
    m=re.search(pattern,code)
    assert m, f'{method} missing'
    start=m.end()-1
    depth=0
    for i in range(start,len(code)):
        if code[i]=='{':depth+=1
        elif code[i]=='}':
            depth-=1
            if depth==0:return code[start:i+1]
    raise AssertionError(f'{method} unterminated')

def door_check(source):
    b=body(source,'OpenRouteExcept')
    assert 'next==excluded' in b, 'candidate wall not blocked in preview'
    assert 'if(!next.Standable(map) &&' in b, 'nonwalkable blocker not checked'
    assert 'next.GetEdifice(map) is Building_Door' in b, 'closed prison doors must be considered'
    assert 'visited.Add(next);pending.Enqueue(next);' in b

def engineer_check(source):
    b=body(source,'BuildAndLaunch')
    beg=b.index('bool fortificationOpen=')
    bar=b.index('TryBuildPrisonBarricade(op,vip,now)')
    check=b.index('if(op.BarricadeCell.IsValid || op.BarricadesBuilt>before)')
    pause=b.index('op.PodConstructionTick=0;',check)
    release=b.index('op.BarricadeCell=IntVec3.Invalid;',check)
    deadline=b.index('now-op.PhaseStartTick<3000',beg)
    build=b.index('Pawn engineer=FieldEngineer(op,site)')
    assert beg<deadline<bar<check<pause<release<build
    assert 'return;' in b[pause:release], 'construction must not override barricade orders'
    assert 'else if(op.BarricadeCell.IsValid)' in b[check:build], 'deadline must release engineer'
    assert 'op.FortifyStartedTick=0;' in b[release:build]
    assert 'int before=op.BarricadesBuilt;' in b[beg:check]
    assert 'op.BarricadesBuilt>before' in b[check:release], 'completed wall must finish its tick first'

def contact_check(source):
    b=body(source,'Holding')
    range_body=body(source,'ContactRange')
    assert 'map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer)' in b
    assert 'GenHostility.HostileTo(leader,defender)' in b
    assert 'op.Targets.Contains(defender)' in b
    assert 'map.listerBuildings.allBuildingsColonist' in b
    assert 'RHFCombatPlannerV125.IsTurret(turret)' in b
    assert 'GenSight.LineOfSight(leader.Position,turret.Position,map)' in b
    assert 'RHFObjectiveRouteV128.StagingExposedToTurret(map,leader.Position)' in b
    assert 'ContactRange(contact)' in b
    assert 'nearest-contactBuffer' in b, 'staging must be outside contact weapon range'
    assert 'target is Building' in range_body
    assert 'turret.def?.building?.turretGunDef?.Verbs' in range_body

for func,src in ((door_check,ops),(engineer_check,ops),(contact_check,stage)):
    func(src)
assert 'python3 tests/rhf_140_s221_engineer_door_contact.py' in ci

negative=[
    ('door erased',door_check,ops.replace('next.GetEdifice(map) is Building_Door',
        'false',1)),
    ('no emergency stop',engineer_check,ops.replace(
        'if(op.BarricadeCell.IsValid || op.BarricadesBuilt>before)',
        'if(false)',1)),
    ('no deadline',engineer_check,ops.replace(
        'now-op.PhaseStartTick<3000;',
        'true;',1)),
    ('turrets ignored',contact_check,stage.replace(
        'RHFCombatPlannerV125.IsTurret(turret)',
        'false',1)),
    ('blind staging',contact_check,stage.replace(
        'nearest-contactBuffer',
        'nearest-12',1)),
]
for label,fn,mutation in negative:
    try:fn(mutation)
    except (AssertionError, ValueError):continue
    raise AssertionError('negative mutation escaped: '+label)
print('PASS: 3 S2.2.1 control contracts + 5 negative mutations; NOT C# / RimWorld test')
