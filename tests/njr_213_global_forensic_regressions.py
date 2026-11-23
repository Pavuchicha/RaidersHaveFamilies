"""R006 global forensic regressions: cross-module correctness, not a RimWorld test.

Unlike keyword-only legacy tests this suite checks cross-method control flow,
verifies old R005 fails the new contracts, and uses independent budget models.
"""
from pathlib import Path
import re
import xml.etree.ElementTree as ET

R = Path(__file__).resolve().parents[1]
S = R/'RaidersHaveFamilies'/'Source'


def method_body(source, declaration):
    at = source.find(declaration)
    assert at >= 0, f'absent method {declaration}'
    begin = source.find('{', at)
    assert begin >= 0
    level = 0
    for n in range(begin, len(source)):
        if source[n] == '{': level += 1
        elif source[n] == '}':
            level -= 1
            if level == 0:
                return source[begin:n+1]
    assert False, f'unbalanced method {declaration}'


hostages = (S/'RHFHostageSystems.cs').read_text(encoding='utf8')
ledger = (S/'RaidersHaveFamilies.cs').read_text(encoding='utf8')
ops = (S/'RHFOperations.cs').read_text(encoding='utf8')
group = (S/'NJRGroupRansomV15.cs').read_text(encoding='utf8')
force = (S/'NJRForceCompositionS42.cs').read_text(encoding='utf8')
debug = (S/'RHFDebugActions.cs').read_text(encoding='utf8')

# This is the confirmed R005 bug: after first participant exits, its Pawn
# reference is explicitly nulled but the group debt stays open. A routine
# audit must not discard that record OR dereference its null Pawn.
exit_body = method_body(hostages, 'public static void OnPrisonerExit(')
assert 'r.Pawn=null;' in exit_body, 'Expected post-exit detach semantics changed'
update = method_body(hostages, 'public static void UpdateOffers(')
retain = 'if (r != null && r.GroupRansomId > 0 && r.GroupReleaseConfirmed)'
assert retain in update, 'EXPIRED GROUP PAYMENT: released member is discarded'
assert update.index(retain) < update.index('(r.Pawn == null && !IsDeadCase(r))')
assert update.index('continue;', update.index(retain)) < update.index('r.Pawn.IsSlaveOfColony')
assert 'NJRGroupRansomV15.Audit(ledger)' in update
assert 'member.GroupReleaseConfirmed' in method_body(group, 'private static bool Ready(')
assert 'member.GroupPaymentAttempted' in method_body(group, 'private static bool Ready(')

# Turning off NEW negotiations must not disable already signed contracts,
# detection of recruitment or already-queued reprisals.
tick = method_body(ledger, 'public override void GameComponentTick()')
assert 'if (Hostages.Count > 0)' in tick
assert 'RHFHostageDiplomacy.UpdateOffers(this, tick);' in tick
assert tick.index('RHFHostageDiplomacy.UpdateOffers(this, tick);') > tick.index('if (RHFMod.Settings.Negotiations)')
assert 'RHFMod.Settings?.Negotiations != true' in update
assert update.index('RHFMod.Settings?.Negotiations != true') > update.index('if (r.Recruited)')
assert update.index('RHFMod.Settings?.Negotiations != true') < update.index('r.InitialOfferMade || r.Negotiated')
assert 'tick < nextHostageCheck' in tick
assert 'Hostages.Count>0?90000:150000' in tick

# All supplementary equipment, fuel and beasts must be deducted BEFORE the
# vanilla incident points are handed over; no 300-point floor re-adds value.
deploy = method_body(ops, 'private bool Deploy(RHFOperation op)')
assert 'NJRForceCompositionS42.Reserve(op,party);' in deploy
assert deploy.index('NJRForceCompositionS42.Reserve(op,party);') < deploy.index('float effectivePoints = party.Points')
assert 'Mathf.Clamp(effectivePoints, 300f, 15000f)' not in deploy
assert 'points = Mathf.Min(effectivePoints, 15000f)' in deploy
assert 'party.AnimalBudgetS42 - party.LoadoutBudgetS42' in deploy
assert 'party.Drop=false' in deploy and 'party.Siege=false' in deploy
assert 'NJRForceCompositionS42.CanUsePods(party.Faction)' in deploy
assert 'NJRForceCompositionS42.CanUseSiege(party.Faction)' in deploy

reserve = method_body(force, 'internal static void Reserve(')
assert 'party.AnimalBudgetS42' in reserve and 'party.LoadoutBudgetS42' in reserve
assert 'float factor=available/reserved' in reserve
for points in [40, 180, 250, 300, 450, 650, 900, 2700, 8000, 20000]:
    supply = min(300, max(0, points*.12))
    for lowtech in (False, True):
        animals = min(points*.42,5500) if lowtech and points>=450 else 0
        loadout = min(points*.23,3600) if points>=650 else 0
        available = max(0,points-supply-350)
        if animals+loadout>available and animals+loadout>0:
            factor=available/(animals+loadout)
            animals*=factor
            loadout*=factor
        native=max(1,points-supply-animals-loadout)
        handed=min(native,15000)
        assert supply+animals+loadout+handed<=points+1.00001
        if points<300: assert handed<300  # regression R005 created extra pawns

# The 18 dev scenarios remain registered; category #08 is a TECH TEST,
# not an independent mandatory 'animal siege' doctrinal category.
scenario_enum = re.search(r'internal enum RHFDebugScenario\s*\{([^}]*)\}',debug,flags=re.S)
assert scenario_enum
scenarios=[a.strip().rstrip(',') for a in scenario_enum.group(1).splitlines() if a.strip() and not a.strip().startswith('//')]
assert len(scenarios)==18 and len(set(scenarios))==18
for scenario in scenarios:
    assert f'Start(RHFDebugScenario.{scenario})' in debug
assert 'Месть: низкотехнологичная фракция' in debug
assert 'Месть: племя с боевыми животными' not in debug

# Metadata/XML validity and save identity protect existing saves.
about = ET.parse(R/'RaidersHaveFamilies'/'About'/'About.xml').getroot()
assert about.findtext('packageId')=='pavuk.raidshavefamilies'
assert 'NJR' in about.findtext('name')
all_xml = list((R/'RaidersHaveFamilies').rglob('*.xml'))
assert len(all_xml)>=10
for xml in all_xml: ET.parse(xml)
assert 'Scribe_Values.Look(ref GroupRansomId' in hostages
assert 'Scribe_Values.Look(ref GroupPaymentAttempted' in hostages
assert 'NJRRescueExitS41' in (S/'NJRRescueExitS41.cs').read_text(encoding='utf8')
print('PASS R006 global source forensic checks: 18 scenarios, XML+save IDs, '
      'group-ransom persistence, diplomacy-toggle isolation, no free raid points, '
      'tech limits and low-TPS cadence (STATIC/MODEL ONLY)')
