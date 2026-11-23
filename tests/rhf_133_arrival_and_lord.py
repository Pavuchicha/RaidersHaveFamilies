"""RHF v0.13.3: guard incident attribution and notifications against old regressions.
Source contracts, NOT an in-game behavioral simulation.
"""
from pathlib import Path
import xml.etree.ElementTree as ET
R=Path(__file__).resolve().parents[1]
S=R/'RaidersHaveFamilies'/'Source'
ops=(S/'RHFOperations.cs').read_text('utf8')
mission=(S/'RHFMissionIntegrityV130.cs').read_text('utf8')
old=(S/'RHFRescueControlV123.cs').read_text('utf8')
new=(S/'RHFRescueCommandV124.cs').read_text('utf8')
reprisal=(S/'RHFReprisals.cs').read_text('utf8')
wf=(R/'.github/workflows/build.yml').read_text('utf8')
# First / any subsequent faction waves in one operation all route through the
# same ambient RHF letter; no synthetic letter after vanilla raid generation.
assert ops.count('ExecuteRaidWithoutVanillaLetter(parms, op)')+ops.count('ExecuteRaidWithoutVanillaLetter(parms,op)')==3
assert 'Native retry with safe rescue' in ops
assert 'refusing a random cave/remote map spawn' in ops
assert 'RHFMissionIntegrityV130.ShowMissionLetter(op,map)' not in ops
assert 'AllowOneNativeLetter(out bool entered)' in mission
assert 'if (state.Sent) return false' in mission
assert 'if (receiveLetterNesting > 0)' in mission
assert 'RHFNativeRaidTitleV133' in mission
assert 'RHFNativeRaidBriefingV133' in mission
assert '__result = RHFMissionIntegrityV130.MissionBriefing(op)' in mission
assert 'ActiveRaidLetterOperation' in mission
assert 'if (method.Name == "ReceiveLetter") yield return method;' in mission
# Search / revenge schedule no longer sends a threat letter 1200 ticks early.
assert 'Reprisal scheduled:' in reprisal
assert 'Find.LetterStack.ReceiveLetter(search ?' not in reprisal
# A rescued squad is not hijacked by old chains. Controller dispatches directly.
assert 'RHFRescueCommandV124.Infiltrate(this, op, now);' in ops
assert 'RHFRescueCommandV124.OnDeploy(this, op, true);' in ops
assert '[HarmonyPatch(typeof(RHFOperationController), nameof(RHFOperationController.MapComponentTick))]' not in old
assert '[HarmonyPatch(typeof(RHFOperationController), "Extract")]' not in new
assert '[HarmonyPatch(typeof(RHFRescueControlV123), "OnDeployed")]' not in new
assert '[HarmonyPatch(typeof(RHFRescueControlV123), "Infiltrate")]' not in new
# No vanilla trash infrastructure think node remains in rescue Lord hold.
assert 'new PawnDuty(missionHold, pawn.Position)' in new
assert 'RHF_HoldForMissionOrders' in new
xml=ET.parse(R/'RaidersHaveFamilies/Defs/DutyDefs/RHF_MissionHold.xml')
ns=[x.text for x in xml.iter('defName')]
assert 'RHF_HoldForMissionOrders' in ns
body=(R/'RaidersHaveFamilies/Defs/DutyDefs/RHF_MissionHold.xml').read_text('utf8')
for node in ('AITrashColony','AITrashBuildingsDistant','AISapper','AssaultColony'):
    assert node not in body
# If the game's modded SpawnSetup observer misses a pawn, recover only pawns
# that appeared DURING the actual incident, matching the exact operation party.
assert 'RecoverFreshRescueMembers(op, party, presentBeforeRaid);' in ops
assert 'existingPawns.Contains(pawn.thingIDNumber)' in ops
assert 'pawn.Faction != party.Faction' in ops
assert 'op.Diversion.Contains(pawn)' in ops
# Test harness really runs this newer contract and includes new XML in build.
assert 'python3 tests/rhf_133_arrival_and_lord.py' in wf
assert 'v0.15.0-beta1' in wf
print('PASS: one RHF native arrival letter, no scheduled duplicate, direct mission Lord, bounded newly-spawned attribution')
