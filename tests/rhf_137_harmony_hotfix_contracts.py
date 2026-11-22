"""S2.1 CI contracts; static only. Negative checks include reverting P0/P1 fixes."""
from pathlib import Path
R=Path(__file__).resolve().parents[1]
S=R/'RaidersHaveFamilies/Source'
exit_guard=(S/'RHFExitJobGuardV131.cs').read_text('utf8')
boot=(S/'RHFPatches.cs').read_text('utf8')
defence=(S/'RHFRescueFireDisciplineV136.cs').read_text('utf8')
route=(S/'RHFObjectiveRouteV128.cs').read_text('utf8')
combat=(S/'RHFCombatPlannerV125.cs').read_text('utf8')
ops=(S/'RHFOperations.cs').read_text('utf8')
wf=(R/'.github/workflows/build.yml').read_text('utf8')

def mandatory(g,b):
    assert '[HarmonyPatch' not in g, 'an optional job-giver hook must never break PatchAll'
    assert 'internal static int Install(Harmony harmony)' in g
    assert 'harmony.Patch(target,prefix:new HarmonyMethod(prefix))' in g
    assert 'parameters.Length==1' in g and 'typeof(Pawn)' in g and 'typeof(Job)' in g
    assert 'nameof(Prefix)' in g and 'Pawn __0' in g
    assert 'harmony.CreateClassProcessor(type).Patch();' in b
    assert 'catch(Exception e)' in b and 'FAILED class' in b
    assert 'harmony.PatchAll(' not in b
    assert 'RHFControlledFreedExitJobV131.Install(harmony);' in b

def combat_responses(d,c):
    assert 'map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer)' in d
    assert 'RHFCombatPlannerV125.IsTurret(turret)' in d
    assert 'job.expiryInterval=240;' in d and 'job.checkOverrideOnExpire=true;' in d
    assert 'UnsafeLine(op,pawn,defender)' in d and 'UnsafeLine(op,pawn,turret)' in d
    assert 'pawn.GetPosture()!=PawnPosture.Standing' in c
    assert 'pawn.CurJobDef==JobDefOf.Wait_Combat' in c

def routes_and_drop(r,o):
    assert 'lane.GetFirstPawn(map)==null' in r
    assert 'IsAllowedWaypoint(route,lane)' in r
    assert '!CrossesKnownFire(route,soldier.Position,lane,map)' in r
    assert 'recorded.UnionWith(presentBeforeRaid)' in o

mandatory(exit_guard,boot); combat_responses(defence,combat);routes_and_drop(route,ops)
assert 'tests/rhf_137_harmony_hotfix_contracts.py' in wf
for check,args in [
    (lambda g:mandatory(g,boot),(exit_guard.replace('internal static int Install(Harmony harmony)','internal static int InstallDisabled(Harmony harmony)',1),)),
    (lambda b:mandatory(exit_guard,b),(boot.replace('harmony.CreateClassProcessor(type).Patch();','harmony.PatchAll(typeof(RHFPatchInit).Assembly);',1),)),
    (lambda d:combat_responses(d,combat),(defence.replace('map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer)','map.mapPawns.FreeColonistsSpawned',1),)),
    (lambda c:combat_responses(defence,c),(combat.replace('pawn.GetPosture()!=PawnPosture.Standing','false',1),)),
    (lambda r:routes_and_drop(r,ops),(route.replace('!CrossesKnownFire(route,soldier.Position,lane,map)','true',1),)),
    (lambda o:routes_and_drop(route,o),(ops.replace('recorded.UnionWith(presentBeforeRaid)','recorded.Clear()',1),)),
]:
    try:check(*args)
    except AssertionError:pass
    else:raise AssertionError('negative mutation incorrectly passed')
print('PASS: optional exit hook isolation, patch-per-class, defensive turret/animal/mech engagement, CE prone guard, safe lanes, cumulative drop capture, six negative mutations')
