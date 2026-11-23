"""S4.1 A/C review follow-up static contracts; NOT C# compile or gameplay proof.
Catches the specific failure modes identified by the second Gemini audit.
"""
from pathlib import Path
import re
R=Path(__file__).resolve().parents[1]
S=R/'RaidersHaveFamilies'/'Source'
P=(S/'RHFRescuePlannerS41.cs').read_text('utf-8')
G=(S/'RHFRescueRoleJobGiverS41.cs').read_text('utf-8')
B=(S/'RHFTacticalBoardS41.cs').read_text('utf-8')
E=(S/'RHFAutoLiberationS41.cs').read_text('utf-8')
V=(S/'RHFRescueCommandV124.cs').read_text('utf-8')
O=(S/'RHFOperations.cs').read_text('utf-8')
W=(R/'.github/workflows/build.yml').read_text('utf-8')

def contract(p,g,b,e,v,o,w):
    # 1: mission Lord binding is present in legacy code and explicit duty
    # refresh is made after adoption, including interrupted / loaded pawns.
    assert 'RHFRescueCommandV124.StabilizeFreed(op,captive);' in p
    assert 'Lord lord=captive.GetLord();' in v
    assert 'lord.CurLordToil?.UpdateAllDuties();' in v
    assert 'if(op.Freed.Contains(captive))' in p
    # 2: blocked LoS is never a no-job outcome; first approach the obstacle
    # from a physically reachable cell, then use safe melee without CE blast.
    assert 'Job approach=ApproachBarrier(pawn,obstacle);' in g
    assert 'return Task(JobDefOf.AttackMelee,obstacle,900);' in g
    assert 'pawn.CanReach(site,PathEndMode.OnCell,Danger.Deadly)' in g
    assert 'RHFRescueFireDisciplineV136.UnsafeLine' in g
    # 3: only a real rescue target and real contact may be adopted; a wall
    # breaking is an event to replan, never license to free someone remotely.
    assert 'nameof(Building.Destroy)' in e
    assert 'RHFRescuePlannerS41.OnBarrierDestroyed' in e
    assert 'SameAccessibleRoom(rescuer,captive,map)' in p
    assert 'rescuer.CanReach(captive,PathEndMode.Touch,Danger.Some)' in p
    assert 'if(op.Targets!=null && op.Targets.Contains(pawn)' in p
    assert '!pawn.IsSlaveOfColony && PhysicalContact(op,pawn,map)' in p
    assert 'else if(InTouch(op,captive,map))' in p
    assert 'map.GetComponent<RHFOperationController>()?.S41FreeTarget' in p
    # 4: freed pawns receive roles in phase 1, before next target work.
    assert 'AssignFreedEscort(op,map,now,escortedDuringAssault);' in p
    assert 'escortedDuringAssault.Contains(soldier)' in p
    assert 'if(op.Phase==2)Extract(controller,op,now);' in p
    assert 'if(next!=captive)Infiltrate(controller,op,now);' in p
    assert '!RHFRescuePlannerS41.Enabled && !p.Downed' in o
    # 5: replace incapable sapper and clean residual roles.
    assert 'Breacher.InMentalState' in b
    assert '!p.InMentalState' in p
    assert 'CapableOf(PawnCapacityDefOf.Manipulation)' in b
    assert 'board.Orders.Clear()' in p
    assert 'python3 tests/rhf_152_s41_agent_review_fixes.py' in w

contract(P,G,B,E,V,O,W)
mutations=[
 (P,G,B,E,V.replace('lord.CurLordToil?.UpdateAllDuties();',''),O,W),
 (P,G.replace('Job approach=ApproachBarrier(pawn,obstacle);',''),B,E,V,O,W),
 (P,G.replace('return Task(JobDefOf.AttackMelee,obstacle,900);','return null;'),B,E,V,O,W),
 (P,G,B,E.replace('nameof(Building.Destroy)','"irrelevant"'),V,O,W),
 (P.replace('SameAccessibleRoom(rescuer,captive,map)','true'),G,B,E,V,O,W),
 (P.replace('if(next!=captive)Infiltrate(controller,op,now);','Infiltrate(controller,op,now);'),G,B,E,V,O,W),
 (P.replace('escortedDuringAssault.Contains(soldier)','false'),G,B,E,V,O,W),
 (P,G,B.replace('Breacher.InMentalState','false'),E,V,O,W),
 (P,G,B,E,V,O.replace('!RHFRescuePlannerS41.Enabled && !p.Downed','!p.Downed'),W),
 (P,G,B,E,V,O,W.replace('python3 tests/rhf_152_s41_agent_review_fixes.py','')), 
]
for i,args in enumerate(mutations,1):
    try: contract(*args)
    except AssertionError: continue
    raise AssertionError(f'Negative mutation {i} unexpectedly passed')
print('PASS: S4.1 A/C seven audit defects, 10 mutation guards (STATIC ONLY)')
