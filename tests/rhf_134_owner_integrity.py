"""RHF S0/S1 single-owner contracts. Static tests; not RimWorld simulation."""
from pathlib import Path
import re
ROOT=Path(__file__).resolve().parents[1]
SRC=ROOT/'RaidersHaveFamilies'/'Source'
read=lambda file:(SRC/file).read_text('utf8')

def strip_comments(text):
    """Strip line/block comments, while preserving C# strings and newlines."""
    out=[];i=0;mode='code'
    while i<len(text):
        c=text[i];d=text[i+1] if i+1<len(text) else ''
        if mode=='code':
            if c=='/' and d=='/': mode='line';out.extend('  ');i+=2;continue
            if c=='/' and d=='*': mode='block';out.extend('  ');i+=2;continue
            if c=='"': mode='string';out.append(c);i+=1;continue
            out.append(c);i+=1;continue
        if mode=='line':
            if c=='\n':mode='code';out.append(c)
            else:out.append(' ')
            i+=1;continue
        if mode=='block':
            if c=='*' and d=='/':mode='code';out.extend('  ');i+=2;continue
            out.append('\n' if c=='\n' else ' ');i+=1;continue
        if mode=='string':
            out.append(c)
            if c=='\\' and d:out.append(d);i+=2;continue
            if c=='"':mode='code'
            i+=1
    return ''.join(out)

def validate_lord_guards(text,filename):
    lines=strip_comments(text).splitlines()
    matches=0
    for i,line in enumerate(lines):
        if 'Notify_PawnLost(' not in line:continue
        matches+=1
        previous='\n'.join(lines[max(0,i-2):i])
        found=re.search(r'if\s*\(\s*(\w+)\s*!=\s*null\s*&&\s*!\(\s*\1\s*\.\s*LordJob\s+is\s+RHFRescueLordJobV124\s*\)\s*\)',previous,re.S)
        assert found,f'{filename}:{i+1} unguarded mission Lord detach'
        assert re.search(r'\b'+found.group(1)+r'\.Notify_PawnLost\s*\(',line), filename+': wrong Lord guard'
    assert matches>0,filename+': Notify_PawnLost unexpectedly missing'

def attribute_patched_classes(source):
    source=strip_comments(source)
    # Multiline and bare attributes, including TargetMethod(s)-based hooks.
    return re.findall(r'\[\s*HarmonyPatch(?:\s*\([^\]]*?\))?\s*\]\s*(?:(?:\[[^\]]*\]\s*)|(?:\s*))*?(?:internal|public|private)\s+(?:static\s+)?class\s+(\w+)',source,re.S)

ops,cmd,old=read('RHFOperations.cs'),read('RHFRescueCommandV124.cs'),read('RHFRescueControlV123.cs')
validate_lord_guards(ops,'RHFOperations.cs')
validate_lord_guards(cmd,'RHFRescueCommandV124.cs')
assert attribute_patched_classes(old)==['RHFRescueExitPatchV123'],attribute_patched_classes(old)
assert attribute_patched_classes(cmd)==['RHFRescueExitV124'],attribute_patched_classes(cmd)
# OnDeploy order and MapComponentTick caller. All text comments stripped.
a=strip_comments(cmd);start=a.index('internal static void OnDeploy(');end=a.index('internal static void Refresh(',start);od=a[start:end]
assert od.index('ClaimGroup(op, MapFor(controller));')<od.index('StabilizeFreed(op, op.Freed[j]);')
assert 'j < MaxTargets' not in od and 'j < op.Freed.Count' in od
b=strip_comments(ops);start=b.index('public override void MapComponentTick()');end=b.index('private void DeployVengeanceSupport(',start)
assert 'RHFRescueCommandV124.OnDeploy(this, op, true);' in b[start:end]
# Negative test: deletion of a guard CANNOT be masked by a comment mentioning the class.
mutated=re.sub(r'if \(oldLord != null && !\(oldLord.LordJob is RHFRescueLordJobV124\)\)',
               'if (oldLord != null)',ops,count=1)
assert mutated!=ops
try:validate_lord_guards(mutated,'mutation')
except AssertionError:pass
else:raise AssertionError('deleted guard falsely accepted')
# Negative test: multiline Harmony hook is visible.
assert 'Bad' in attribute_patched_classes(old+'\n[HarmonyPatch(\n typeof(Pawn), nameof(Pawn.SpawnSetup)\n)]\ninternal static class Bad {}')
assert 'Bad2' in attribute_patched_classes(old+'\n[HarmonyPatch]\ninternal static class Bad2 { private static IEnumerable<System.Reflection.MethodBase> TargetMethods() { yield break; } }')
assert 'python3 tests/rhf_134_owner_integrity.py' in (ROOT/'.github/workflows/build.yml').read_text('utf8')
print('PASS: stripped-comment single Lord owner guards, multiline Harmony detection, negative mutations, rebind ordering')
