"""RHF v0.9 static contracts. NOT a C# compilation or real-game TPS proof."""
from pathlib import Path
import xml.etree.ElementTree as ET
root=Path(__file__).resolve().parents[1]
source=root/'RaidersHaveFamilies/Source'
tactics=(source/'RHFTactical.cs').read_text('utf8')
ops=(source/'RHFOperations.cs').read_text('utf8')
carry=(source/'RHFCarryDowned.cs').read_text('utf8')
patch=(source/'RHFPatches.cs').read_text('utf8')
debug=(source/'RHFDebugActions.cs').read_text('utf8')
settings=(source/'RaidersHaveFamilies.cs').read_text('utf8')
assert 'RHFTacticalMember : IExposable' in tactics
assert 'RoleName(int role)' in tactics
for name in ['Breacher','Escort','Cover','Medic','Flanker','Assault']:
    assert name in tactics
assert 'TacticalMembers' in ops and 'tacticalMembersV9' in ops
assert 'MeleeWeapon_BreachAxe' in tactics and 'IssueBreachAxe' in tactics
assert 'RHF_BreachKit' not in tactics and 'TryBreachCharge(' not in ops
assert 'ConsumeBreachKit' not in tactics and 'GenExplosion.DoExplosion' not in tactics
assert 'party.Points - party.SupplyBudget' in ops
assert 'party.SupplyBudget = RHFMod.Settings?.RoleEquipment == true' in ops
assert 'Mathf.Min(300f, Mathf.Max(0f, party.Points * 0.12f))' in ops
assert 'TryFactionMeleeFallback' in tactics and 'IsMeleeWeapon' in tactics
assert 'kind.weaponTags' in tactics and 'def.weaponTags' in tactics
assert 'pawn.equipment.Primary == null' not in tactics or 'pawn.equipment?.Primary == null' in tactics
assert 'pawn.equipment.AddEquipment(melee)' in tactics
assert 'MaxCandidatesPerThink = 45' in tactics and 'DecisionInterval = 90' in ops
assert 'MaxOrdersPerDecision = 14' in ops and 'MaxOperationDuration = 180000' in ops
assert 'PressureAssault' in tactics and 'TryCover' in tactics
assert 'TryFlank' in tactics and 'LengthHorizontalSquared' in tactics
assert 'RHFTactical.Register' in ops and 'TacticalDebugStatus' in ops
assert '20 — Аудит боевых ролей' in debug and '21 — Роль и вооружение' in debug
assert 'RHFTactical.RoleName' in patch and 'GetInspectString' in patch
assert 'RHF_CarryDowned' in ops and 'RHF_CarryDowned' in (root/'RaidersHaveFamilies/Defs/JobDefs/RHF_EvacDowned.xml').read_text()
assert 'Toils_Haul.StartCarryThing' in carry and 'TryDropCarriedThing' in carry
assert 'TacticalCombat' in settings and 'RoleEquipment' in settings
# Intentionally do not patch generic combat jobs or every pawn's tick.
assert '[HarmonyPatch(typeof(TickManager)' not in patch
assert '[HarmonyPatch(typeof(PawnGenerator)' not in patch
assert 'public override void MapComponentTick' not in tactics
for name in ['RHF_BreachKit.xml', 'RHF_EvacDowned.xml']:
    path=(root/'RaidersHaveFamilies/Defs/ThingDefs/RHF_BreachKit.xml') if name.startswith('RHF_Breach') else (root/'RaidersHaveFamilies/Defs/JobDefs/RHF_EvacDowned.xml')
    ET.parse(path)
assert not list(ET.parse(root/'RaidersHaveFamilies/Defs/ThingDefs/RHF_BreachKit.xml').getroot())
print('PASS: RHF v0.9 role records, bounded tactical controller, physical equipment, downed evacuation and CI packaging.')
