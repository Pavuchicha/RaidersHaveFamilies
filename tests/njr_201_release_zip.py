"""Verifies the *actual built install ZIP* in CI, not a source-only preview."""
import sys,zipfile,xml.etree.ElementTree as ET
from pathlib import Path
assert len(sys.argv)==2,'Usage: python3 tests/njr_201_release_zip.py <zip>'
path=Path(sys.argv[1])
assert path.is_file() and path.stat().st_size>2048,path
with zipfile.ZipFile(path) as z:
    bad=z.testzip()
    assert bad is None,bad
    names=z.namelist()
    prefix='RaidersHaveFamilies/'
    required=[
        prefix+'Assemblies/RaidersHaveFamilies.dll',
        prefix+'About/About.xml',
        prefix+'Languages/Russian/Keyed/RHF.xml',
        prefix+'Languages/English/Keyed/RHF.xml',
        prefix+'Defs/DutyDefs/RHF_OffenseS41.xml',
        prefix+'Defs/JobDefs/RHF_PodCargoS41.xml',
    ]
    assert all(n in names for n in required),[x for x in required if x not in names]
    assert not any('/Source/' in n or '/tests/' in n or '.csproj' in n for n in names)
    assert len(names)==len(set(names)), 'duplicate ZIP entries'
    dll=z.read(required[0]);assert len(dll)>3000 and dll[:2]==b'MZ'
    meta=ET.fromstring(z.read(required[1]));assert meta.findtext('name')=='NJR — Not Just Raiders'
    assert meta.findtext('packageId')=='pavuk.raidshavefamilies'
    print('PASS NJR: REAL install ZIP CRC, compiled MZ DLL, package identity, localization, Defs, file layout.')
