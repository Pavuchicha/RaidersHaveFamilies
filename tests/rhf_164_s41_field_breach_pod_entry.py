"""S4.1-A/O game-finding regression tests. Source contracts, NOT runtime/CE proof."""
from pathlib import Path
import xml.etree.ElementTree as ET
R=Path(__file__).resolve().parents[1]
S=R/'RaidersHaveFamilies/Source'
T=(S/'RHFTactical.cs').read_text('utf-8')
ROLE=(S/'RHFRescueRoleJobGiverS41.cs').read_text('utf-8')
OFF=(S/'RHFOffenseS41.cs').read_text('utf-8')
PLANNER=(S/'RHFRescuePlannerS41.cs').read_text('utf-8')
POD=(S/'RHFVipEvacS311.cs').read_text('utf-8')
OPS=(S/'RHFOperations.cs').read_text('utf-8')
ENTRY=(S/'RHFRescueGroundEntryS41.cs').read_text('utf-8')
ROUTE=(S/'RHFObjectiveRouteV128.cs').read_text('utf-8')
NATIVE=(S/'RHFPodPlannerS41.cs').read_text('utf-8')
J=(S/'JobDriver_RHFPlantBreachChargeS41.cs').read_text('utf-8')
X=(R/'RaidersHaveFamilies/Defs/JobDefs/RHF_BreachChargeS41.xml').read_text('utf-8')
K=(R/'RaidersHaveFamilies/Defs/ThingDefs/RHF_BreachKit.xml').read_text('utf-8')
W=(R/'.github/workflows/build.yml').read_text('utf-8')

def verify(t=T,role=ROLE,off=OFF,planner=PLANNER,pod=POD,ops=OPS,entry=ENTRY,route=ROUTE,native=NATIVE,j=J,x=X,k=K,w=W):
    # No mod-supplied kit or scripted destruction of barriers anywhere.
    assert len(ET.fromstring(k))==0 and len(ET.fromstring(x))==0
    assert 'RHF_BreachKit' not in t and 'RHF_PlantBreachChargeS41' not in role
    assert 'GenExplosion.DoExplosion' not in j and 'TakeDamage(' not in j
    assert 'TryBreachCharge(' not in ops
    assert 'GenExplosion.DoExplosion' not in off
    assert 'MeleeWeapon_BreachAxe' in t and 'IssueBreachAxe' in t
    assert 'GenStuff.DefaultStuffFor(weapon)' in t
    assert 'EnsureBreachTeam' in t and 'op.Squad.Count<6' in t
    assert 'RHFTactical.EnsureBreachTeam(op,map)' in planner
    assert 'return Task(JobDefOf.AttackMelee,target,600);' in off
    assert 'return Task(strike,obstacle,900);' in role
    assert 'JobDefOf.AttackMelee' in role
    # Bounded, threat-aware explicit starting edge before vanilla EdgeWalkIn.
    assert 'RHFRescueGroundEntryS41.Choose(map,op)' in ops
    assert 'parms.spawnCenter=safer;' in ops
    assert 'ReachableExterior(map,center,captive)' in entry
    assert 'GenSight.LineOfSight' in entry
    assert 'foreach(Building turret in map.listerBuildings.allBuildingsColonist)' in entry
    assert 'checkedTurrets>200' not in entry and 'checkedDefenders>96' not in entry
    assert 'bool stalled=prior!=null' in route and 'now-prior.Tick>=300 &&' in route
    # Recover a field engineer that can't actually obtain a vanilla WorkGiver job.
    assert 'RHFPodPlannerS41.WorkJob(engineer,currentSite)==null' in pod
    assert 'NativePrepareEngineerS41(available)' in pod
    assert 'RHFPodPlannerS41.WorkJob(available,currentSite)==null' in pod
    assert 'foreach(Pawn available in op.Squad)' in pod
    assert 'if(++checkedPawns>16)break;' not in pod
    assert 'return RHFPodPlannerS41.WorkJob' not in pod  # no fake job completion
    assert 'return result;' in native and 'giver.JobOnThing(pawn,site,true)' in native
    assert 'carrier.inventory.innerContainer.TryAdd(stack,' in pod  # real inventory, not drop piles
    assert 'GenSpawn.Spawn(stack, places[i], map)' not in pod
    assert 'python3 tests/rhf_164_s41_field_breach_pod_entry.py' in w

verify()
changed=[
 ('t','MeleeWeapon_BreachAxe','RHF_BreachKit'),
 ('t','op.Squad.Count<6','op.Squad.Count>6'),
 ('planner','RHFTactical.EnsureBreachTeam(op,map)','// no second sapper'),
 ('off','return Task(JobDefOf.AttackMelee,target,600);','return null;'),
 ('ops','parms.spawnCenter=safer;','// no safer spawn'),
 ('entry','ReachableExterior(map,center,captive)','return false; /* no approach check */'),
 ('entry','GenSight.LineOfSight','true /* bypass LOS */'),
 ('entry','foreach(Building turret in map.listerBuildings.allBuildingsColonist)','// no turret search'),
 ('route','now-prior.Tick>=300','now-prior.Tick>=30000'),
 ('pod','RHFPodPlannerS41.WorkJob(available,currentSite)==null','false'),
 ('pod','foreach(Pawn available in op.Squad)','if(false) { /* no alternate engineer scan */ }'),
 ('pod','carrier.inventory.innerContainer.TryAdd(stack,','// no physical cargo in inventories'),
 ('x','<Defs/>','<Defs><JobDef><defName>RHF_PlantBreachChargeS41</defName></JobDef></Defs>'),
 ('k','<Defs/>','<Defs><ThingDef><defName>RHF_BreachKit</defName></ThingDef></Defs>'),
 ('j','namespace RaidersHaveFamilies {}','GenExplosion.DoExplosion'),
]
orig=dict(t=T,role=ROLE,off=OFF,planner=PLANNER,pod=POD,ops=OPS,entry=ENTRY,route=ROUTE,native=NATIVE,j=J,x=X,k=K,w=W)
for key,old,new in changed:
    assert old in orig[key],(key,old)
    sample=dict(orig);sample[key]=sample[key].replace(old,new)
    try:verify(**sample)
    except (AssertionError,ET.ParseError):continue
    raise AssertionError('escaped mutation '+key+' '+old)
print('PASS: no kit/explosion, physical vanilla axes, incident-specific safe entry, native engineer job rotation, 15 negative mutations; STATIC ONLY')
