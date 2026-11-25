"""R031 trace regression + independent allocation policy model, not a RimWorld test."""
import json
from pathlib import Path
R=Path(__file__).resolve().parents[1]; S=R/'RaidersHaveFamilies/Source'
a=json.loads((R/'tests/fixtures/r032_runtime_extract.json').read_text())
assert [x['revision'] for x in a]==['R031']*3
assert [x['max_duplicate'] for x in a]==[8,6,2]
assert [x['wall_hits_by_weapon'].get('MeleeWeapon_Knife',0) for x in a]==[36,54,0]
assert [x['wall_hits_by_weapon'].get('MeleeWeapon_BreachAxe',0) for x in a]==[0,0,4]
assert all(sum(1 for v in x['members'].values() if v[1]=='MeleeWeapon_BreachAxe')>=2 for x in a)
p=(S/'RHFRescuePlannerS41.cs').read_text();j=(S/'RHFRescueRoleJobGiverS41.cs').read_text();b=(S/'RHFTacticalBoardS41.cs').read_text();c=(S/'RHFRescueCommandV124.cs').read_text();r=(S/'RHFObjectiveRouteV128.cs').read_text();f=(S/'NJRRescueFormationR032.cs').read_text();w=(S/'RHFCombatPlannerV125.cs').read_text()
assert 'Math.Min(3,Math.Max(1,available-2))' in p
assert 'breachSlots.Add(actor,slot);allocated.Add(slot)' in p and 'board.Assign(soldier,RHFRoleS41.Breach,obstacle,strikeCell,now)' in p
assert 'PermitsAssaultBreach(pawn,order.Objective)' in j and 'PermitsAssaultBreach(p,structure)' in c and 'board.PermitsAssaultBreach(soldier,target)' in p
assert 'Barrier==barrier && order!=null' in b and 'order.Objective==barrier' in b
assert 'else if(op.Phase!=2 || op.BoardS41?.PermitsAssaultBreach(pawn,order.Objective)!=true)' in j and 'if(op.Phase==2 && !NJRRescueAccessR040.PermitsExit(op,obstacle))' in j
assert 'RHFRoleS41.Breach &&' in p and 'previous.Objective==obstacle' in p
assert 'RHFTactical.IsBreachAxe(pawn)?10000:0' in w
assert 'NJRRescueFormationR032.StrikeCell(pawn.Map,obstacle,order.Cell,pawn)' in j
assert 'PathEndMode.Touch,pawn' in f and 'if(d!=1 && d!=2)return false;' in f
assert 'allocated.Contains(cell)' in r and 'FormationLegSafe' in r and 'nearest+18' in r
assert 'allocated.Add(way)' in p and 'obsoleteDestination' in p and 'contactSlots.Add(contact)' in p
# Model the policy on the actual rosters, with three legal strike cells and
# a non-operator standing in one of them. This is a synthetic geometry, not
# replay of the native game. A friendly occupant must be assigned a new slot.
for record in a:
 roster=[(int(k),v[1]) for k,v in record['members'].items() if v[1]]
 ranked=sorted(roster,key=lambda x: (x[1]!='MeleeWeapon_BreachAxe',x[1]!='MeleeWeapon_Knife',x[0]))
 melee=[x for x in ranked if x[1].startswith('MeleeWeapon')]
 slots=[(0,1),(1,1),(-1,1)]
 n=min(3,max(1,len(roster)-2),len(melee),len(slots))
 permits={pawn:slot for (pawn,_),slot in zip(melee[:n],slots)}
 assert len(set(permits.values()))==n and all(pawn in permits for pawn,weapon in melee[:2])
 support={pawn:(i,4) for i,(pawn,_) in enumerate(roster) if pawn not in permits}
 assert not (set(permits.values()) & set(support.values()))
 # All demolition permits point to a single barrier, never a neighboring wall.
 barrier=(0,0); orders={pawn:('Breach',barrier) for pawn in permits}
 def permitted(pawn,target):return target==barrier and orders.get(pawn)==('Breach',target)
 assert all(permitted(pawn,barrier) and not permitted(pawn,(1,0)) for pawn in permits)
 assert all(not permitted(pawn,barrier) for pawn in support)
 # A sealed one-cell approach admits one operator; no overlap is required.
 assert len(dict(zip(permits,[(0,1)])))==1
print('PASS R032: 3 recorded traces; distinct-cell, equipment priority, same-barrier and narrow-slot model. NOT gameplay.')
