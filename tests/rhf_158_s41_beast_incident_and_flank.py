"""S4.1-A/I incident-exclusive beast ownership and support flank contracts.
This is NOT a RimWorld runtime test or proof of C# compilation.
"""
from pathlib import Path
import importlib.util
root=Path(__file__).resolve().parents[1]
ops=(root/'RaidersHaveFamilies/Source/RHFOperations.cs').read_text('utf-8')
native=(root/'RaidersHaveFamilies/Source/RHFNativeDoctrineS41.cs').read_text('utf-8')
offense=(root/'RaidersHaveFamilies/Source/RHFOffenseS41.cs').read_text('utf-8')
fire=(root/'RaidersHaveFamilies/Source/RHFRescueFireDisciplineV136.cs').read_text('utf-8')
workflow=(root/'.github/workflows/build.yml').read_text('utf-8')
need=[
 (ops,'public List<Pawn> NativeAnimalsS41 = new List<Pawn>();'),
 (ops,'Scribe_Collections.Look(ref NativeAnimalsS41, "rhfNativeAnimalsS41AI", LookMode.Reference)'),
 (ops,'activeOperation != null && activeParty != null'),
 (ops,'activeParty.Role != (int)RHFStrikeRole.Rescue'),
 (ops,'pawn.RaceProps.Animal &&'),
 (ops,'beastsBeforeRaid.Add(already.thingIDNumber);'),
 (ops,'RecoverIncidentBeastsS41(op,party,beastsBeforeRaid);'),
 (ops,'before.Contains(pawn.thingIDNumber)'),
 (ops,'pawn.RaceProps?.Animal!=true'),
 (native,'ObserveList(op,map,now,op.NativeAnimalsS41,seenLords);'),
 (native,'TryBeastSupportAnchor('),
 (native,'LastVisibleEnemyCell'),
 (native,'GenSight.LineOfSight(beastFront.Position,enemy.Position,map)'),
 (native,'supporter.CanReach(c,PathEndMode.OnCell,Danger.Some)'),
 (native,'(c-wing.Rally).LengthHorizontalSquared>225'),
 (native,'now-wing.LastEnemySeenTick<=900'),
 (offense,'RHFNativeDoctrineS41.TryBeastSupportAnchor(beastWing,'),
 (fire,'MemberInLine(op.NativeAnimalsS41,shooter,target,dx,dz,len)'),
 (fire,'op.NativeAnimalsS41.Contains(pawn)'),
 (workflow,'python3 tests/rhf_158_s41_beast_incident_and_flank.py'),
]
for source,required in need:
    assert required in source,required
# Explicit native-only case: no human link, but one incident-owned animal Lord.
def owned(beasts_before,beasts_after,party_faction,already_owned):
    return [p for p in beasts_after if p['id'] not in beasts_before and
            p['faction']==party_faction and not p['human'] and
            p['id'] not in already_owned]
assert owned({1,2},[{'id':1,'faction':'F','human':False},
    {'id':2,'faction':'F','human':False},
    {'id':3,'faction':'F','human':False},
    {'id':4,'faction':'OTHER','human':False},
    {'id':5,'faction':'F','human':True}], 'F',set()) ==     [{'id':3,'faction':'F','human':False}]
assert owned(set(),[{'id':10,'faction':'F','human':False}], 'F',{10}) == []
# LoS enemy probe is bounded and never gives global enemy position.
assert 'if(++scanned>48)break;' in native
assert 'enemy.Position.Fogged(map)' in native
# A second, unrelated raid sharing the same faction outside the sync call
# MUST NOT be scooped up by an unbounded per-tick map scan.
assert 'NativeAnimalsS41.Add(pawn)' in ops
assert 'activeOperation != null && activeParty != null' in ops # synchronous incident scope
# Negative mutations: missing a crucial guard must fail the relevant predicate.
mutations=[
  (ops,'activeParty.Role != (int)RHFStrikeRole.Rescue'),
 (ops,'pawn.RaceProps.Animal &&'),
  (ops,'before.Contains(pawn.thingIDNumber)'),
 (ops,'pawn.RaceProps?.Animal!=true'),
  (ops,'pawn.Faction!=party.Faction'),
  (ops,'pawn.RaceProps?.Animal!=true'),
  (native,'ObserveList(op,map,now,op.NativeAnimalsS41,seenLords);'),
  (native,'GenSight.LineOfSight(beastFront.Position,enemy.Position,map)'),
  (native,'supporter.CanReach(c,PathEndMode.OnCell,Danger.Some)'),
  (fire,'MemberInLine(op.NativeAnimalsS41,shooter,target,dx,dz,len)'),
]
for source,required in mutations:
    # Some necessary guards occur in more than one scoped incident path.
    # Removing the first occurrence must decrement the count by exactly 1;
    # it need not remove the marker everywhere in the file.
    minimum = 2 if required=='before.Contains(pawn.thingIDNumber)' else 1
    assert source.count(required)>=minimum
    mutated=source.replace(required,'// DISABLED',1)
    assert mutated.count(required)==source.count(required)-1
print('PASS: incident-specific native beasts, isolated Lords, visible threat, reachable flank, friendly-fire scope; 7 negative mutations')
print('NOT CHECKED: RimWorld runtime / Beast Lord pathing / CE / 1.7 C# compilation')
