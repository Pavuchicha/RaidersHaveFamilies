#!/usr/bin/env python3
"""Read an NJR test recording ZIP or folder without extracting it. Stdlib only."""
import argparse
import collections
import json
from pathlib import Path
from zipfile import ZipFile


def records(path):
    """Stream events; tolerate a truncated last line after a game crash."""
    path = Path(path)
    def parse(lines, name):
        for line_no, line in enumerate(lines, 1):
            try:
                yield json.loads(line)
            except (ValueError, UnicodeError) as e:
                yield {"kind": "reader_fault", "data": {"file": name, "line": line_no, "error": str(e)}}
    if path.is_dir():
        for f in sorted(path.glob('events-*.jsonl')):
            with f.open('rb') as stream:
                yield from parse(stream, f.name)
    else:
        with ZipFile(path) as z:
            for name in sorted(n for n in z.namelist() if Path(n).name.startswith('events-') and n.endswith('.jsonl')):
                with z.open(name) as stream:
                    yield from parse(stream, name)


def summarize(events):
    counts = collections.Counter()
    jobs = collections.Counter()
    bursts = collections.Counter()
    console = collections.Counter()
    routes = collections.defaultdict(lambda: collections.deque(maxlen=8))
    ops = {}
    starts = []
    faults = collections.deque(maxlen=20)
    markers = []
    prev_seq = None
    gaps = []
    sample_costs = []
    for e in events:
        kind, d = e.get('kind'), e.get('data') or {}
        counts[kind] += 1
        seq, tick = e.get('seq'), e.get('tick')
        if isinstance(seq, int):
            if prev_seq is not None and seq != prev_seq + 1:
                gaps.append([prev_seq, seq])
            prev_seq = seq
        if kind == 'session_start':
            starts.append(d)
        if kind in ('reader_fault', 'recorder_fault'):
            faults.append(d)
        if kind == 'tester_marker':
            markers.append({'seq': seq, 'tick': tick, **d})
        if kind == 'job_request' and d.get('method') == 'StartJob':
            pawn = (d.get('pawn') or {}).get('thing') or {}
            pid = pawn.get('id', -1)
            jobs[(pid, pawn.get('label', '?'))] += 1
            bursts[(pid, tick)] += 1
        if kind == 'console' and str(d.get('level', '')).startswith(('Error', 'Warning')):
            args = d.get('args') or []
            console[(d.get('level'), str(args[0] if args else '')[:400])] += 1
        if kind == 'route':
            routes[d.get('operation')].append({k: d.get(k) for k in ('Valid','SearchPending','BudgetExceeded','Expanded','Walls','Doors','TotalCost','DangerCost','Explanation','barrier','from','to')} | {'seq': seq, 'tick': tick})
        if kind == 'operations':
            for op in d.get('items') or []:
                ops[op['id']] = {'tick':tick, **op}
        if kind == 'decision_end' and d.get('operation'):
            op = d['operation'];ops[op['id']] = {'tick':tick, **op}
        if kind == 'sample_cost' and isinstance(d.get('elapsedMs'), (int, float)):
            sample_costs.append(d['elapsedMs'])
    return {'events':dict(counts), 'last_seq':prev_seq, 'sequence_gaps':gaps[:100], 'sessions':starts,
            'faults':list(faults), 'markers':markers,
            'job_requests_top':[{'pawn':k[0],'label':k[1],'count':n} for k,n in jobs.most_common(25)],
            'same_tick_job_bursts':[{'pawn':k[0],'tick':k[1],'count':n} for k,n in bursts.most_common(30) if n>=5],
            'console_errors_warnings':[{'level':k[0],'message':k[1],'count':n} for k,n in console.most_common(50)],
            'recent_routes':{str(k):list(v) for k,v in routes.items()},'last_operation_states':ops,
            'sample_cost_ms':{'count':len(sample_costs),'max':max(sample_costs,default=0),'mean':sum(sample_costs)/max(1,len(sample_costs))}}


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('recording', type=Path)
    p.add_argument('--pawn',type=int,help='Show events containing this pawn ID, instead of summary')
    p.add_argument('--operation',type=int,help='Show events containing this operation ID, instead of summary')
    p.add_argument('--kind',action='append',help='Filter event types (repeatable)')
    args=p.parse_args()
    stream=records(args.recording)
    if args.pawn is None and args.operation is None and not args.kind:
        print(json.dumps(summarize(stream),ensure_ascii=False,indent=2));return
    def match(x,key,value):
        if isinstance(x,dict):
            return x.get(key)==value or any(match(v,key,value) for v in x.values())
        return isinstance(x,list) and any(match(v,key,value) for v in x)
    for e in stream:
        d=e.get('data') or {}
        if args.kind and e.get('kind') not in args.kind:continue
        if args.pawn is not None and not (match(d,'pawn',args.pawn) or match(d,'id',args.pawn) or match(d,'captive',args.pawn) or match(d,'escort',args.pawn)):continue
        if args.operation is not None and not (match(d,'operation',args.operation) or (isinstance(d.get('operation'),dict) and d['operation'].get('id')==args.operation) or (e.get('kind')=='operations' and any(op.get('id')==args.operation for op in d.get('items',[])))):continue
        print(json.dumps(e,ensure_ascii=False))

if __name__=='__main__':main()
