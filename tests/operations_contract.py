"""Structural contracts only; not proof of a successful RimWorld operation."""
from pathlib import Path
import xml.etree.ElementTree as ET
P=Path(__file__).resolve().parents[1]
src=P/'RaidersHaveFamilies/Source'
files={f.name:f.read_text('utf-8') for f in src.glob('*.cs')}
assert {'RHFPatches.cs','RHFOperations.cs','RHFRanks.cs','RHFWCERegistry.cs',
        'RHFHostageSystems.cs','RHFPoliticalReputation.cs'}<=set(files)
assert 'Danger.Some, false, TraverseMode.ByPawn' not in files['RHFPatches.cs']
assert 'RHFDialogue.HasCaptives(faction)' in files['RHFPatches.cs']
for needle in ['RHFStrikeParty','RHFStrikeRole','CombinedStrike','Sponsors','ForceDeployNow',
  'RHFMissionIntegrityV130.ExecuteRaidWithoutVanillaLetter(parms, op)','private bool Deploy(',
  'SplitRescueWing(','Scribe_Collections.Look(ref Parties',
  'Infiltrate(', 'FindBarrier(', 'FreeTarget(', 'Extract(', 'BuildAndLaunch(',
  'PawnLostCondition.LeftVoluntarily','LastProgressTick','map.roofGrid.RoofAt(cell)!=null','Steel',
  'PodReady','Phase','ExitMap(false,Rot4.Invalid)']:
    assert needle in files['RHFOperations.cs'],needle
for needle in ['RHFStrikeRole.Diversion','sponsors.Count>1','hired != null',
               'Mathf.Clamp','RetaliationIssued','RHFPoliticalReputation','StartRetaliation']:
    assert needle in files['RHFHostageSystems.cs'],needle
for t in ['RHF_ExtractionPod','RHF_FieldLauncher']:
    assert t in (P/'RaidersHaveFamilies/Defs/ThingDefs/RHF_ExtractionEquipment.xml').read_text('utf-8')
for xml in (P/'RaidersHaveFamilies').rglob('*.xml'):ET.parse(xml)
print('PASS: rescuer/diversion wings, coalition sponsorship, all rescue phases, metadata.')
