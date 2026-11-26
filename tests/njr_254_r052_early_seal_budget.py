"""R052: recorded R051 ordering and procurement model; source contracts, not C# or CE runtime."""
import json, math
from pathlib import Path
r=Path(__file__).resolve().parents[1];s=r/'RaidersHaveFamilies/Source'
a=json.loads((r/'tests/fixtures/r052_runtime.json').read_text())
logs=a['logs']; assert a['revision']=='R051'
find=lambda text:[(t,m) for t,m in logs if text in m]
assert find('PodSite')[0][0]==41235 and 'tier=0' in find('PodSite')[0][1]
assert find('Freed=')[0][0]==41325
assert [t for t,m in find('Real enemy wall blueprint')]==[41505,43485]
assert [t for t,m in find('completed Wall')]==[42135,43755]
assert [t for t,m in find('physically reopened own seal')]==[50543,50899]
# The old rank-then-filter rejected the chamber winner and hid a valid alternative.
pads=[('prison',0,10,True),('side room',1,30,True),('unreachable',1,5,False)]
assert min(pads,key=lambda p:(p[1],p[2]))[0]=='prison'
assert min((p for p in pads if p[0]!='prison' and p[3]),key=lambda p:(p[1],p[2]))[0]=='side room'
# Synthetic native adjusted recipes: high-volume modded material must be charged at its real count.
# material, units, price, unit mass, hp, work
materials=[('wood',5,1,.4,195,100),('steel',5,2,.5,300,135),('mod_light',5,4,.2,700,120),('mod_dense',50,4,1,2000,100)]
def plan(points,available):
 limit=min(points*.1,available);mass_limit=max(18,min(42,18+points*.002))
 for count in range(min(8,2+int(points/1500)),1,-1):
  legal=[]
  for name,units,price,mass,hp,work in materials:
   cost=count*units*price;weight=count*units*mass
   if weight>mass_limit:continue
   score=hp/(max(1,work)**.35*max(1,cost)**.25*max(1,weight)**.15)
   legal.append((score,name,count,cost,units))
  affordable=[m for m in legal if m[3]<=limit]
  if affordable:return max(affordable)
  if count==2 and legal:return min(legal,key=lambda m:m[3])
assert plan(500,1)[1:]==('wood',2,10,5)
assert plan(1500,300)[1]=='mod_light' and plan(1500,300)[2]==3
assert plan(9000,2000)[1]=='mod_light' and plan(9000,2000)[2]==8
assert plan(9000,2000)[3]==160
b=(s/'NJRSealBudgetR052.cs').read_text();o=(s/'RHFOperations.cs').read_text();v=(s/'RHFVipEvacS311.cs').read_text();ac=(s/'NJRRescueAccessR040.cs').read_text();p=(s/'RHFRescuePlannerS41.cs').read_text()
assert 'CostListAdjusted(stuff,errorOnNullStuff:false)' in b and 'DefDatabase<ThingDef>.AllDefsListForReading' in b
assert 'cost+=op.SealCostR052' in o and 'effectivePoints-=cost' in o and 'Scribe_Defs.Look(ref SealStuffR052' in o
assert 'CostListAdjusted(op.SealStuffR052,errorOnNullStuff:false)' in v and 'issuedUnits!=op.SealUnitsR052' in v
assert 'op.BarricadesBuilt>=op.SealLimitR052' in v and 'owner,op.SealStuffR052??ThingDefOf.Steel' in v
assert ac.index('earlyEngineer!=null && room==vip.Position.GetRoom(map)')<ac.index('tier>tierBest')
assert 'earlyEngineer!=null)){noLane++;continue;}' in ac and 'now-op.EarlyPadProbeTickR052<150' in v
assert 'NativeAtSiteS311(map,cell,requested,op.PodCargoOwnerFactionS311)' in v
assert 'NJREarlyWingR053.Reserve(op,map,captive,escortedDuringAssault,now)' in p and 'contacts.Count==0?1' in (s/'NJREarlyWingR053.cs').read_text()
assert 'AssignEarlyR052' in v and 'EndCurrentJob(JobCondition.InterruptForced,false)' in v
print('PASS R052: recorded timing, rank-before-filter regression, synthetic budget/material model and source contracts')
