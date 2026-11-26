"""R047 actual R046 trace plus model/source contracts; no native runtime claim."""
import json
from pathlib import Path
r=Path(__file__).resolve().parents[1];s=r/'RaidersHaveFamilies/Source'
a=json.loads((r/'tests/fixtures/r047_runtime.json').read_text())
assert a['revision']=='R046' and a['events']==14436
assert len(a['hits'])==11 and {x[1] for x in a['hits']}=={36825,36748,36834}
assert sum('no geometric seal yet' in m for _,m in a['logs'])==8
assert any('Real hostile world transport contains the VIP' in m for _,m in a['logs'])
assert not a.get('door_hits')
assert a['boards'][-1][1:4]==[True,0,None]
assert all(p[1]==[121,154] and p[2]=='Wait' for p in a['snapshots'][-1][1])
assert len([row for row in a['snapshots'] if all(p[1]==[121,154] for p in row[1])])>=55
v=(s/'RHFVipEvacS311.cs').read_text();p=(s/'RHFPodPlannerS41.cs').read_text();d=(s/'NJRPrisonerDefenseR047.cs').read_text();ac=(s/'NJRRescueAccessR040.cs').read_text();rp=(s/'RHFRescuePlannerS41.cs').read_text();j=(s/'RHFRescueRoleJobGiverS41.cs').read_text();driver=(s/'JobDriver_RHFPodCargoS41.cs').read_text()
# A prospective pocket can be accepted while the VIP is outside; a real wall cannot.
assert '!seen.Contains(vip.Position) && !vip.CanReach(stand' in v
assert '!pocket.Contains(vip.Position)' in v and v.index('!pocket.Contains(vip.Position)')<v.index('Blueprint_Build plan=GenConstruct.PlaceBlueprintForBuild')
assert 'vipOrder.Cell==assembly' in p and 'sealPocket.Contains(stand)' in p
assert 'op.FortifyPrison?0:20' in j and 'planned.Cell!=vip.Position' in v
# Actual observed hit, local response only, no arbitrary prisoner chase and no VIP combat.
assert 'DamageInfo __0' in d and '__0.Instigator is Pawn attacker' in d and 'c.Attacker==attacker' in d
assert 'op.Freed?.Contains(pawn)!=true' in d and 'LengthHorizontalSquared<=36' in d and 'c.Tick<=360' in d and 'c.Responders.Count<2' in d
assert ac.index('NJRPrisonerDefenseR047.MayDefend')<ac.index('IntVec3 away=DisengageR042')
assert 'JobDefOf.AttackMelee' in d and 'job.expiryInterval=90' in d
# Physical exit access; no shared non-exit fallback; real debris moves after launch.
assert 'AccessSlotR047' in ac and 'priorOrder?.Objective==barrier' in ac
assert 'fallbackSlotsR047' in rp and 'TryExitDebrisR047' in rp
assert 'corridor.BarrierAt(corridor.Exit)' in ac and 'TryMove(op,map,chunk,null,null,null' in ac
assert 'op.Phase==2 && op.PodLaunchCompleted' in driver
# simple state model tests for the actual transition, not pathfinder emulation
pocket={(121,151),(122,151),(123,152)};vip=(118,152);builder=(121,151);refueler=(122,151)
def seal_ready(v,b,f,cargo_clear):return v in pocket and b in pocket and f in pocket and cargo_clear
assert not seal_ready(vip,builder,refueler,True)
assert not seal_ready((123,152),builder,refueler,False)
assert seal_ready((123,152),builder,refueler,True)
print('PASS R047: real trace, prospective seal + physical staging, bounded defence and post-launch recovery (source/model)')
# Replay the local recorded geometry, omitting new debris for this geometric
# proof only. Both possible origins of a 1x2 launcher are considered.
from collections import deque
g=a['geometry'];walk=set(map(tuple,g['walk']));old=tuple(g['vip']);wall=tuple(g['candidate']);stand=tuple(g['boarding']);pad=tuple(g['pod']);base=tuple(g['launcher'])
def flood(c,cells):
    if c not in cells:return set()
    seen={c};queue=deque([c])
    while queue:
        x,y=queue.popleft()
        for n in ((x+1,y),(x-1,y),(x,y+1),(x,y-1)):
            if n in cells and n not in seen:seen.add(n);queue.append(n)
    return seen
for zshift in (-1,1):
    free=walk-{pad,base,(base[0],base[1]+zshift)}
    assert stand in flood(old,free), 'VIP can stage before seal in geometry model'
    prospective=flood(stand,free-{wall})
    assert old not in prospective and stand in prospective
    assert len(prospective)>=10
print('R046 recorded geometry: proposed wall can separate old VIP position from boarding side; move VIP first (ignores live debris, native reservations, CE).')
