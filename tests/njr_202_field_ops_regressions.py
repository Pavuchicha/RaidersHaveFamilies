"""NJR 0.16.0-beta1: integrated field-test regression gates. STATIC ONLY.
These guards are NOT substitutes for compilation or actual RimWorld raid tests.
"""
from pathlib import Path
import re,xml.etree.ElementTree as ET
R=Path(__file__).resolve().parents[1]; S=R/'RaidersHaveFamilies/Source'
ops=(S/'RHFOperations.cs').read_text(); entry=(S/'RHFRescueGroundEntryS41.cs').read_text()
rescue=(S/'RHFRescuePlannerS41.cs').read_text(); role=(S/'RHFRescueRoleJobGiverS41.cs').read_text()
air=(S/'RHFVipEvacS311.cs').read_text(); pods=(S/'RHFPodPlannerS41.cs').read_text()
threat=(S/'NJRThreatBudgetS41.cs').read_text(); clears=(S/'NJRPodSiteClearS41.cs').read_text()
interior=(S/'NJRRescueInteriorS41.cs').read_text(); roof=(S/'JobDriver_NJRPodRoofS41.cs').read_text()
politics=(S/'RHFPoliticalReputation.cs').read_text(); hostages=(S/'RHFHostageSystems.cs').read_text()
reprisals=(S/'RHFReprisals.cs').read_text(); debug=(S/'RHFDebugActions.cs').read_text()
contracts={
 'cluster gate and safe-only fallback':[(entry,'GroundEntryClusterSafe(map,center)'),(entry,'roof.isThickRoof'),(ops,'refusing a random cave/remote map spawn')],
 'turret encirclement bounding':[(rescue,'TurretStagingCell(soldier,board.Threat,map,allocated)'),(rescue,'return best??nearest;'),(role,'case RHFRoleS41.SuppressTurret:')],
 'physical VIP boarding':[(air,'private void NativeVipMovementS41('),(air,'vip.jobs.TryTakeOrderedJob(board);'),(air,'transport.innerContainer.Contains(vip)')],
 'tiered indoor rescue landing':[(ops,'private IntVec3 RescueDropCenter('),(ops,'tier*100000+risk*20000'),(interior,'DirectDoor(Map map,Room prison,Room other')],
 'indoor build and physical safe clear':[(ops,'private bool FindFieldConstructionSite('),(ops,'NJRPodSiteClearS41.Needed('),(clears,'internal static Thing Blocker('),(clears,'map.roofGrid.RoofAt(b.Position)==null')],
 'native construction and thin roof work':[(air,'GenConstruct.PlaceBlueprintForBuild('),(pods,'NJRPodRoofS41.NeedsWork('),(pods,'for(int i=0;i<op.Squad.Count;i++)'),(pods,'for(int radius=1;radius<=12;radius++)'),(roof,'GotoCell(TargetIndex.A,PathEndMode.Touch)'),(roof,'pawn.Map.roofGrid.SetRoof(cell,null);')],
 'each faction independent vanilla budget':[(threat,'StorytellerUtility.DefaultThreatPointsNow(map)'),(threat,'float points=Mathf.Max(1f,vanilla*modifier*'),(threat,'Mathf.Clamp(RHFPoliticalReputation.Pressure(sponsor),-100,0)'),(hostages,'NJRThreatBudgetS41.PartyPoints(map,member'),(reprisals,'NJRThreatBudgetS41.PartyPoints(map,member'),(debug,'NJRThreatBudgetS41.PartyPoints(map,sponsors[i]')],
 'historical save identities':[(src:=(S/'RaidersHaveFamilies.cs').read_text(),'namespace RaidersHaveFamilies'),(role,'RHFRescueLordJobV124')],
}
count=0
for group,requirements in contracts.items():
 for text,anchor in requirements:
  assert anchor in text,(group,anchor)
  assert anchor not in text.replace(anchor,'[REMOVED]'),(group,'mutant not detected',anchor)
  count+=1
# Do not count a combined coalition budget as independent generation.
assert '300f,15000f' not in threat
assert 'operation.ThreatPoints+=party.Points' in reprisals
assert 'op.ThreatPoints+=party.Points' in hostages
assert '!op.PodSuppliesIssued' in air
assert 'NativeLaunchReadyS41(' in air
assert 'SetFactionDirect(Faction.OfPlayer)' not in air
assert 'NJR: ' in (R/'RaidersHaveFamilies/Defs/StatDefs/RHF_RankStats.xml').read_text() or 'NJR — Not Just Raiders' in (R/'RaidersHaveFamilies/Defs/StatDefs/RHF_RankStats.xml').read_text()
assert ET.parse(R/'RaidersHaveFamilies/About/About.xml').getroot().findtext('packageId')=='pavuk.raidshavefamilies'
print('PASS NJR v0.16.0-beta1:',len(contracts),'integrated subsystems /',count,'static anchors and negative mutations; NOT game runtime')
