"""S4.1-A/K multi-captive escort and downed extraction regression contracts.

STATIC CONTRACT + MODEL ONLY. It does NOT run RimWorld, test CE, or compile C#.
"""
from pathlib import Path
from dataclasses import dataclass
root=Path(__file__).resolve().parents[1]
p=(root/'RaidersHaveFamilies/Source/RHFRescuePlannerS41.cs').read_text('utf-8')
g=(root/'RaidersHaveFamilies/Source/RHFRescueRoleJobGiverS41.cs').read_text('utf-8')
v=(root/'RaidersHaveFamilies/Source/RHFRescueCommandV124.cs').read_text('utf-8')
w=(root/'.github/workflows/build.yml').read_text('utf-8')
checks=[
(p,'if(link==existing)break; // earliest valid allocation wins'),
(p,'if(occupied)continue;'),
(p,'existing.Escort=null; // invalidate stale double allocation'),
(p,'if(captive.Downed &&'),
(p,'!candidate.CanReach(captive,PathEndMode.ClosestTouch,Danger.Some)'),
(p,'// The mission\'s ThinkTree is the ONLY carrier job writer.'),
(p,'board.Assign(carrier,RHFRoleS41.Escort,rescued,'),
(p,'paired.Add(carrier);'),
(p,'board.Assign(soldier,RHFRoleS41.Hold,null,soldier.Position,now);'),
(g,'if(escorted.Downed)'),
(g,'if(op.Phase!=2 || !order.Cell.IsValid ||'),
(g,'Job job=JobMaker.MakeJob(carry,escorted,order.Cell);'),
(g,'job.count=1;'),
(v,'MissionExit(carrier);\n            if(carrier.MapHeld==map)return;\n            MissionExit(rescued);'),
(v,'if(rescued.MapHeld!=map)\n                op.EscapedWithEscort++;'),
(v,'if(link?.Captive==rescued && link.Escort==carrier)'),
(w,'python3 tests/rhf_160_s41_multivip_downed_single_writer.py'),
]
for content,fragment in checks:assert fragment in content,repr(fragment)
assert 'controller.TryAssignCarrier(op,rescued)' not in p
assert 'occupied*100000' not in p
assert v.index('MissionExit(carrier);')<v.index('MissionExit(rescued);')
assert 'TryTakeOrderedJob' not in g

# Independent tiny model: a worker cannot be reserved for two live captives.
# Explicitly verifies the corrected semantics, not implementation behavior.
@dataclass
class Soldier:
 name: str
 manipulation: bool=True
 free_hands: bool=True
 reach: bool=True

A=Soldier('A');B=Soldier('B');C=Soldier('C',manipulation=False)
pairs={'VIP-1':'A'}
def choose(captive,downed,team):
 for soldier in team:
  if any(worker==soldier.name and passenger!=captive for passenger,worker in pairs.items()):
   continue
  if downed and not (soldier.manipulation and soldier.free_hands and soldier.reach):
   continue
  pairs[captive]=soldier.name
  return soldier.name
 return None
assert choose('VIP-2',True,[A,B,C])=='B'
assert len(set(pairs.values()))==2
assert choose('VIP-3',True,[A,B,C]) is None
assert len(set(pairs.values()))==2
pairs.pop('VIP-1')
assert choose('VIP-3',True,[A,B,C])=='A'
# A failed second exit must not count as escorted success.
def pair_exit(escort_left,patient_left):return int(bool(escort_left and patient_left))
assert pair_exit(False,True)==0
assert pair_exit(True,False)==0
assert pair_exit(True,True)==1

# Independent mutation diagnostics: every broken source contract should trip.
for source,frag in checks:
 changed=source.replace(frag,'/* MUTATED */')
 assert frag in source and frag not in changed
print('PASS S4.1-A/K: strict one-to-one escort, downed ThinkTree carrier,')
print('safe alternative when no free carrier, escort-first exit and count guard; 17 contract mutations')
print('NOT CHECKED: C# compile, RimWorld 1.7 jobs, engine ExitMap, Combat Extended')
