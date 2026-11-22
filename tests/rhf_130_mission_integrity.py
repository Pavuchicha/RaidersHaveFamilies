"""RHF v0.13.1 source/API contracts and deterministic mission bookkeeping.
These are NOT real RimWorld gameplay tests; GitHub Actions must compile the DLL.
"""
from pathlib import Path
import xml.etree.ElementTree as ET
root=Path(__file__).resolve().parents[1]
source=root/'RaidersHaveFamilies/Source'
ops=(source/'RHFOperations.cs').read_text('utf8')
integrity=(source/'RHFMissionIntegrityV130.cs').read_text('utf8')
rescue=(source/'RHFRescueCommandV124.cs').read_text('utf8')
debug=(source/'RHFDebugActions.cs').read_text('utf8')
wf=(root/'.github/workflows/build.yml').read_text('utf8')

# ONE coherent mission letter replaces vanilla RaidEnemy, and no field-kit letter.
assert 'ExecuteRaidWithoutVanillaLetter(parms, op)' in ops
assert 'IncidentDefOf.RaidEnemy.Worker.TryExecute(parms)' in integrity
assert 'RHFMissionIntegrityV130.ShowMissionLetter(op,map)' not in ops
assert 'GetLetterLabel' in integrity and 'GetLetterText' in integrity
assert 'AllowOneNativeLetter' in integrity
assert 'ActiveRaidLetterOperation' in integrity
assert 'AccessTools.GetDeclaredMethods(typeof(LetterStack))' in integrity
assert 'MissionBriefing(op)' in integrity and 'RHFMissionProfiles.Label(op)' in integrity
assert 'Полевой эвакуационный комплекс' not in ops
assert 'Find.LetterStack.ReceiveLetter("Эвакуация важного пленника"' not in ops

# VIP extraction uses actual vanilla buildings, including steel and components.
assert 'GetNamedSilentFail("PodLauncher")' in ops
assert 'GetNamedSilentFail("TransportPod")' in ops
assert 'ThingDefCountClass' in ops and 'FieldCost(' in ops
for value in ('PodComponents', 'PodSteelExtra', 'PodConstructionStage',
              'PodLauncherThing', 'PodVehicleThing', 'PodLauncherCell',
              'FieldFootprintFree(', 'FindFieldConstructionSite(',
              'FieldMaterialsPresent(', 'SpendFieldSteel(', 'TryLaunchPhysicalVipPod('):
    assert value in ops, value
assert 'GenSpawn.Spawn(completed,site,map)' in ops
assert 'UseFieldMaterial(op.PodComponents,requiredComponents)' in ops
assert 'if(op.PodConstructionStage>=2)op.PodReady=true' in ops
assert 'if(op.PodReady)' in ops
assert 'if(!op.PodBudgetPaid' in ops
assert '16500' not in ops, 'old idle VIP build wait still used'
assert 'ThingMaker.MakeThing(pod),op.PodCell,map' not in ops, 'old fake shell still spawned'
assert 'GetNamedSilentFail("RHF_ExtractionPod")' not in ops
assert 'GetNamedSilentFail("RHF_FieldLauncher")' not in ops
assert 'Scribe_Values.Look(ref PodConstructionStage' in ops
assert 'Scribe_References.Look(ref PodComponents' in ops
assert 'Scribe_References.Look(ref PodSteelExtra' in ops
# Existing physical skyfaller still transports the REAL prisoner (no teleport).
assert 'vip.DeSpawn();' in ops and 'innerContainer.TryAdd(vip)' in ops
assert 'GenSpawn.Spawn(skyfaller,op.PodCell,map)' in ops

# Drop center never targets occupied prison cells / hostage position.
assert 'radius=14;radius<=22' in ops
assert 'cell.GetRoom(map)==prisonRoom' in ops
assert 'nearHostage=true' in ops
assert 'map.roofGrid.RoofAt(cell)!=null' in ops
assert 'DropCellFinder.IsGoodDropSpot(cell,map,true,true)' in ops

# No global diplomacy rewrites. Thing hostility is overridden only for named
# members of the same live rescue; player combat against them is still hostile.
assert 'new Type[] {typeof(Thing),typeof(Thing)}' in integrity
assert 'new Type[] {typeof(Thing),typeof(Faction)}' in integrity
assert 'if(__result && RHFMissionIntegrityV130.SameSide' in integrity
assert 'attackingFaction.IsPlayer' in integrity
assert 'op.Squad.Contains(pawn)' in integrity and 'op.Targets.Contains(pawn)' in integrity
assert 'Soldier(op,pa) && ProtectedPerson(op,pb)' in integrity
assert 'RHFMissionIntegrityV130.MayEscort(op,p,captive)' in rescue
assert 'RHFMissionIntegrityV130.MayEscort(op,rescuer,captive)' in ops
assert 'LordMaker.MakeNewLord(captive.Faction' in rescue
assert 'lord.LordJob is RHFRescueLordJobV124' in ops

# Group commander protects the field-building phase and scripted exit errors
# are logged once (not endlessly every strategic pulse).
assert 'op.Phase == 3 ?' in rescue
assert 'op.PodCell.IsValid ? op.PodCell' in rescue
assert 'Log.ErrorOnce(' in rescue
assert '32 — Свой-чужой, VIP и этапы операции' in integrity
assert 'RHFDoctrine.InfiltrationAndExit' in debug
assert 'scenario == RHFDebugScenario.MassRescue' in debug
assert 'total=Mathf.Max(total,1200f)' in debug

# Supply accounting model: launchers and pods consume distinct component and
# steel stacks; a single 130-steel stack would violate vanilla stack limits.
steel_for_launcher, steel_for_pod = 50,60
component_for_launcher, component_for_pod=1,1
starting_steel=steel_for_launcher+steel_for_pod+20
stacks=[min(starting_steel,75),max(0,starting_steel-75)]
assert stacks==[75,55] and sum(stacks)==130
assert sum(stacks)-20-steel_for_launcher-steel_for_pod==0
assert component_for_launcher+component_for_pod==2
assert 'Math.Min(steel,ThingDefOf.Steel.stackLimit)' in ops
assert 'if(extra!=null)GenSpawn.Spawn(extra,extraCell,map)' in ops
assert 'int stageWork=stage==0?3000:1600' in ops

# Release archives must offer the installable DLL ZIP after CI compilation.
assert 'python3 tests/rhf_130_mission_integrity.py' in wf
assert 'Build Raiders Have Families 0.13.3' in wf
assert 'RaidersHaveFamilies_v0.13.3_RimWorld_1.7_INSTALL.zip' in wf
assert 'RaidersHaveFamilies-RimWorld-1.7-v0.13.3-S2.2.1-ENGINEER-DOOR-CONTACT-TEST' in wf
for f in (root/'RaidersHaveFamilies').rglob('*.xml'):
    ET.parse(f)
print('PASS: mission letters, vanilla VIP buildings, paid supplies, safe drops, mission friendlies, group exit safeguards, CI metadata')
