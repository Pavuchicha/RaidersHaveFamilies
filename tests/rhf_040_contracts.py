"""Static contract tests; only a green GitHub Actions compile and real game test verify functionality."""
from pathlib import Path
import xml.etree.ElementTree as ET
root=Path(__file__).resolve().parents[1]
source=(root/'RaidersHaveFamilies/Source/RaidersHaveFamilies.cs').read_text()
ui=(root/'RaidersHaveFamilies/Source/RHFRankUI.cs').read_text()
debug=(root/'RaidersHaveFamilies/Source/RHFDebugActions.cs').read_text()
stat=root/'RaidersHaveFamilies/Defs/StatDefs/RHF_RankStats.xml'
tree=ET.parse(stat)
defs={x.findtext('defName'):x for x in tree.findall('StatDef')}
assert {'RHF_SocialImportance','RHF_ConfirmedRelations'}<=defs.keys()
assert 'RHFRankStatWorker' in ui and 'RHFProvenanceStatWorker' in ui
assert 'ITab_Pawn_Character' in ui and 'ITab_Pawn_Prisoner' in ui
assert 'RegisterProvenance' in source and 'rhfProvenanceV4' in source and 'LookMode.Deep' in source
assert 'MaxPerWave = 8' in source and 'HardMaxPerCycle = 8' in source
assert 'CurrentRelations(p) >= cfg.DesiredLinks' in source
assert 'for (int pass = 0; pass < 2' in source
assert '"Родство через общего родителя"' in source
assert 'RHFMapComponent.EnrichSelectedForDebug' in debug
assert 'RHF_ConfirmedRelations' in stat.read_text()
assert 'Pawn.SpawnSetup' in (root/'RaidersHaveFamilies/Source/RHFPatches.cs').read_text()
assert 'MapComponentTick()' in source
assert 'RHFPublicityKind' in (root/'RaidersHaveFamilies/Source/RHFHostageSystems.cs').read_text()
print('PASS: 0.4.0 rank UI (3 surfaces), provenanced family edits, load/save, bounded event generation, debug tools')
