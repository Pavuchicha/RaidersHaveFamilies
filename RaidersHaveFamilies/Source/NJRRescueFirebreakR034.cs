using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using HarmonyLib;
using System.Reflection;
using Verse;
using Verse.AI;

namespace RaidersHaveFamilies
{
    // A mission-scoped, committed fire encounter. The existing role job giver
    // still owns the jobs. CE keeps its native suppression and medical response.
    internal static class NJRRescueFirebreakR034
    {
        private sealed class Encounter
        {
            internal Thing Gun;
            internal int Initial, Progress, InitialCount, LastGroupMove;
            internal NJRRallyFieldR037 Approach;
            internal readonly Dictionary<Thing,int> TargetHP=new Dictionary<Thing,int>();
            internal readonly Dictionary<Pawn,Thing> FireTargets=new Dictionary<Pawn,Thing>();
            internal readonly Dictionary<Pawn,int> LastMove=new Dictionary<Pawn,int>();
            internal readonly Dictionary<Pawn,int> LastSearch=new Dictionary<Pawn,int>();
            internal readonly Dictionary<Pawn,int> LastPositionChange=new Dictionary<Pawn,int>();
            internal readonly Dictionary<Pawn,IntVec3> LastPosition=new Dictionary<Pawn,IntVec3>();
            internal IntVec3 Shelter;
            internal readonly Dictionary<Pawn,int> LastCover=new Dictionary<Pawn,int>();
            internal RHFOperation Operation;
            internal readonly HashSet<IntVec3> FailedSlots=new HashSet<IntVec3>();
            internal readonly Dictionary<Pawn,IntVec3> Slots=new Dictionary<Pawn,IntVec3>();
            internal readonly HashSet<Pawn> Recovering=new HashSet<Pawn>();
            internal readonly HashSet<Pawn> Shooters=new HashSet<Pawn>();
            internal bool Active;
        }
        private static readonly ConditionalWeakTable<RHFOperation,Encounter> Encounters=
            new ConditionalWeakTable<RHFOperation,Encounter>();
        private static readonly ConditionalWeakTable<Pawn,Encounter> Participants=
            new ConditionalWeakTable<Pawn,Encounter>();
        private static void Bind(Pawn p,Encounter e)
        {Participants.Remove(p);Participants.Add(p,e);}
        internal static bool IsActive(RHFOperation op) => op!=null && op.Phase==1 &&
            Encounters.TryGetValue(op,out Encounter e) && e.Active;
        internal static bool Controls(RHFOperation op,Pawn p) => IsActive(op) &&
            Encounters.TryGetValue(op,out Encounter e) && e.Slots.ContainsKey(p);
        internal static void End(RHFOperation op)
        {
            if(op==null || !Encounters.TryGetValue(op,out Encounter e))return;
            e.Active=false;
            foreach(Pawn member in e.Slots.Keys)
                if(Participants.TryGetValue(member,out Encounter owner) && ReferenceEquals(owner,e))Participants.Remove(member);
            e.Slots.Clear();e.Recovering.Clear();e.Shooters.Clear();e.LastCover.Clear();
            e.FireTargets.Clear();e.TargetHP.Clear();e.LastMove.Clear();e.LastSearch.Clear();e.LastPosition.Clear();e.LastPositionChange.Clear();e.Approach=null;e.Operation=null;
        }
        internal static bool DirectGun(Pawn p)
        {
            string name=p?.equipment?.Primary?.def?.defName ?? "";
            return RHFCombatPlannerV125.WeaponRange(p)>3f &&
                name.IndexOf("Grenade",StringComparison.OrdinalIgnoreCase)<0 &&
                name.IndexOf("Concussion",StringComparison.OrdinalIgnoreCase)<0;
        }
        private static bool Mobile(Pawn p,Map map) => RHFRescuePlannerS41.ActiveForRoleS41(p,map);
        private static bool Threat(Thing t,Map map) => t is Building b && b.Map==map &&
            RHFCombatPlannerV125.IsTurret(b);
        // Read the CE component, without referencing CE at compile time or
        // changing its state. Public isSuppressed is the native decay result.
        private sealed class SuppressionProbe
        {
            internal ThingComp Comp;
            internal FieldInfo Flag;
            internal bool Initialized;
        }
        private static readonly ConditionalWeakTable<Pawn,SuppressionProbe> Probes=
            new ConditionalWeakTable<Pawn,SuppressionProbe>();
        internal static bool? NativeSuppression(Pawn p)
        {
            if(p==null)return null;
            try
            {
                var probe=Probes.GetValue(p,_=>new SuppressionProbe());
                if(!probe.Initialized)
                {
                    probe.Initialized=true;
                    if(p.AllComps!=null)
                    foreach(ThingComp comp in p.AllComps)
                    {
                        if(comp?.GetType().FullName!="CombatExtended.CompSuppressable")continue;
                        probe.Comp=comp;probe.Flag=AccessTools.Field(comp.GetType(),"isSuppressed");break;
                    }
                }
                return probe.Flag?.GetValue(probe.Comp) is bool flag ? (bool?)flag : null;
            }
            catch(Exception){return null;}
        }
        internal static bool RecoveredPosture(Pawn p) => p!=null &&
            Participants.TryGetValue(p,out Encounter e) && e.Active &&
            !e.Recovering.Contains(p) && NativeSuppression(p)==false;
        private static bool CoverJob(Job job)
        {
            string n=job?.def?.defName ?? "";
            return n.IndexOf("RunForCover",StringComparison.OrdinalIgnoreCase)>=0 ||
                n.IndexOf("TakeCover",StringComparison.OrdinalIgnoreCase)>=0 ||
                n.IndexOf("SeekCover",StringComparison.OrdinalIgnoreCase)>=0 ||
                n.IndexOf("Suppress",StringComparison.OrdinalIgnoreCase)>=0 ||
                n.IndexOf("HunkerDown",StringComparison.OrdinalIgnoreCase)>=0 ||
                n.IndexOf("MaintainPosture",StringComparison.OrdinalIgnoreCase)>=0 ||
                n.IndexOf("Flee",StringComparison.OrdinalIgnoreCase)>=0;
        }
        internal static void Observe(RHFOperation op,Map map,int now)
        {
            if(!IsActive(op))return;
            Encounter e=Encounters.GetValue(op,_=>new Encounter());
            foreach(Pawn p in e.Slots.Keys)
                if(Mobile(p,map) && (CoverJob(p.jobs?.curJob) || NativeSuppression(p)==true))
                    MarkCover(p,e,now);
        }
        // Actual job start is observed even when native cover finishes between
        // two planner pulses. Unlike R034, recovery is NOT a permanent latch.
        internal static void ObserveCover(Pawn p,Job job)
        {
            if(p==null || !CoverJob(job) || !Participants.TryGetValue(p,out Encounter e) ||
               !e.Active || e.Operation?.Phase!=1 || !e.Slots.ContainsKey(p))return;
            MarkCover(p,e,Find.TickManager.TicksGame);
        }
        private static void MarkCover(Pawn p,Encounter e,int now)
        {
            e.LastCover[p]=now;
            if(e.Recovering.Add(p))
            {
                if(e.Slots[p].IsValid && e.FailedSlots.Count<64)e.FailedSlots.Add(e.Slots[p]);
                Log.Message("[NJR][R039][Firebreak] native suppression/cover: "+p.LabelShortCap+
                    " native="+(NativeSuppression(p)?.ToString()??"unknown"));
            }
        }
        private static List<Pawn> RefreshRecovery(Encounter e,List<Pawn> fighters,int now)
        {
            var recovered=new List<Pawn>();
            foreach(Pawn p in e.Recovering)
            {
                if(!fighters.Contains(p)){recovered.Add(p);continue;}
                bool? suppressed=NativeSuppression(p);
                if(suppressed==true || CoverJob(p.jobs?.curJob))continue;
                // With CE: only its actual cleared flag plus quiet interval.
                // Without compatible CE data: conservative timed fallback.
                if(e.LastCover.TryGetValue(p,out int tick) && now-tick<(suppressed==false?180:480))continue;
                recovered.Add(p);
            }
            foreach(Pawn p in recovered)
            {
                e.Recovering.Remove(p);e.LastCover.Remove(p);
                // A finished native cover job must not revive the old exposed
                // Goto. A recovered soldier gets a fresh local decision.
                if(e.Slots.ContainsKey(p))e.Slots[p]=IntVec3.Invalid;
                e.LastMove.Remove(p);e.LastSearch.Remove(p);e.FireTargets.Remove(p);
                if(fighters.Contains(p))Log.Message("[NJR][R039][Firebreak] recovered: "+p.LabelShortCap+
                    " native="+(NativeSuppression(p)?.ToString()??"unknown"));
            }
            return recovered;
        }
        // Native verb capability (including CE overrides) is stricter than
        // def range and map LOS. No field writes, no forced projectile.
        private static readonly MethodInfo NativeHit=AccessTools.Method(typeof(Verb),"CanHitTarget",new[]{typeof(LocalTargetInfo)});
        private static readonly PropertyInfo PrimaryVerbProperty=AccessTools.Property(typeof(CompEquippable),"PrimaryVerb");
        private static bool NativeCanFire(Pawn p,Thing target)
        {
            if(NativeHit==null || PrimaryVerbProperty==null)return true;
            try
            {
                CompEquippable eq=p.equipment?.Primary?.GetComp<CompEquippable>();
                if(eq==null)return true;
                if(!(PrimaryVerbProperty.GetValue(eq) is Verb verb))return true;
                return !(NativeHit.Invoke(verb,new object[]{new LocalTargetInfo(target)}) is bool result) || result;
            }
            catch(Exception){return true;}
        }
        private static bool CanFire(Pawn p,RHFOperation op,Thing gun)
        {
            if(gun==null || gun.Destroyed || !gun.Spawned || gun.Map!=p.Map)return false;
            float range=RHFCombatPlannerV125.WeaponRange(p);
            return range>3f && (p.Position-gun.Position).LengthHorizontalSquared<=range*range &&
                GenSight.LineOfSight(p.Position,gun.Position,p.Map) &&
                !RHFRescueFireDisciplineV136.UnsafeLine(op,p,gun) && NativeCanFire(p,gun);
        }
        private static bool Usable(Map map,Pawn p,IntVec3 c,HashSet<IntVec3> taken)
        {
            if(!c.InBounds(map) || !c.Standable(map) || c.GetEdifice(map)!=null || taken.Contains(c))return false;
            Pawn occupant=c.GetFirstPawn(map);
            return occupant==null || occupant==p;
        }
        private static IntVec3 Position(Pawn p,Map map,RHFOperation op,Thing gun,IntVec3 front,
            HashSet<IntVec3> taken,bool shooting,bool safeOnly,bool leavingFire=false,HashSet<IntVec3> avoid=null)
        {
            var options=new List<IntVec3>();
            var costs=new Dictionary<IntVec3,int>();
            float range=RHFCombatPlannerV125.WeaponRange(p);
            // Bounded local candidate search; only 24 native path queries.
            int radius=shooting?18:8;
            for(int x=-radius;x<=radius;x++)for(int z=-radius;z<=radius;z++)
            {
                IntVec3 c=front+new IntVec3(x,0,z);
                if(x*x+z*z>radius*radius || (c-p.Position).LengthHorizontalSquared>324 || !Usable(map,p,c,taken))continue;
                if(shooting && avoid!=null)
                {
                    bool failedLane=false;
                    foreach(IntVec3 failed in avoid)if((c-failed).LengthHorizontalSquared<=4)
                    {failedLane=true;break;}
                    if(failedLane)continue;
                }
                if(shooting && (c==p.Position || (c-gun.Position).LengthHorizontalSquared>range*range ||
                   !GenSight.LineOfSight(c,gun.Position,map) ||
                   RHFRescueFireDisciplineV136.UnsafeLineFrom(op,p,gun,c)))continue;
                if(safeOnly && RHFObjectiveRouteV128.StagingExposedToTurret(map,c))continue;
                bool straight=RHFObjectiveRouteV128.FormationLegSafe(p.Position,c,map,!safeOnly ||
                    (leavingFire && RHFObjectiveRouteV128.StagingExposedToTurret(map,p.Position)));
                // Short physical leg only; never send a safe shooter across a
                // firing lane merely because its destination is safe.
                if(!straight)continue;
                options.Add(c);costs[c]=Score(p,map,gun,front,c,safeOnly,shooting,range,taken)+(straight?0:2000);
            }
            options.Sort((a,b)=>costs[a].CompareTo(costs[b]));
            for(int i=0;i<options.Count && i<24;i++)
                if(p.CanReach(options[i],PathEndMode.OnCell,Danger.Deadly))return options[i];
            return IntVec3.Invalid;
        }
        private static int Score(Pawn p,Map map,Thing gun,IntVec3 front,IntVec3 c,
            bool safeOnly,bool shooting,float range,HashSet<IntVec3> taken)
        {
            int score=(p.Position-c).LengthHorizontalSquared+(front-c).LengthHorizontalSquared*(shooting?1:2);
            if(!safeOnly && RHFObjectiveRouteV128.StagingExposedToTurret(map,c))score+=250;
            // Avoid a line of shooters all pointing through one another. Keep
            // wider individual slots, at a useful fraction of weapon range.
            foreach(IntVec3 other in taken)
            {
                int d=(other-c).LengthHorizontalSquared;
                if(d<16)score+=(16-d)*(shooting?80:30);
                if(shooting)
                {
                    long dx=gun.Position.x-c.x,dz=gun.Position.z-c.z;
                    long px=other.x-c.x,pz=other.z-c.z,len=dx*dx+dz*dz;
                    long dot=px*dx+pz*dz,cross=px*dz-pz*dx;
                    if(dot>0 && dot<len && cross*cross<=4L*len)score+=1500;
                    dx=gun.Position.x-other.x;dz=gun.Position.z-other.z;
                    px=c.x-other.x;pz=c.z-other.z;len=dx*dx+dz*dz;
                    dot=px*dx+pz*dz;cross=px*dz-pz*dx;
                    if(dot>0 && dot<len && cross*cross<=4L*len)score+=1500;
                }
            }
            if(shooting)
            {
                float distance=(float)Math.Sqrt((gun.Position-c).LengthHorizontalSquared);
                float close=Math.Max(0f,range*0.8f-distance);
                score+=(int)(close*close*20f);
                // Adjacent solid cover is a placement preference, not a CE
                // ballistic or hit-probability claim.
                bool cover=false;
                foreach(IntVec3 delta in GenAdj.CardinalDirections)
                {
                    IntVec3 n=c+delta;
                    if(n.InBounds(map) && n.GetEdifice(map)!=null && !n.Standable(map))
                    {cover=true;break;}
                }
                if(!cover)score+=180;
            }
            return score;
        }
        private static bool Local(Pawn p,Encounter e) => (p.Position-e.Shelter).LengthHorizontalSquared<=576;
        private static bool Ready(Pawn p,Encounter e) => !e.Recovering.Contains(p) &&
            NativeSuppression(p)!=true && !RHFCombatPlannerV125.IsCombatEmergencyJob(p);
        private static Thing FireTarget(Pawn p,RHFOperation op,Encounter e)
        {
            // A distant straggler can fight the actual gun in front of him.
            // Scan ALL viable targets, not just the nearest unusable one.
            if(!DirectGun(p) || p.WorkTagIsDisabled(WorkTags.Violent))return null;
            Thing best=null;float score=float.MaxValue;
            Thing incoming=LastIncoming(p);
            foreach(Building turret in p.Map.listerBuildings.allBuildingsColonist)
            {
                if(!Threat(turret,p.Map) || !CanFire(p,op,turret))continue;
                float value=(p.Position-turret.Position).LengthHorizontalSquared;
                if(turret==e.Gun)value-=150;
                if(turret==incoming)value-=300;
                if(value<score){best=turret;score=value;}
            }
            foreach(Pawn defender in p.Map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            {
                if(defender==null || defender.Dead || defender.Downed || !defender.Spawned ||
                   !CanFire(p,op,defender))continue;
                float value=(p.Position-defender.Position).LengthHorizontalSquared-200;
                if(defender.jobs?.curJob?.targetA.Thing==p)value-=300;
                if(value<score){best=defender;score=value;}
            }
            return best;
        }
        private sealed class Incoming {internal Thing Gun;internal int Tick;}
        private static readonly ConditionalWeakTable<Pawn,Incoming> IncomingGuns=
            new ConditionalWeakTable<Pawn,Incoming>();
        internal static void ObserveIncoming(Pawn p,Thing gun,int now)
        {if(p==null || gun==null)return;var i=IncomingGuns.GetValue(p,_=>new Incoming());i.Gun=gun;i.Tick=now;}
        private static Thing LastIncoming(Pawn p) => IncomingGuns.TryGetValue(p,out Incoming i) &&
            Find.TickManager.TicksGame-i.Tick<600 && Threat(i.Gun,p.Map)?i.Gun:null;
        private static void TrackTarget(Encounter e,Thing gun)
        {if(gun is Building && !e.TargetHP.ContainsKey(gun))e.TargetHP[gun]=gun.HitPoints;}
        private static void Begin(Encounter e,RHFOperation op,Map map,RHFRoutePlanV128 route,
            Thing gun,List<Pawn> fighters,int now)
        {
            e.Active=true;e.Operation=op;e.Gun=gun;e.Initial=e.Progress=now;
            e.InitialCount=fighters.Count;e.LastGroupMove=now-600;e.FailedSlots.Clear();
            int first=route.FirstTurretExposureIndex;
            IntVec3 gathered=NJRRescueRallyR033.Anchor(op);
            e.Shelter=gathered.IsValid?gathered:route.Cells[Math.Max(0,first-5)];
            TrackTarget(e,gun);
            foreach(Pawn p in fighters){Bind(p,e);e.Slots[p]=IntVec3.Invalid;}
            Log.Message("[NJR][R039][Firebreak] begin local combat; members="+fighters.Count+" shelter="+e.Shelter+" gun="+gun.Position);
        }
        internal static bool TryControl(RHFOperation op,Map map,RHFTacticalBoardS41 board,
            RHFRoutePlanV128 route,Thing gun,HashSet<Pawn> reserved,int now)
        {
            Encounter e=Encounters.GetValue(op,_=>new Encounter());
            var fighters=new List<Pawn>();
            foreach(Pawn p in op.Squad)if(Mobile(p,map) && !reserved.Contains(p))fighters.Add(p);
            bool beforeBarrier=route!=null && route.Valid && route.FirstTurretExposureIndex>=0 &&
                (route.FirstBarrierIndex<0 || route.FirstTurretExposureIndex<route.FirstBarrierIndex);
            if(e.Active && (!Threat(e.Gun,map) || (route!=null && route.Valid && !beforeBarrier)))
            {
                End(op);
                foreach(Pawn p in fighters)
                    if(p.jobs?.curJob?.def==JobDefOf.Goto && !RHFCombatPlannerV125.IsCombatEmergencyJob(p))
                        p.jobs.EndCurrentJob(JobCondition.InterruptForced,false);
                return false;
            }
            if(!e.Active)
            {
                if(!beforeBarrier || !Threat(gun,map) || fighters.Count==0)return false;
                Begin(e,op,map,route,gun,fighters,now);
            }
            var removed=new List<Pawn>();
            foreach(Pawn p in e.Slots.Keys)if(!fighters.Contains(p))removed.Add(p);
            foreach(Pawn p in removed)
            {
                e.Slots.Remove(p);e.Shooters.Remove(p);e.Recovering.Remove(p);e.FireTargets.Remove(p);
                if(Participants.TryGetValue(p,out Encounter owner) && ReferenceEquals(owner,e))Participants.Remove(p);
            }
            foreach(Pawn p in fighters)if(!e.Slots.ContainsKey(p)){Bind(p,e);e.Slots[p]=IntVec3.Invalid;}
            Observe(op,map,now);
            RefreshRecovery(e,fighters,now);
            // Damage to any engaged turret is progress, not only to the first
            // turret on the strategic route. Native damage remains untouched.
            var tracked=new List<Thing>(e.TargetHP.Keys);
            foreach(Thing target in tracked)
            {
                int hp=target.Destroyed?0:target.HitPoints;
                if(hp<e.TargetHP[target]){e.Progress=now;op.LastProgressTick=now;}
                e.TargetHP[target]=hp;
                if(target.Destroyed)e.TargetHP.Remove(target);
            }
            int guns=0,localGuns=0,localReady=0,localFire=0;
            var occupied=new HashSet<IntVec3>();
            var candidates=new List<Pawn>();
            // First preserve actual viable firing positions. No forced retreat
            // because a neighbour is suppressed or a distant member is absent.
            foreach(Pawn p in fighters)
            {
                if(DirectGun(p))guns++;
                if(DirectGun(p) && Local(p,e))localGuns++;
                if(!e.LastPosition.TryGetValue(p,out IntVec3 last) || last!=p.Position)
                {e.LastPosition[p]=p.Position;e.LastPositionChange[p]=now;}
                if(!Ready(p,e)){occupied.Add(p.Position);continue;}
                if(DirectGun(p) && Local(p,e))localReady++;
                Thing target=FireTarget(p,op,e);
                if(target!=null)
                {
                    e.FireTargets[p]=target;TrackTarget(e,target);e.Slots[p]=p.Position;
                    e.Shooters.Add(p);occupied.Add(p.Position);
                    if(Local(p,e))localFire++;
                }
                else
                {
                    e.FireTargets.Remove(p);e.Shooters.Remove(p);candidates.Add(p);
                }
            }
            // Keep short, still-progressing orders. Safe and exposed movement
            // cannot share a destination or steal a current firing position.
            var allocate=new List<Pawn>();
            foreach(Pawn p in candidates)
            {
                IntVec3 slot=e.Slots[p];
                if(slot.IsValid && slot!=p.Position && !occupied.Contains(slot) &&
                   Usable(map,p,slot,occupied) && now-e.LastPositionChange[p]<480 &&
                   e.LastMove.TryGetValue(p,out int start) && now-start<1200)
                {occupied.Add(slot);continue;}
                e.Slots[p]=IntVec3.Invalid;
                if(e.LastSearch.TryGetValue(p,out int searched) && now-searched<180)continue;
                e.LastSearch[p]=now;allocate.Add(p);
            }
            // Laggards get a separate SAFE approach field, not an unbounded
            // Goto across the firing perimeter. No quorum dependency on them.
            var distant=new List<Pawn>();
            foreach(Pawn p in allocate)if(!Local(p,e))distant.Add(p);
            if(distant.Count>0)
            {
                if(e.Approach==null || (now-e.Approach.Created>=1800 && !e.Approach.Pending))
                    e.Approach=new NJRRallyFieldR037(map,e.Shelter,now);
                e.Approach.ExpandFor(distant);
                foreach(Pawn p in distant)
                {
                    IntVec3 step=e.Approach.Waypoint(p,occupied);
                    if(step.IsValid){e.Slots[p]=step;e.LastMove[p]=now;occupied.Add(step);}
                }
            }
            var maneuver=new List<Pawn>();
            allocate.Sort((a,b)=>RHFCombatPlannerV125.WeaponRange(b).CompareTo(RHFCombatPlannerV125.WeaponRange(a)));
            foreach(Pawn p in allocate)
            {
                if(!Local(p,e))continue;
                if(DirectGun(p))
                {
                    // Prefer local protected lines; a recovered shooter need
                    // not leave useful cover to rejoin a central rally point.
                    Thing target=LastIncoming(p)??e.Gun;
                    IntVec3 slot=Position(p,map,op,target,p.Position,occupied,true,true,true,e.FailedSlots);
                    if(slot.IsValid && slot!=p.Position)
                    {e.Slots[p]=slot;e.LastMove[p]=now;occupied.Add(slot);continue;}
                    maneuver.Add(p);
                }
                else
                {
                    IntVec3 slot=Position(p,map,op,e.Gun,e.Shelter,occupied,false,true,true);
                    if(slot.IsValid){e.Slots[p]=slot;e.LastMove[p]=now;occupied.Add(slot);}
                }
            }
            // Exposed movement is a joint local manoeuvre. A covering shooter
            // can support a single mover; without cover fire at least two
            // recovered local shooters must have usable destinations.
            bool canMove=localReady>=2 && localReady*2>=localGuns && now-e.LastGroupMove>=600;
            if(canMove && maneuver.Count>0)
            {
                e.LastGroupMove=now;
                var proposed=new Dictionary<Pawn,IntVec3>();
                foreach(Pawn p in maneuver)
                {
                    Thing target=LastIncoming(p)??e.Gun;
                    IntVec3 slot=Position(p,map,op,target,p.Position,occupied,true,false,false,e.FailedSlots);
                    if(slot.IsValid && slot!=p.Position){proposed[p]=slot;occupied.Add(slot);}
                }
                if(proposed.Count>=2 || (proposed.Count>0 && localFire>0))
                {
                    foreach(var pair in proposed){e.Slots[pair.Key]=pair.Value;e.LastMove[pair.Key]=now;}
                    e.LastGroupMove=now;
                    Log.Message("[NJR][R039][Firebreak] local maneuver movers="+proposed.Count+" covering="+localFire+" localReady="+localReady+" distant="+distant.Count);
                }
            }
            if((fighters.Count*2<=e.InitialCount || guns==0 || now-e.Progress>=7200) && now-e.Initial>=300)
            {
                End(op);board.Barrier=null;board.Breacher=null;op.Phase=2;op.PhaseStartTick=now;
                Log.Message("[NJR][R039][Firebreak] abort; mobile="+fighters.Count+" guns="+guns+" noDamageTicks="+(now-e.Progress));
                board.Decision="R039 failed local engagement; withdrawal";return true;
            }
            board.Threat=e.Gun;board.Barrier=null;board.Breacher=null;
            foreach(Pawn p in fighters)
            {
                bool recovering=!Ready(p,e);
                Thing target=null;if(!recovering)e.FireTargets.TryGetValue(p,out target);
                IntVec3 cell=recovering || !e.Slots[p].IsValid?p.Position:e.Slots[p];
                board.Assign(p,recovering?RHFRoleS41.Hold:target!=null?RHFRoleS41.SuppressTurret:RHFRoleS41.Advance,target,cell,now);
                Job running=p.jobs?.curJob;
                if(running!=null && !recovering &&
                   (running.def==JobDefOf.Goto && (target!=null || running.targetA.Cell!=cell) ||
                    (running.def==JobDefOf.Wait || running.def==JobDefOf.Wait_Combat) && (target!=null || cell!=p.Position) ||
                    running.def==JobDefOf.AttackStatic && target==null && cell!=p.Position &&
                    (running.targetA.Thing==null || !CanFire(p,op,running.targetA.Thing))))
                    p.jobs.EndCurrentJob(JobCondition.InterruptForced,false);
            }
            board.Decision="R039 local engagement; firing="+localFire+" ready="+localReady+" localGuns="+localGuns+" recovering="+e.Recovering.Count;
            return true;
        }
        private static Job JobFor(JobDef def,LocalTargetInfo target,int expiry)
        {
            Job j=JobMaker.MakeJob(def,target);j.expiryInterval=expiry;
            j.checkOverrideOnExpire=true;return j;
        }
        internal static Job CombatJob(RHFOperation op,Pawn p,RHFOrderS41 order)
        {
            Encounter e=Encounters.GetValue(op,_=>new Encounter());
            if(!Threat(e.Gun,p.Map))return JobFor(JobDefOf.Wait,p.Position,30);
            // Recheck actual firing opportunity regardless of distance to
            // assigned slot. Do not cancel native emergency / reload jobs.
            if(e.Recovering.Contains(p) || NativeSuppression(p)==true)
                return JobFor(JobDefOf.Wait_Combat,p.Position,60);
            Thing target=FireTarget(p,op,e);
            if(target!=null){TrackTarget(e,target);return JobFor(JobDefOf.AttackStatic,target,360);}
            if((p.Position-e.Gun.Position).LengthHorizontalSquared<=2 &&
                p.CanReach(e.Gun,PathEndMode.Touch,Danger.Deadly))
                return JobFor(JobDefOf.AttackMelee,e.Gun,180);
            IntVec3 slot=order.Cell;
            if(slot.IsValid && slot!=p.Position && p.CanReach(slot,PathEndMode.OnCell,Danger.Deadly))
                return JobFor(JobDefOf.Goto,slot,600);
            return JobFor(JobDefOf.Wait_Combat,p.Position,60);
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker),"StartJob")]
    internal static class NJRFirebreakCoverObserverR034
    {
        private static readonly FieldInfo PawnField=AccessTools.Field(typeof(Pawn_JobTracker),"pawn");
        private static void Postfix(Pawn_JobTracker __instance,Job __0)
        {
            // Native start observation only. No global alteration of CE jobs.
            if(__0==null || PawnField==null || !ReferenceEquals(__instance.curJob,__0))return;
            string name=__0.def?.defName ?? "";
            if(name.IndexOf("Cover",StringComparison.OrdinalIgnoreCase)<0 &&
               name.IndexOf("Suppress",StringComparison.OrdinalIgnoreCase)<0 &&
               name.IndexOf("Flee",StringComparison.OrdinalIgnoreCase)<0 &&
               name.IndexOf("HunkerDown",StringComparison.OrdinalIgnoreCase)<0 &&
               name.IndexOf("MaintainPosture",StringComparison.OrdinalIgnoreCase)<0)return;
            NJRRescueFirebreakR034.ObserveCover(PawnField.GetValue(__instance) as Pawn,__0);
        }
    }
}
