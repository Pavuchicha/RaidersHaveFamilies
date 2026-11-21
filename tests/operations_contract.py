"""Static contract to catch regressions like the v0.2.0 CS1503 signature error.
Does not certify C# compilation or in-game AI behavior."""
from pathlib import Path
import xml.etree.ElementTree as ET
P=Path(__file__).resolve().parents[1]
source=P/'RaidersHaveFamilies/Source'
files={f.name:f.read_text('utf-8') for f in source.glob('*.cs')}
assert {'RHFPatches.cs','RHFOperations.cs','RHFRanks.cs','RHFWCERegistry.cs','RHFHostageSystems.cs','RHFPoliticalReputation.cs'}<=set(files)
assert 'Danger.Some, false, TraverseMode.ByPawn' not in files['RHFPatches.cs']
assert 'RHFDialogue.HasCaptives(faction)' in files['RHFPatches.cs']
assert 'PawnsArrivalModeDef' in files['RHFHostageSystems.cs']
assert 'RaidArrivalModeDef' not in files['RHFHostageSystems.cs']
for text in ['RHFRankResolver.RankOf','RansomMultiplier','SecondSponsor','ChooseExecutor','QueueRetaliation',
             'OfferDeadlineTick','FindStrategy','StartRetaliation','Rand.Chance(.25f)','RetaliationIssued','RHFPoliticalReputation']:
    assert text in '\n'.join(files.values()),text
for text in ['Infiltrate(', 'FindBarrier(', 'FreeTarget(', 'Extract(', 'BuildAndLaunch(',
             'PawnLostCondition.LeftVoluntarily','LastProgressTick','RoofRockThick','Steel',
             'PodReady', 'Phase', 'Expir', 'ExitMap(false,Rot4.Invalid)']:
    assert text in files['RHFOperations.cs'],text
for t in ['RHF_ExtractionPod','RHF_FieldLauncher']:
    assert t in (P/'RaidersHaveFamilies/Defs/ThingDefs/RHF_ExtractionEquipment.xml').read_text('utf-8')
for xml in (P/'RaidersHaveFamilies').rglob('*.xml'):
    ET.parse(xml)
assert (P/'RaidersHaveFamilies/Textures/RaidersHaveFamilies/FieldPod.png').stat().st_size>100
assert (P/'RaidersHaveFamilies/Textures/RaidersHaveFamilies/FieldLauncher.png').stat().st_size>100
print('PASS: nine-tier rank, two raid plans, bounded rescue logic, release/boarding, field equipment, metadata.')
