"""R014 source-level regression and small independent reference models.
NOT a RimWorld simulation, DLL compilation, or engine integration test.
"""
from pathlib import Path
S=Path(__file__).resolve().parents[1]/'RaidersHaveFamilies'/'Source'
read=lambda name:(S/name).read_text(encoding='utf-8-sig')
off=read('RHFOffenseS41.cs'); board=read('RHFTacticalBoardS41.cs')
role=read('RHFCombatPlannerV125.cs'); contact=read('NJRCombatContactS44.cs')
assert 'public sealed class RHFObservedTurretS46 : IExposable' in board
assert 'Scribe_References.Look(ref Turret,"turret")' in board
assert 'Scribe_Collections.Look(ref ObservedTurretsS46,' in board
assert '"observedTurretsS46", LookMode.Deep' in board
assert 'ObservedTurretsS46.RemoveAt(i)' in board
assert 'sighting.Turret.Map!=map' in board
assert 'faction.GetUniqueLoadID()' in off
assert 'entry.FactionKey==key' in off
assert 'TeamSees(members,turret,map)' in off
assert 'new RHFObservedTurretS46(key,turret,now)' in off
assert 'KnownTurrets(board,members[0].Faction,' in off
assert 'IndividuallyConfirmed(soldier,assignment,map)' in off
assert 'IndividuallyConfirmed(soldier,defender,map)' in off
assert 'if(target is Pawn && (role==RHFRoleS41.OffenseHunt &&' in off
assert 'HasInvalidActiveOrders(board,map)' in off
assert 'target.Destroyed || !target.Spawned' in off
assert '!RHFCombatPlannerV125.RequiresBreach(target)' in off
assert 'dist<3600' not in off
assert 'pawn.WorkTagIsDisabled(WorkTags.Violent)' in role
assert 'p.WorkTagIsDisabled(WorkTags.Violent)' in off
assert 'soldier.WorkTagIsDisabled(WorkTags.Violent)' in contact

# Reference behavior for a two-faction coalition and dynamic sightings.
class Memory:
    def __init__(self):self.known={}
    def observe(self,faction,turret):self.known.setdefault(faction,set()).add(turret)
    def recall(self,faction,alive):return self.known.get(faction,set()) & set(alive)
mem=Memory()
mem.observe('coalition-A','turret-1')
assert mem.recall('coalition-A',{'turret-1','turret-2'})=={'turret-1'}
assert mem.recall('coalition-B',{'turret-1'})==set()
assert mem.recall('coalition-A',set())==set()
mem.observe('coalition-B','turret-2')
assert mem.recall('coalition-B',{'turret-2'})=={'turret-2'}

# A last-sighting coordinate can be shared. Live tracking cannot be shared
# after target moves out of view. This test is a reference model only.
last_seen=(18,4); current_hidden=(28,9)
def assign_target(sees):return current_hidden if sees else None
assert assign_target(False) is None
assert last_seen != current_hidden

# The mission pulse must react to destroyed targets and opened doors.
def invalid(role,exists,needs_breach):
    return not exists or (role=='Breach' and not needs_breach)
assert invalid('Breach',True,False)
assert invalid('OffenseCover',False,False)
assert not invalid('OffenseCover',True,False)
print('PASS R014 mission sight memory, faction isolation, replan guards, and nonviolent operators (SOURCE ONLY)')
