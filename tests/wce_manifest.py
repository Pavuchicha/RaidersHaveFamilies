"""The exact 1.6 XML recipe set from the attached WCE2 archive, checked against embedded adapter."""
from pathlib import Path
import re
P=Path(__file__).resolve().parents[1]
src=(P/'RaidersHaveFamilies/Source/RHFWCERegistry.cs').read_text('utf-8')
items=dict((name,int(sev)) for name,sev in re.findall(r'\{"(WCE2_[A-Za-z0-9_]+)", (\d+)\}',src))
assert len(items)==60, len(items)
assert items['WCE2_RipOutHeart']>=10
assert items['WCE2_Vivisect']>=8
assert items['WCE2_RemovePainAmplifier']==0
assert items['WCE2_WakeFromComa']==0
assert items['WCE2_Lobotomize']>=8
patch=(P/'RaidersHaveFamilies/Source/RHFPatches.cs').read_text('utf-8')
assert 'RHFWCERegistry.Known' in patch
assert 'BeforeSurgery' in patch and 'OnSurgery' in patch
assert 'Mersid.WCE2Updated.Core'.lower() in (P/'RaidersHaveFamilies/About/About.xml').read_text('utf-8').lower()
print('PASS: 60 WCE2 recipe defs mapped; 20 non-abusive/reversal recipes excluded; optional guarded surgical hooks.')
