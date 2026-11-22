"""v0.11 release-specific source contracts. No C# compile or in-game TPS claims."""
from pathlib import Path
import xml.etree.ElementTree as ET
root=Path(__file__).resolve().parents[1]
source=root/'RaidersHaveFamilies/Source'
base=(source/'RaidersHaveFamilies.cs').read_text('utf8')
ops=(source/'RHFOperations.cs').read_text('utf8')
ui=(source/'RHFRankUI.cs').read_text('utf8')
rank=(source/'RHFRanks.cs').read_text('utf8')
medical=(source/'RHFHostageSystems.cs').read_text('utf8')
patch=(source/'RHFPatches.cs').read_text('utf8')
dia=(source/'RHFDialogue.cs').read_text('utf8')
ideology=(source/'RHFPreceptNotes.cs').read_text('utf8')
catalog=(source/'RHFOfficeCatalog.cs').read_text('utf8')
debug=(source/'RHFDebugActions.cs').read_text('utf8')
# Family links must not be silently forgotten between ordinary raids.
for marker in ('oneTimeBackfill','visited < MaxBatchSize',
               'Remember(processedPawn.thingIDNumber)', 'CleanupGeneratedKin',
               'CurrentRelations(pawn) > 0', 'FamilyAudit',
               'MaxPerWave = 48', 'HardMaxPerSave = 1024'):
    assert marker in base, marker
assert base.index('int linked = Enrich(batch)') < base.index('Remember(processedPawn.thingIDNumber)')
assert 'EnsureMinimumFamily(newcomers, ledger, cfg' in base
assert 'DesiredLinks = Math.Max(3, Math.Min(DesiredLinks, 7))' in base
assert 'bool viaFamily=r.Importance<5 && family!=null' in dia
assert 'Rand.Range(0, texts.Length)' in dia
assert 'kin.relations.RelatedPawns' in base
# Only one-time loading exception to no full-map-scanning policy.
a=base[base.index('public override void MapComponentTick()',base.index('class RHFMapComponent')):
       base.index('private void Remember(int id)',base.index('class RHFMapComponent'))]
assert a.count('AllPawnsSpawned')==1 and a.count('oneTimeBackfill = false')==1
# Raid never counts unescorted departure as an escorted extraction.
for marker in ('class RHFEscortLink', 'Escorts = new List<RHFEscortLink>',
               'Scribe_Collections.Look(ref Escorts', 'EnsureEscort(op,captive)',
               'separation > 144', 'EscapedWithEscort++',
               'if (allFree)', 'cell.GetRoom(map)==prisonRoom',
               'DropCellFinder.IsGoodDropSpot(cell,map,true,true)'):
    assert marker in ops, marker
assert ops.index('captive.ExitMap(false,Rot4.Invalid)') < ops.index('if (escort.Spawned) escort.ExitMap(false,Rot4.Invalid)')
assert 'if (!party.Drop && op.Type != (int)RHFOperationType.Rescue) parms.raidArrivalMode = null' in ops
assert 'member?.Role == (int)RHFCombatRole.Escort ? -36 : 0' in ops
# Personal panel not drawn, nor all stat rows exposed, for player faction.
assert 'pawn.Faction.IsPlayer' in ui
assert 'RHFCharacterTabSize.SetExpanded(__instance, foreign)' in ui
assert 'p.Faction != null && !p.Faction.IsPlayer' in ui
# Missing-part medical path is not defined merely by an unclear dialogue confession.
assert 'RecordMedicalMissingPart' in medical and 'LastResponsibleActor' in medical
assert 'RHFMedicalMissingPartEvent' in patch and 'Hediff_MissingPart' in patch
assert 'nameof(Pawn_HealthTracker.AddHediff)' in patch
assert 'dinfo.HasValue' in patch
# Actually existing Royalty titles take precedence over narrative rank assignments.
assert 'pawn.royalty.GetCurrentTitle(pawn.Faction)' in rank
assert 'RHFOfficeCatalog.Office' in rank
assert catalog.count('new[] {') >= 18
# No default disabled cannibalism button prior to real proof, no mixing fiction/debug.
assert 'if(RHFHostageDiplomacy.CanConfessCannibalism(r))' in dia
assert 'RHFHostageDiplomacy.HasNewEvidence(r)' in dia and 'report.Disable' in dia
assert '\\n\\n[Сведения RHF]\\n' in dia
assert 'if (Prefs.DevMode)' in dia
assert 'r.NegotiationEscalation' in medical and 'r.HighDemandRejected' in medical
assert 'Извин' not in dia  # no automatic denial of responsibility
# Ideology native entries include public execution and negative abhorrent cannibalism.
assert 'name.StartsWith("Execution_"' in ideology
assert 'RHF_PublicCannibalismDisgust' in ideology
thoughts=ET.parse(root/'RaidersHaveFamilies/Defs/ThoughtDefs/RHF_Thoughts.xml').getroot()
node=next(n for n in thoughts.findall('ThoughtDef') if n.findtext('defName')=='RHF_PublicCannibalismDisgust')
assert float(node.findtext('./stages/li/baseMoodEffect'))<0
# Diagnostics available in Russian, 2 new tests.
assert '24 — Аудит автоматических семей' in debug
assert '25 — Аудит эвакуации' in debug
assert '0.13.3' in (root/'.github/workflows/build.yml').read_text()
print('PASS: v0.11 family persistence/load backfill, escort extraction, medical, rank, ideology and dialogue contracts.')
