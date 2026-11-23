"""R002 static regression: chooser and fail-closed pod dispatch. NOT a C# build or gameplay test."""
from pathlib import Path
p=Path(__file__).resolve().parents[1]
d=(p/'RaidersHaveFamilies/Source/RHFDialogue.cs').read_text(encoding='utf-8')
g=(p/'RaidersHaveFamilies/Source/NJRGroupRansomV15.cs').read_text(encoding='utf-8')
h=(p/'RaidersHaveFamilies/Source/RHFHostageSystems.cs').read_text(encoding='utf-8')
# Dialogues may list 24 hostages; an individual signed contract is capped at 12.
for s in ('private const int MaximumGroup=12','private const int CandidatePreview=24',
          'group.Count<2 || group.Count>MaximumGroup','if(total!=expectedTotal)return false'):
    assert s in g,s
for s in ('private static DiaNode SelectGroup(', 'const int perPage=8;',
          'currentlySelected','if(next.Count<12)next.Add(selectedPawn)',
          'chosen.Count>=2 && chosen.Count<=12',
          'SignGroup(negotiator,faction,sealedGroup,quote)',
          'Выбрано: ', 'Следующие пленники','Предыдущие пленники'):
    assert s in d,s
assert 'static System.Collections.Generic.List<Pawn> selection' not in d
# Every member saved with the unique issuing map identity and a dispatch attempt barrier.
for s in ('njrGroupOriginMapIdV16','njrGroupPaymentAttemptedV16'):
    assert s in h,s
assert 'r.GroupOriginMapId=map.Parent?.GetUniqueLoadID() ?? "";' in g
assert 'first.GroupOriginMapId!=destination.Parent.GetUniqueLoadID()' in g
assert 'member.GroupPaymentAttempted=true;' in g
assert g.index('member.GroupPaymentAttempted=true;') < g.index('DropPodUtility.DropThingsNear(')
assert 'member.GroupPaymentAttempted || !member.GroupReleaseConfirmed' in g
# A genuinely clear drop cannot be found -> no state commitment; user can request again.
start=g.index('IntVec3 landing=RHFHostageDiplomacy.SafeRansomDrop(destination)')
assert g.index('return false; // absolutely no payout attempt was made',start) < g.index('member.GroupPaymentAttempted=true;',start)
assert 'PendingDeliveryIds(faction)' in d and 'GroupDelivery(negotiator,faction,pendingId)' in d
assert 'UncertainDeliveryIds(faction)' in d and 'UncertainGroupDelivery(' in d
assert 'GroupPaymentAttempted' not in g[g.index('RequestDelivery('):g.index('private static bool TryDeliver(')], 'RequestDelivery must go through Ready() gate'
assert 'int[] clearRadii={6,3,1};' in h
assert 'r.Pawn.IsSlaveOfColony' in g
assert 'if(r.GroupRansomId>0 && !r.GroupReleaseConfirmed)' in h
print('PASS R002 selectable group contracts and guarded delivery request (STATIC ONLY)')
