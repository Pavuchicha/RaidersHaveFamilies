"""RHF S1 P0 invariants, with negative mutations. Not an AI simulation."""
from pathlib import Path
import re
R=Path(__file__).resolve().parents[1]
S=R/'RaidersHaveFamilies'/'Source'
ops=(S/'RHFOperations.cs').read_text('utf8')
cmd=(S/'RHFRescueCommandV124.cs').read_text('utf8')
host=(S/'RHFHostageSystems.cs').read_text('utf8')
carry=(S/'RHFCarryDowned.cs').read_text('utf8')

def method_body(src, marker):
  pos=src.index(marker);start=src.index('{',pos);depth=0
  for i in range(start,len(src)):
    if src[i]=='{':depth+=1
    if src[i]=='}':
      depth-=1
      if depth==0:return src[start+1:i]
  raise ValueError('Unbalanced method '+marker)

def test_case(s):
  f=method_body(s,'public RHFOperation FindRescueFor(string victimId)')
  assert '!string.IsNullOrEmpty(op.CaseKeyV10)' not in f
  assert 'op.Freed[j]?.ThingID==victimId' in f
  assert 'op.Targets[j]?.ThingID==victimId' in f
  assert 'if(!hasTargets && !hasFreed)continue' in f

def test_infil(s,c):
  m=method_body(s,'public override void MapComponentTick()')
  assert 'RHFRescueCommandV124.Infiltrate(this, op, now);' in m
  assert 'else Infiltrate(op, now)' not in m
  infil=method_body(c,'internal static void Infiltrate(RHFOperationController controller, RHFOperation op, int now)')
  assert '!string.IsNullOrEmpty(op.CaseKeyV10)' not in infil.split('\n',5)[0:5].__str__()
  assert '!string.IsNullOrEmpty(op.CaseKeyV10)) return;' not in infil
  assert 'SearchMissingVip(op, now);' in m

def test_extract(s,c,driver):
  m=method_body(s,'public override void MapComponentTick()')
  assert 'Extract(op, now)' not in m
  e=method_body(c,'internal static bool Extract(RHFOperationController controller, RHFOperation op, int now)')
  assert 'if(captive.Downed)' in e
  assert 'controller.TryAssignCarrier(op,captive)' in e
  assert 'if (op.Freed[i]!=null && op.Freed[i].Downed' not in e
  assert 'MissionExit(captive)' in e
  assert 'CompleteCarriedExtraction(pawn, rescued)' in driver
  assert 'rescued.ExitMap(' not in driver
  x=method_body(c,'internal static void CompleteCarriedExtraction(Pawn carrier, Pawn rescued)')
  assert 'MissionExit(rescued)' in x and 'MissionExit(carrier)' in x
  # S4.1 checks carrier departure first; a rejected patient exit must
  # NEVER claim success, and its escort assignment must be reset.
  assert 'if(carrier.MapHeld==map)return;' in x
  assert x.index('MissionExit(carrier)') < x.index('MissionExit(rescued)')
  assert 'if(rescued.MapHeld!=map)' in x and 'op.EscapedWithEscort++' in x
  assert 'link.Escort=null;' in x

def test_letters(s):
  body=method_body(s,'private static void StartRetaliation(RHFHostageRecord r)')
  assert 'LetterStack.ReceiveLetter(' not in body
  assert 'LetterDefOf.ThreatBig' not in body

test_case(ops);test_infil(ops,cmd);test_extract(ops,cmd,carry);test_letters(host)
# Prove each test can reject prior behavior (one mutation per P0).
for check,inputs in [
 (test_case,(ops.replace('if(!hasTargets && !hasFreed)continue;', 'if(!hasTargets && !hasFreed)continue;\n                if (!string.IsNullOrEmpty(op.CaseKeyV10)) continue;',1),)),
 (lambda o:test_infil(o,cmd),(ops.replace('RHFRescueCommandV124.Infiltrate(this, op, now);', 'Infiltrate(op, now);',1),)),
 (lambda d:test_extract(ops,cmd,d),(carry.replace('RHFRescueCommandV124.CompleteCarriedExtraction(pawn, rescued);', 'rescued.ExitMap(false, Rot4.Invalid);',1),)),
 (test_letters,(host.replace('            // No early combat letter (or early raid jump target) is emitted.', '            Find.LetterStack.ReceiveLetter("duplicate", "bad", LetterDefOf.ThreatBig, r.Pawn);',1),))]:
 try:check(*inputs)
 except AssertionError:pass
 else:raise AssertionError('mutation escaped S1 contract')
workflow=(R/'.github/workflows/build.yml').read_text('utf8')
assert 'python3 tests/rhf_135_s1_rescue_contracts.py' in workflow
print('PASS: case-keyed target finding, single infiltrate, physically carried authorized exit, single scheduled letter; four negative mutations')
