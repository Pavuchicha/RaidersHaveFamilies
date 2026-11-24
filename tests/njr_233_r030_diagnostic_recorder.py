"""R030 isolation contracts and actual Python ZIP-reader test; not C# or game runtime."""
from pathlib import Path
import importlib.util, json, tempfile, zipfile, re
import xml.etree.ElementTree as ET
ROOT=Path(__file__).resolve().parents[1]
SRC=ROOT/'RaidersHaveFamilies/Source'
text=(SRC/'Diagnostics/NJRTestRecorderR030.cs').read_text('utf-8')
project=ET.parse(SRC/'RaidersHaveFamilies.csproj').getroot()
assert project.findtext('./PropertyGroup/NJRDiagnostics')=='true'
assert 'NJR_TEST_DIAGNOSTICS' in project.findtext('./PropertyGroup/DefineConstants')
assert any(x.attrib.get('Remove')=='Diagnostics/**/*.cs' for x in project.iter('Compile'))
base=(SRC/'RaidersHaveFamilies.cs').read_text('utf-8-sig')
assert base.count('NJRTestRecorderR030.Sample(map)')==1
assert re.search(r'#if NJR_TEST_DIAGNOSTICS\s+if \(NJRTestRecorderR030.Active\) NJRTestRecorderR030.Sample\(map\);\s+#endif',base)
assert ' : MapComponent' not in text and 'Scribe_' not in text
assert 'Rand.' not in text and '.StartJob(' not in text and '.EndCurrentJob(' not in text
assert 'ref bool __result' not in text
for event in ['console','job_request','job_after','job_driver','path_event','engine_event','role_change','route','decision_begin','decision_end','decision_result','cell_result','ai_job_choice','cast_started','pawns','operations','layout_row','objects_changed','objects_removed','sample_cost','recorder_fault','tester_marker','session_start','session_stop']:
    assert f'Write("{event}"' in text,event
for fragment in ['512L * 1024 * 1024','32L * 1024 * 1024','StopLocked("512 MiB','ZipFile.CreateFromDirectory','HookResults.Add("FAILED','if(!Recording || Inside)return','[ThreadStatic]','Writer.Flush()','typeof(RHFOffenseDirectorS41)','typeof(RHFObjectiveRouteV128)']:
    assert fragment in text,fragment
assert sorted(re.findall(r'\[DebugAction\(NJRTestRecorderR030.Category,"(\d\d)',text))==['01','02','03','04','05']
allcs='\n'.join(p.read_text('utf-8-sig') for p in SRC.glob('*.cs'))+text
for typename in re.findall(r'typeof\(((?:RHF|NJR)\w+)\)',text):
    assert re.search(r'\bclass\s+'+typename+r'\b',allcs),typename
spec=importlib.util.spec_from_file_location('reader',ROOT/'tools/njr_diagnostics_read.py');m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
def event(n,kind,data,tick=100):return {'seq':n,'tick':tick,'kind':kind,'data':data}
rows=[event(1,'session_start',{'hooks':['OK sample','FAILED sample']})]
for n in range(2,12):rows.append(event(n,'job_request',{'method':'StartJob','pawn':{'thing':{'id':42,'label':'Shoat'}}}))
rows += [event(12,'route',{'operation':1,'Valid':False,'Expanded':30001,'SearchPending':True}),event(13,'route',{'operation':1,'Valid':True,'Walls':1,'Doors':1}),event(14,'operations',{'items':[{'id':1,'Phase':2,'freed':[42]}]}),event(15,'console',{'level':'Error','args':['job loop']}),event(16,'console',{'level':'Error','args':['job loop']}),event(17,'tester_marker',{'label':'manual'}),event(18,'sample_cost',{'elapsedMs':4.5})]
with tempfile.TemporaryDirectory() as td:
    p=Path(td); chunks=[rows[:9],rows[9:]]
    for i,chunk in enumerate(chunks,1):
        (p/f'events-{i:04}.jsonl').write_text(''.join(json.dumps(r)+'\n' for r in chunk)+('broken\n' if i==2 else ''),encoding='utf-8')
    z=p/'capture.zip'
    with zipfile.ZipFile(z,'w',zipfile.ZIP_DEFLATED) as f:
        for x in sorted(p.glob('*.jsonl')):f.write(x,x.name)
    a=m.summarize(m.records(z));b=m.summarize(m.records(p));assert a==b
    assert a['same_tick_job_bursts']==[{'pawn':42,'tick':100,'count':10}]
    assert a['recent_routes']['1'][-1]['Valid'] is True
    assert a['last_operation_states'][1]['Phase']==2
    assert a['console_errors_warnings'][0]['count']==2
    assert a['events']['reader_fault']==1 and a['last_seq']==18 and not a['sequence_gaps']
    assert a['sample_cost_ms']['max']==4.5
assert 'python3 tests/njr_233_r030_diagnostic_recorder.py' in (ROOT/'.github/workflows/build.yml').read_text('utf8')
print('PASS R030: isolated recorder contracts + real multi-segment ZIP reader fixture. C# / RimWorld NOT tested.')
