"""S2.2 safety/formation source contracts + deterministic negative mutations.
This is NOT C# compilation or gameplay verification.
"""
from pathlib import Path
R=Path(__file__).resolve().parents[1]
S=R/'RaidersHaveFamilies/Source'
ops=(S/'RHFOperations.cs').read_text('utf-8')
rescue=(S/'RHFRescueCommandV124.cs').read_text('utf-8')
combat=(S/'RHFCombatPlannerV125.cs').read_text('utf-8')
assembly=(S/'RHFStagedAdvanceV139.cs').read_text('utf-8')
ci=(R/'.github/workflows/build.yml').read_text('utf-8')

def method_body(code,name):
    import re
    m=re.search(r'\b'+re.escape(name)+r'\s*\([^)]*\)\s*\{',code)
    assert m, f'cannot find {name}'
    start=m.end()-1; level=0
    for i in range(start,len(code)):
        if code[i]=='{':level+=1
        if code[i]=='}':
            level-=1
            if level==0:return code[start:i+1]
    raise AssertionError('unterminated function '+name)

def fortification_contract(o):
    b=method_body(o,'TryBuildPrisonBarricade')
    topology=method_body(o,'BarricadeLeavesExit')
    for token in ('op.PodLauncherCell','op.PodCell','BarricadeLeavesExit(op,vip,cell)',
                  'BarricadeWorkCell(engineer,op.BarricadeCell)',
                  'BarricadeLeavesExit(op,vip,op.BarricadeCell)',
                  'wall.Destroy()', 'vip.CanReach(op.PodCell'):
        assert token in b or token in topology,token
    assert 'IntVec3 center=op.PodLauncherCell;' in b
    assert 'GetRoom(map)!=room' not in b
    assert 'doorCell' not in b  # no wall inside a single-exit prison
    assert 'OpenRouteExcept(vip.Position,op.PodCell,candidate)' in topology
    assert 'OpenRouteExcept(engineer.Position,op.PodLauncherCell,candidate)' in topology
    assert 'next==excluded' in method_body(o,'OpenRouteExcept')

def breach_contract(r,c):
    b=method_body(c,'SelectBreachOperator')
    attacker=method_body(c,'BreachAttack')
    infiltrate=method_body(r,'Infiltrate')
    assert 'HasBreachKit(pawn)' in b
    assert 'weapon.IsMeleeWeapon' in b
    assert 'grenade' not in attacker.lower().replace('"grenade"','') or '"Grenade"' in attacker
    assert 'IndexOf("Grenade"' in attacker
    assert 'rescuer!=breachOperator || assignedBreaches>0' in infiltrate
    assert 'RHFCombatPlannerV125.BreachAttack(rescuer)' in infiltrate
    assert '(needsFireSuppression ? 15000 : 27000)' in infiltrate

def formation_contract(a,r):
    assert 'AssemblyDeadline=2400' in a
    assert 'ConditionalWeakTable<RHFOperation,RHFAssemblyStateV139>' in a
    assert 'column=slot%5-2' in a and 'rank=slot/5' in a
    assert 'ready>=Math.Max(3,(live*3+3)/4)' in a
    assert 'now-state.StartTick>=AssemblyDeadline' in a
    assert 'RHFStagedAdvanceV139.Holding(' in r
    assert 'if(gathering)' in r
    assert 'assemblySlots.TryGetValue(' in r

fortification_contract(ops)
assert 'LateralExposed(map,soldier.Position,lane)' in (S/'RHFObjectiveRouteV128.cs').read_text('utf8')
breach_contract(rescue,combat)
formation_contract(assembly,rescue)
assert 'python3 tests/rhf_139_assembly_breach_fortify.py' in ci
for name, f, arg in (
    ('blocked VIP route',fortification_contract,(ops.replace('OpenRouteExcept(vip.Position,op.PodCell,candidate)','true',1),)),
    ('wrong wall location',fortification_contract,(ops.replace('op.PodLauncherCell;\n                for(int radius','vip.Position;\n                for(int radius',1),)),
    ('breach by everybody',lambda x:breach_contract(x,combat),(rescue.replace('rescuer!=breachOperator || assignedBreaches>0','assignedBreaches>4',1),)),
    ('infinite staging',lambda x:formation_contract(x,rescue),(assembly.replace('now-state.StartTick>=AssemblyDeadline','false',1),)),
):
    try:f(*arg)
    except AssertionError:pass
    else:raise AssertionError('negative mutation escaped: '+name)
print('PASS: 4 mission contracts, 4 negative mutations; not a game test')
