"""Very simple lexical brace check; NOT a compiler or full C# parser."""
from pathlib import Path
base=Path(__file__).resolve().parents[1]/'RaidersHaveFamilies/Source'
for file in base.rglob('*.cs'):
    s=file.read_text('utf-8')
    stack=[];i=0;mode='code'
    while i<len(s):
        ch=s[i]; nxt=s[i+1] if i+1<len(s) else ''
        if mode=='code':
            if ch=='/' and nxt=='/':mode='line';i+=2;continue
            if ch=='/' and nxt=='*':mode='block';i+=2;continue
            if ch=='@' and nxt=='"':mode='verbatim';i+=2;continue
            if ch in '\"\'':mode='string' if ch=='"' else 'char';i+=1;continue
            if ch in '({[':stack.append((ch,i))
            if ch in ')}]':
                assert stack, f'{file.name}: unpaired {ch} at {i}'
                op,ii=stack.pop()
                assert {'(':')','{':'}','[':']'}[op]==ch, f'{file.name}: mismatched {op} at {ii} / {ch} at {i}'
        elif mode=='line':
            if ch=='\n':mode='code'
        elif mode=='block':
            if ch=='*' and nxt=='/':mode='code';i+=2;continue
        elif mode=='verbatim':
            if ch=='"' and nxt=='"':i+=2;continue
            if ch=='"':mode='code'
        else:
            if ch=='\\':i+=2;continue
            if (mode=='string' and ch=='"') or (mode=='char' and ch=="'"):mode='code'
        i+=1
    assert not stack, f'{file.name}: unterminated {stack[-1]}'
    assert mode in ('code','line'),f'{file.name}: unterminated string/comment {mode}'
print('PASS: C# structural delimiters in all source files. Does not compile/check API.')
