"""RHF 0.8.0 new feature regression guard. Not a compile or in-game test."""
from pathlib import Path
import xml.etree.ElementTree as ET
r=Path(__file__).resolve().parents[1]
p=r/'RaidersHaveFamilies/Source'
h=(p/'RHFHostageSystems.cs').read_text('utf8')
m=(p/'RHFMercy.cs').read_text('utf8')
patch=(p/'RHFPatches.cs').read_text('utf8')
ops=(p/'RHFOperations.cs').read_text('utf8')
notes=(p/'RHFPreceptNotes.cs').read_text('utf8')
dbg=(p/'RHFDebugActions.cs').read_text('utf8')
xml=ET.parse(r/'RaidersHaveFamilies/Defs/ThoughtDefs/RHF_Thoughts.xml').getroot()
thoughts={x.findtext('defName'):x.findtext('./stages/li/baseMoodEffect') for x in xml.findall('ThoughtDef')}
assert {f'RHF_MercyFreedom{i}':v for i,v in enumerate(['3','6','9'],1)}.items() <= thoughts.items()
assert {f'RHF_MercyTrade{i}':v for i,v in enumerate(['1','2','3'],1)}.items() <= thoughts.items()
assert 'DropPodUtility.DropThingsNear' in h and 'DropCellFinder.TradeDropSpot' in h
assert 'GenPlace.TryPlaceThing(silver' not in h
assert 'new LookTargets(dropCenter, previousMap)' in h
assert 'record.Negotiated || record.PendingSilver > 0' in m
assert 'record.Enslaved || record.ForcedConversion' in m
assert 'record.CrueltyScore > 0 || record.WceOperations > 0' in m
assert 'record.MercyAwarded = true' in m
assert 'Scribe_Values.Look(ref MercyAwarded' in h
assert 'RHFMercy.TryAward(r, ordinarySale: false)' in h
assert 'RHFMercy.OnOrdinarySale(pawn)' in patch
assert 'Notify_PawnSold' in patch and 'RHFOrdinarySaleHook' in patch
assert 'RHF_MercyTrade' in m and 'RHF_MercyFreedom' in m
assert 'charity_' in m and 'mercy_' in m
assert 'name.StartsWith("Charity_"' in notes
assert 'parms.spawnCenter = nearPrison' in ops and 'RescueDropCenter' in ops
assert 'SafeRescueDropPad(map,c)' in ops and 'NJRRescueInteriorS41.DirectDoor' in ops
assert 'squared<16 || (roof!=null && squared<25)' in ops
assert 'Рейд отменён' in ops or 'рейд отменён' in ops
assert '19 — Проверить бонусы идеологии и милосердия' in dbg
assert '0.16.0-beta1' in (r/'.github/workflows/build.yml').read_text()
print('PASS: 0.8 mercy/charity, sale vs ransom, physical ransom pods, prison-side rescue drop, diagnostics.')
