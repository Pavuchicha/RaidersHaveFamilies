"""NJR V15/V16 grouped ransom state machine static audit; does NOT compile/run RimWorld."""
from pathlib import Path
r=Path(__file__).resolve().parents[1]
h=(r/'RaidersHaveFamilies/Source/RHFHostageSystems.cs').read_text('utf-8')
g=(r/'RaidersHaveFamilies/Source/NJRGroupRansomV15.cs').read_text('utf-8')
l=(r/'RaidersHaveFamilies/Source/RaidersHaveFamilies.cs').read_text('utf-8')
d=(r/'RaidersHaveFamilies/Source/RHFDialogue.cs').read_text('utf-8')
assert 'int NextGroupRansomId = 1;' in l and 'njrNextGroupRansomIdV15' in l
for field in ('GroupRansomId','GroupRansomMembers','GroupRansomSilver','GroupReleaseConfirmed'):
    assert field in h and field+'V15' in h
for field in ('GroupOriginMapId','GroupPaymentAttempted'):
    assert field in h and field+'V16' in h
assert 'private const int MaximumGroup=12;' in g
assert 'group.Count<2 || group.Count>MaximumGroup' in g
assert 'int expectedTotal' in g and 'if(total!=expectedTotal)return false' in g
assert 'var unique=new HashSet<Pawn>();' in g
assert 'r.Pawn?.Map!=map' in g and 'map.Parent==null' in g
assert 'r.PendingSilver=0' in g
assert 'r.GroupReleaseConfirmed=true' in g
assert 'member.GroupPaymentAttempted=true' in g
assert 'member.GroupPaymentAttempted || !member.GroupReleaseConfirmed' in g
assert 'seen==first.GroupRansomMembers' in g
assert 'if(exited)ledger.Hostages.RemoveAt(i)' in g
assert 'RHFHostageDiplomacy.SafeRansomDrop(destination)' in g
assert '!r.Pawn.IsPrisonerOfColony' not in g
assert g.index('member.GroupPaymentAttempted=true') < g.index('DropPodUtility.DropThingsNear(')
assert g.index('DropPodUtility.DropThingsNear(') < g.index('ledger.Hostages[i].Paid=true')
assert 'NJRGroupRansomV15.OnPrisonerExit(ledger,r,previousMap,wasReleased,pawn)' in h
assert 'NJRGroupRansomV15.Audit(ledger)' in h
assert 'if(r.GroupRansomId>0 && !r.GroupReleaseConfirmed)' in h
assert 'contract member died before release' in h
assert 'GroupTerms(negotiator,faction,selectedMap)' in d
assert 'SignGroup(negotiator,faction,sealedGroup,quote)' in d
assert 'NJRGroupRansomV15.Sign(faction,people,negotiator,' in d
print('PASS V15/V16 grouped ransom physical exit gate and delivery idempotency (STATIC)')
