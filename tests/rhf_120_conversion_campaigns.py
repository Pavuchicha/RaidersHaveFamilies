"""v0.12 source-contract checks: useful regressions, NOT proof of C# compile/gameplay."""
from pathlib import Path
import re
import xml.etree.ElementTree as ET
r=Path(__file__).resolve().parents[1]
s=r/'RaidersHaveFamilies/Source'
load=lambda f:(s/f).read_text('utf8')
events=load('RHFStatusEvents.cs')
host=load('RHFHostageSystems.cs')
ops=load('RHFOperations.cs')
reps=load('RHFReprisals.cs')
profiles=load('RHFMissionProfiles.cs')
dia=load('RHFDialogue.cs')
dbg=load('RHFDebugActions.cs')
for part in ['RecruitUtility.Recruit','Pawn.SetFaction', 'Pawn_IdeoTracker.SetIdeo',
             'RecordRecruitment(', 'RecordConversion(']:
    assert part in events, f'missing actual pawn event {part}'
assert 'recruiter!=null' in events, 'Only actual player recruitment should trigger traitor hunt'
for field in ['Recruited','RecruitmentTick','RecruitmentReported','RecruitmentRevengeQueued',
              'OriginalIdeo','ConvertedIdeo','IdeologyConverted','IdeologyReported','IdeologyChangeTick']:
    assert re.search(r'Scribe_(?:Values|References)\.Look\(ref '+field+r',',host),f'unsaved {field}'
for m in ['RecordRecruitment','RecordConversion','ReportConversion','ReportRecruitment']:
    assert ' '+m+'(' in host
assert 'pawn.IsSlaveOfColony && !r.Recruited' in host
assert 'p.Ideo==r.ConvertedIdeo' in host and 'p.Ideo==r.OriginalIdeo' in host
assert 'r.IdeologyReported=true' in host
assert 'r.RecruitmentRevengeQueued=true' in host
assert 'r==null || !r.IdeologyConverted || r.IdeologyReported' in host
assert 'RHFHostageDiplomacy.ReportConversion(r)' in dia
assert 'RHFHostageDiplomacy.ReportRecruitment(r)' in dia
assert 'r.IdeologyConverted' in dia and 'r.Recruited' in dia
# Slave's location controls the near-field landing, not a generic prison or colony center.
assert 'prisoner.IsSlaveOfColony' in ops and 'radius=14;radius<=22' in ops
assert 'DropCellFinder.IsGoodDropSpot' in ops and 'parms.spawnCenter = nearPrison' in ops
assert 'IsSlaveOfColony' in host and 'IsSlaveOfColony' in ops
assert 'r.Pawn.IsSlaveOfColony' in host
# True staged rescue operation versus a claim in a letter.
for term in ['TargetedTraitorHunt','SiegeAndRaze','FortifiedVipExtraction',
             'InfiltrationAndExit','RescueThenAssault','RescueAndReprisal',
             'PursuePriorityTarget(', 'AssaultStrategicStructures(',
             'TryBuildPrisonBarricade(', 'PostExtractionAssault(']:
    assert term in ops or term in profiles, term
assert 'RHFMissionProfiles.ConfigureCase' in reps
assert 'RHFMissionProfiles.Configure' in host
assert 'Scribe_Values.Look(ref Doctrine' in ops
assert 'Scribe_References.Look(ref HuntTarget' in ops
assert 'Scribe_Values.Look(ref BarricadesBuilt' in ops
assert 'Scribe_Values.Look(ref PodBudgetPaid' in ops
assert 'party.Points - party.SupplyBudget' in ops
assert 'op.PodSupplyCost=cost' in ops
assert 'if(!op.PodBudgetPaid' in ops
assert 'FieldMaterialsPresent(op,requiredSteel,requiredComponents)' in ops and 'ThingDefOf.Steel' in ops
# Actual physical VIP container + departing skyfaller; never unexplained disappearance.
for name in ['TryLaunchPhysicalVipPod(', 'ActiveTransporter', 'ActiveTransporterInfo',
             'FlyShipLeaving', 'SkyfallerMaker.MakeSkyfaller', 'ThingDefOf.DropPodLeaving',
             'innerContainer.TryAdd(vip)',
             'GenSpawn.Spawn(skyfaller']:
    assert name in ops, f'no real pod launch: {name}'
assert re.search(r'skyfaller\.createWorldObject\s*=\s*false',ops), 'pod should leave without creating player faction world objects'
assert 'vip.ExitMap(' not in ops, 'A direct scripted VIP disappear is NOT physical transport'
assert 'vip.DeSpawn();' in ops and 'finally' in ops, 'cargo must avoid duplicate spawned VIP and roll back safely'
assert 'PodLaunchCompleted=true' in ops
# Public Ideology mood effects are not paid at conversion, only at radio disclosure.
for stage in (1,2,3):
    x=ET.parse(r/'RaidersHaveFamilies/Defs/ThoughtDefs/RHF_Thoughts.xml')
    defs={y.findtext('defName'):y for y in x.getroot().findall('ThoughtDef')}
    for prefix in ['RHF_ConversionVictory','RHF_ConversionLoss']:
        name=f'{prefix}{stage}'
        assert name in defs
        assert defs[name].findtext('durationDays')=='5'
assert '26 — Аудит вербовки' in dbg and '27 — Аудит боевой доктрины' in dbg
assert '0.13.0' in (r/'.github/workflows/build.yml').read_text()
print('PASS: actual conversion/recruitment hooks, mood reporting, slave drop, real pod cargo, phased mission orders and debug diagnostics')
# A durable trace of all gathered requirements and their distinct tactical cases
# must survive the next archive transfer, not just appear in prose once.
audit = (r/'REQUIREMENTS_AUDIT_MASTER_RU.md').read_text('utf8')
design = (r/'TACTICAL_DOCTRINE_DESIGN_RU.md').read_text('utf8')
reqs = re.findall(r'\| ([ABCDEF]\d\d) \|', audit)
assert len(reqs)==82 and len(set(reqs))==82, f'missing requirements from audit: {len(reqs)}'
for target in ['Спасение рядового', 'VIP-капсула + карательный удар',
               'Охота на завербованного VIP', 'Спасение рабов', 'Месть за каннибализм',
               'Коалиция собственников', 'Спасение + отвлечение',
               'Спасение из враждебного поселения игроком',
               'Отдельная цель устранения предателя']:
    assert target in design, f'missing documented tactic {target}'
print('PASS: full 82-item requirements audit and tactical-doctrine design preserved')
# A hostile VIP hunt must also attack the settlement, using a separate cool-
# down and (when possible) a separate combat wing from its named-target hunt.
assert 'op.AssaultAfterObjective=true' in profiles
assert 'op.HuntTarget != null && op.AssaultAfterObjective' in ops
assert 'LastInfrastructureOrderTick' in ops and 'rhfLastInfrastructureOrderV12' in ops
assert 'op.Diversion.Count==0 &&' in ops and 'attackers.Count/2' in ops
print('PASS: dual-objective VIP retribution includes active settlement destruction')
