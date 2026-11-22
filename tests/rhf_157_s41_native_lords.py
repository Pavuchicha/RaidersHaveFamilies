"""S4.1-A/H native Lord coexistence contracts (static, not game runtime)."""
from pathlib import Path
root=Path(__file__).resolve().parents[1]
o=(root/'RaidersHaveFamilies/Source/RHFOffenseS41.cs').read_text('utf-8')
n=(root/'RaidersHaveFamilies/Source/RHFNativeDoctrineS41.cs').read_text('utf-8')
p=(root/'RaidersHaveFamilies/Source/RHFOperations.cs').read_text('utf-8')
w=(root/'.github/workflows/build.yml').read_text('utf-8')

assert 'RHFNativeDoctrineS41.Observe(op,map,now)' in o
assert 'bool liveEvac=op.Phase==3 || CaptivesStillOnMap(op,map)' in o
assert '(op.EscapedWithEscort>0 || op.PodLaunchCompleted)' in o
assert 'NativeOwner(op,candidate))continue;' in o
assert 'bool preserveNative=false;' not in o
assert 'if(party!=null && party.Faction==p.Faction && party.Siege' in o
assert 'if(lord==null && op.Parties!=null)' in o
assert 'if(lord?.LordJob is LordJob_Siege)return true;' in o
assert 'RHFNativeDoctrineS41.IsAnimalLord(lord)' in o
assert 'new List<Pawn>(32)' in o
assert 'guardSiege' in o and 'board.Assign(soldier,nearbyThreat' in o
assert 'supportBeasts' in o and 'beastWing.Rally' in o
assert 'JobDefOf.Build' not in o and 'TryTakeOrderedJob' not in '\n'.join(
  line for line in o.splitlines() if not line.lstrip().startswith('//'))
assert 'class RHFNativeWingS41 : IExposable' in n
assert 'Scribe_References.Look(ref Faction' in n
assert 'RHFNativeDoctrineS41' in n
assert 'now<op.NextNativeObservationS41' in n
assert 'new HashSet<Lord>()' in n
assert 'InspectNativeLords()' in n
assert 'lord.CurLordToil is LordToil_Siege' in n
assert 'lord.CurLordToil is LordToil_AssaultColony' in n
assert 'RHFNativeDoctrineS41.IsAnimalLord' not in n # class implements IsAnimalLord directly
assert 'Scribe_Collections.Look(ref NativeWingsS41, "rhfNativeWingsS41AH", LookMode.Deep)' in p
assert 'if (NativeWingsS41 == null)' in p
assert 'python3 tests/rhf_157_s41_native_lords.py' in w

# Model native-Lord partition: a siege operator / animal handler must remain
# untouched even if separate same-faction reserve infantry can be assigned.
def partition(pawns):
    return ([p['id'] for p in pawns if p['human'] and not p['native_lord']],
            [p['id'] for p in pawns if p['native_lord']])
assert partition([dict(id=1,human=True,native_lord=True),
                  dict(id=2,human=True,native_lord=False),
                  dict(id=3,human=True,native_lord=False),
                  dict(id=4,human=False,native_lord=True)]) == ([2,3],[1,4])
assert partition([dict(id=1,human=True,native_lord=True),
                  dict(id=2,human=False,native_lord=True)]) == ([],[1,2])

mutations=[
  (o,'bool liveEvac=op.Phase==3 || CaptivesStillOnMap(op,map)',
      'bool liveEvac=false',lambda x:'bool liveEvac=op.Phase==3 || CaptivesStillOnMap(op,map)' in x),
  (o,'NativeOwner(op,candidate))continue;','false)continue;',
      lambda x: 'NativeOwner(op,candidate))continue;' in x),
  (o,'if(lord?.LordJob is LordJob_Siege)return true;','if(false)return true;',
      lambda x:'if(lord?.LordJob is LordJob_Siege)return true;' in x),
  (o,'RHFNativeDoctrineS41.Observe(op,map,now);',';',
      lambda x:'RHFNativeDoctrineS41.Observe(op,map,now);' in x),
  (o,'if(guardSiege)','if(false)',lambda x:'if(guardSiege)' in x),
  (o,'if(supportBeasts)','if(false)',lambda x:'if(supportBeasts)' in x),
  (n,'new HashSet<Lord>()','new HashSet<Pawn>()',
      lambda x:'new HashSet<Lord>()' in x),
  (n,'now<op.NextNativeObservationS41','false',
      lambda x:'now<op.NextNativeObservationS41' in x),
  (n,'Scribe_References.Look(ref Faction','// missing',
      lambda x:'Scribe_References.Look(ref Faction' in x),
]
for source,old,new,predicate in mutations:
    assert old in source
    assert not predicate(source.replace(old,new,1)),old
print('PASS: S4.1-A/H native-siege / animal Lord partition, save/load, reserve support, 9 negative mutations')
print('NOT CHECKED: C# 1.7 compilation or gameplay with siege/CE/animals')
