#!/usr/bin/env python3
from pathlib import Path
import re
import xml.etree.ElementTree as ET
c=Path('RaidersHaveFamilies/Source/RHFPreceptNotes.cs').read_text(encoding='utf-8')
assert '[StaticConstructorOnStartup]' in c
assert 'WCE2_Torture_AlwaysRespected' in c
assert 'WCE2_Torture_RespectedGuilty' in c
assert 'Cannibalism_' in c
assert 'ЕЩЁ НЕ РЕАЛИЗОВАНЫ' in c
assert 'static RHFPreceptNotes()' in c
assert 'AllDefsListForReading' in c
assert 'RHF_PublicCruelty' not in c # docs only, no fake reward code
assert 'GameComponentTick' not in c and 'MapComponentTick' not in c
ET.parse('RaidersHaveFamilies/About/About.xml')
print('RHF v0.3.2 precept tooltip contract: PASS')
