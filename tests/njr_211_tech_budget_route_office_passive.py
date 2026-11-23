"""R004 source + deterministic model checks: tech policy, force budget, safe-first corridors,
role kit accounting, event-driven cleanup, and save-compatibility. STATIC ONLY.
Run with python3 tests/njr_211_tech_budget_route_office_passive.py.
"""
from pathlib import Path
import heapq
R=Path(__file__).resolve().parents[1]
S=R/'RaidersHaveFamilies/Source'
read=lambda n: (S/n).read_text(encoding='utf-8')
forces=read('NJRForceCompositionS42.cs')
ops=read('RHFOperations.cs')
route=read('RHFObjectiveRouteV128.cs')
kit=read('NJRAdaptiveLoadoutsS42.cs')
orb=read('NJROrbitalSupportS42.cs')
offices=read('RHFOfficeCatalog.cs')
ledger=read('RaidersHaveFamilies.cs')
patch=read('RHFPatches.cs')
hostages=read('RHFHostageSystems.cs')
member=read('RHFTactical.cs')
assert 'techLevel < TechLevel.Industrial' in forces
assert 'techLevel >= TechLevel.Industrial' in forces
assert 'party.Drop=false' in ops and 'party.Siege=false' in ops
assert 'f.HostileTo(sponsor) || sponsor.HostileTo(f)' in hostages
assert 'PawnGenOption' in forces and 'pawnGroupMakers' in forces
assert 'party.AnimalBudgetS42' in ops and 'party.LoadoutBudgetS42' in ops
assert 'party.Points - party.SupplyBudget -' in ops
assert 'party.AnimalSpentS42+=selected.combatPower' in forces
assert 'party.Role==(int)RHFStrikeRole.Diversion' in forces
assert 'op.Diversion!=null && op.Diversion.Count>0' in ops
assert 'Lord lord=leader.GetLord()' in forces and 'lord.AddPawn(animal)' in forces
assert 'private static readonly string[] Heavy=' in kit
for weapon in ['Gun_Minigun','Gun_TripleRocket','Gun_DoomsdayRocket','MeleeWeapon_Mace','MeleeWeapon_ZeusHammer','MeleeWeapon_PlasmaSword']:
    assert weapon in kit
assert 'ThingDefOf.Uranium' in kit and 'ArmorUpgrade' in kit
assert 'kindGunCache' in kit and 'FactionTaggedGuns' in kit
assert 'replacement.MarketValue-previousValue' in kit
assert 'CombatExtended && selected.IsRangedWeapon' in kit
assert 'OrbitalTargeterPowerBeam' in orb and 'VerbTracker' in orb
assert 'TryStartCastOn' in orb and 'FriendlyNear(op,map' in orb
assert 'njrAnimalBudgetS42' in ops and 'njrLoadoutBudgetS42' in ops
assert 'njrWeaponPresetS42' in member and 'njrOrbitalItemS42' in member
assert 'Math.Max(map.Size.x,map.Size.z));' in route
assert route.count('PlanBounded(map,origins,captive,fresh,') == 1
assert 'forbidTurretExposure' not in route
assert 't.Origin.GetRoom(map)==cell.GetRoom(map)' in route
assert 'WEIGHTED-R024' in route
assert 'private static readonly string[][] Clan' in offices
assert 'private static readonly string[][] Outlaw' in offices
assert 'tech<TechLevel.Medieval ? Clan' in offices
assert 'if (tick < nextHostageCheck) return;' in ledger
assert 'Hostages.Count>0?90000:150000' in ledger
assert 'ReprisalCases.Count>0 && tick>=nextCaseCheck' in ledger
assert 'OnPrisonerExit' in patch and 'ledger.Hostages.Remove(r)' in hostages and 'InvalidateHostageIndex()' in hostages
assert 'private void CleanupStaleHostagesS42()' in ledger
assert 'CleanupStaleHostagesS42();' in ledger

# R024: independent weighted graph regression. One turret-covered doorway
# is not automatically worse than demolishing three unrelated walls.
def plan(edges):
    q=[(0,'start',())]
    seen={}
    while q:
        cost,node,trail=heapq.heappop(q)
        if node=='end':return cost, trail
        if node in seen and seen[node]<=cost:continue
        seen[node]=cost
        for nxt,kind in edges.get(node,()):
            weight={'walk':10,'wall':8500,'door':800,'turret':600}[kind]
            heapq.heappush(q,(cost+weight,nxt,trail+(kind,)))
    return None
G={'start':[('a','wall'),('d','door')],
   'a':[('b','wall')],'b':[('end','wall')],
   'd':[('e','door')],'e':[('end','turret')]}
assert plan(G)[1]==('door','door','turret')
G['d']=[]
assert plan(G)[1]==('wall','wall','wall')
G['b']=[]
assert plan(G) is None

# Reserve model mirrors critical declared percentages and minimum human core.
def reserve(points,low,role):
    supply=min(300,max(0,points*.12)) if role else 0
    animals=min(points*.42,5500) if low and points>=450 else 0
    kit=min(points*.23,3600) if role and points>=650 else 0
    available=max(0,points-supply-350)
    if animals+kit>available and animals+kit>0:
        factor=available/(animals+kit)
        animals*=factor;kit*=factor
    return supply,animals,kit,max(1,points-supply-animals-kit)
for points in (250,450,650,2000,8000,15000):
    for low in (False,True):
        supply,animals,kit,remaining=reserve(points,low,True)
        assert remaining>=1 and supply+animals+kit+remaining<=points+0.0001
        if not low:assert animals==0
assert reserve(10000,True,True)[1]>reserve(1000,True,True)[1]
print('PASS NJR R004: technology restrictions, budgeted beasts and specialized kits, '
      'weighted route semantics, faction offices, event cleanup and Scribe contracts (STATIC ONLY).')
