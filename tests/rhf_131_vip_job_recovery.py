"""Regression contract for 2026-11-22 live FieldPod/VIP reports.
Static checks are not in-game mission simulations or successful C# compilation.
"""
from pathlib import Path
root=Path(__file__).resolve().parents[1]
src=root/'RaidersHaveFamilies/Source'
ops=(src/'RHFOperations.cs').read_text(encoding='utf-8')
rescue=(src/'RHFRescueCommandV124.cs').read_text(encoding='utf-8')
guard=(src/'RHFExitJobGuardV131.cs').read_text(encoding='utf-8')
medical=(src/'RHFPatches.cs').read_text(encoding='utf-8')
mission=(src/'RHFMissionIntegrityV130.cs').read_text(encoding='utf-8')
workflow=(root/'.github/workflows/build.yml').read_text(encoding='utf-8')
# After authentic release and BEFORE returning from FreeTarget, make the pawn
# an explicitly mission-controlled captive. Prevent vanilla ExitMap thrash.
assert 'RHFRescueCommandV124.StabilizeFreed(op,p);' in ops
assert ops.index('RHFRescueCommandV124.StabilizeFreed(op,p);') < ops.index('Pawn next = FindCaptive(op);')
assert 'RHFRescueCommandV124.StabilizeFreed(op,vip);' in ops
assert 'StabilizeFreed(op,op.Freed[j]);' in rescue
assert 'RebindFreed(op,captive);' in rescue
assert 'MissionKey == key' in rescue
assert 'JobGiver_ExitMapBest' in guard and 'JobGiver_ExitMapPanic' in guard
assert 'IsControlledFreed(pawn)' in guard
assert '__result = null;' in guard and 'return false;' in guard
assert 'AuthorizedExits.Contains' in rescue and 'MissionExit' in rescue
# Rescue pawns can be freed by vanilla without calling RHF FreeTarget.
assert 'RecoverReleasedTargets(op,map,now);' in rescue
assert 'op.Freed.Add(p);' in rescue
assert 'LengthHorizontalSquared<=144' in rescue
assert 'op.Freed.Contains(op.Vip)' in rescue
assert 'op.Phase==3 ?' in rescue
# VIP build must expose true state and fallback, not silently hang.
assert 'no uncovered buildable double footprint' in ops
assert 'Log.Message("[RHF][V131][Pod] Construction site="' in ops
assert 'Log.Message("[RHF][V131][Pod] Built="' in ops
assert 'op.PodSuppliesIssued' in mission and 'op.PodConstructionStage' in mission
assert 'Переходит к наземному отходу'.lower() in ops.lower()
assert 'steel-first>ThingDefOf.Steel.stackLimit' in ops
# WCE2 uses parameter names "victim" and "torturer", unlike RHF's former
# "pawn" and "billDoer". Harmony positional args avoid name binding failure.
assert 'Pawn __0, Pawn __2' in medical
assert 'Pawn pawn=__0, billDoer=__2;' in medical
assert 'Pawn pawn, Pawn billDoer' not in medical
assert 'python3 tests/rhf_131_vip_job_recovery.py' in workflow
assert 'RaidersHaveFamilies-RimWorld-1.7-v0.13.1-VIP-ESCORT-RECOVERY-TEST' in workflow
# Deterministic state-machine sanity: independent walk-away must not terminate
# an active scripted pod mission.  The physical escort/pod completes it.
def allowed_independent_exit(is_freed,live,pod_finished,authorized):
    if not is_freed or not live or pod_finished or authorized: return True
    return False
assert not allowed_independent_exit(True,True,False,False)
assert allowed_independent_exit(True,True,False,True)
assert allowed_independent_exit(True,False,False,False)
print('PASS: VIP mission authority, prevented ExitMap job storm, auto-recovery, construction visibility, WCE2 positional hooks, CI label')
