"""RHF S2.1 arrival-mode invariants. Static tests with negative mutations."""
from pathlib import Path
R=Path(__file__).resolve().parents[1]
S=R/'RaidersHaveFamilies/Source'
ops=(S/'RHFOperations.cs').read_text('utf8')
dbg=(S/'RHFDebugActions.cs').read_text('utf8')
wf=(R/'.github/workflows/build.yml').read_text('utf8')

def contract(o,d):
    assert 'if(op.TryPod)' in o and 'if(!dropRescue)' in o
    assert 'planned.Drop &&' in o and 'planned.Role==(int)RHFStrikeRole.Rescue' in o
    assert 'if (op.TryPod && party.Drop &&' in o
    assert 'bool successfulDropRescue=false;' in o
    assert 'if(!successfulDropRescue)' in o
    assert 'FallBackToLand(op,now,"no successful drop-rescue unit to construct transport")' in o
    assert 'scenario==RHFDebugScenario.FieldPod ||' in d
    assert 'drop: (scenario==RHFDebugScenario.FieldPod ||' in d
    assert 'executor.def.techLevel < TechLevel.Industrial' in d

contract(ops,dbg)
assert 'tests/rhf_138_air_only_pod_gate.py' in wf
for check,args in [
    (contract,(ops.replace('if(!dropRescue)','if(false)',1),dbg)),
    (contract,(ops.replace('if (op.TryPod && party.Drop &&','if (op.TryPod &&',1),dbg)),
    (contract,(ops,dbg.replace('drop: (scenario==RHFDebugScenario.FieldPod ||','drop: (scenario==RHFDebugScenario.Invalid ||',1))),
    (contract,(ops.replace('if(!successfulDropRescue)','if(false)',1),dbg)),
]:
    try:check(*args)
    except AssertionError:pass
    else:raise AssertionError('mutation incorrectly passed')
print('PASS: land pods forbidden, air-rescue-only field kits, industrial field-pod debug 04, successful air crew required to build; 4 negative mutations')
