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
            internal int Initial, Progress, HP, InitialCount, WaveStart, Waves, RegroupStart;
            internal bool Regrouping;
            internal bool PushBroken;
            internal IntVec3 Shelter, Front;
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
            e.Operation=null;
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
                if(!e.Regrouping)e.FailedSlots.Add(e.Slots[p]);
                Log.Message("[NJR][R038][Firebreak] native suppression/cover: "+p.LabelShortCap+
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
                if(fighters.Contains(p))Log.Message("[NJR][R038][Firebreak] recovered: "+p.LabelShortCap+
                    " native="+(NativeSuppression(p)?.ToString()??"unknown"));
            }
            return recovered;
        }
        private static bool CanFire(Pawn p,RHFOperation op,Thing gun)
        {
            float range=RHFCombatPlannerV125.WeaponRange(p);
            return range>3f && (p.Position-gun.Position).LengthHorizontalSquared<=range*range &&
                GenSight.LineOfSight(p.Position,gun.Position,p.Map) &&
                !RHFRescueFireDisciplineV136.UnsafeLine(op,p,gun);
        }
        private static bool Usable(Map map,Pawn p,IntVec3 c,HashSet<IntVec3> taken)
        {
            if(!c.InBounds(map) || !c.Standable(map) || c.GetEdifice(map)!=null || taken.Contains(c))return false;
            Pawn occupant=c.GetFirstPawn(map);
            return occupant==null || occupant==p;
        }
        private static IntVec3 Position(Pawn p,Map map,Thing gun,IntVec3 front,
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
                if(x*x+z*z>radius*radius || !Usable(map,p,c,taken))continue;
                if(shooting && avoid!=null)
                {
                    bool failedLane=false;
                    foreach(IntVec3 failed in avoid)if((c-failed).LengthHorizontalSquared<=4)
                    {failedLane=true;break;}
                    if(failedLane)continue;
                }
                if(shooting && ((c-gun.Position).LengthHorizontalSquared>range*range ||
                   !GenSight.LineOfSight(c,gun.Position,map)))continue;
                if(safeOnly && RHFObjectiveRouteV128.StagingExposedToTurret(map,c))continue;
                bool straight=RHFObjectiveRouteV128.FormationLegSafe(p.Position,c,map,!safeOnly || leavingFire);
                // A retreat can take a native passable detour around a rock.
                // Prefer the verified direct exit leg; do not apply this to
                // any approach to a shooting position.
                if(!straight && !(leavingFire && safeOnly && !shooting))continue;
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
        private static void Begin(Encounter e,RHFOperation op,Map map,RHFRoutePlanV128 route,
            Thing gun,List<Pawn> fighters,int now)
        {
            if(!e.Active)
            {
                e.Active=true;e.Operation=op;e.Gun=gun;e.Initial=e.Progress=now;e.HP=gun.HitPoints;
                e.InitialCount=fighters.Count;e.Waves=0;e.FailedSlots.Clear();e.PushBroken=false;
            }
            e.Regrouping=false;e.Waves++;e.WaveStart=now;
            e.Slots.Clear();e.Recovering.Clear();e.Shooters.Clear();e.LastCover.Clear();e.PushBroken=false;
            int first=route.FirstTurretExposureIndex;
            int last=route.FirstBarrierIndex>=0?route.FirstBarrierIndex-1:route.Cells.Count-1;
            IntVec3 front=route.Cells[Math.Min(last,Math.Min(route.Cells.Count-1,first+6))];
            IntVec3 shelter=route.Cells[Math.Max(0,first-5)];
            e.Front=front;
            if(e.Waves==1)e.Shelter=shelter;
            else shelter=e.Shelter;
            var used=new HashSet<IntVec3>(e.FailedSlots);
            foreach(Pawn p in fighters)Bind(p,e);
            fighters.Sort((a,b)=>RHFCombatPlannerV125.WeaponRange(b).CompareTo(RHFCombatPlannerV125.WeaponRange(a)));
            // Try actual protected firing lanes first. Do not send the axes
            // through gunfire when the rifles can engage from outside it.
            foreach(Pawn p in fighters)
            {
                if(!DirectGun(p))continue;
                IntVec3 slot=Position(p,map,gun,shelter,used,true,true,false,e.Waves>1?e.FailedSlots:null);
                if(slot.IsValid){e.Slots[p]=slot;used.Add(slot);e.Shooters.Add(p);}
            }
            int directCount=0;foreach(Pawn p in fighters)if(DirectGun(p))directCount++;
            bool protectedFire=e.Shooters.Count>0 && e.Shooters.Count*2>=directCount;
            // Otherwise commit a SINGLE group movement with stable separate
            // endpoints. Do not reissue a lost advance after native cover.
            foreach(Pawn p in fighters)
            {
                if(e.Slots.ContainsKey(p))continue;
                bool gunner=DirectGun(p);
                IntVec3 slot=gunner&&!protectedFire?Position(p,map,gun,front,used,true,false,false,e.Waves>1?e.FailedSlots:null):IntVec3.Invalid;
                if(slot.IsValid)e.Shooters.Add(p);
                else slot=Position(p,map,gun,shelter,used,false,true);
                if(!slot.IsValid)slot=p.Position;
                e.Slots[p]=slot;used.Add(slot);
            }
            Log.Message("[NJR][R038][Firebreak] commit members="+fighters.Count+
                " shootingSlots="+e.Shooters.Count+" protected="+protectedFire+" gun="+gun.Position+
                " wave="+e.Waves);
        }
        internal static bool TryControl(RHFOperation op,Map map,RHFTacticalBoardS41 board,
            RHFRoutePlanV128 route,Thing gun,HashSet<Pawn> reserved,int now)
        {
            Encounter e=Encounters.GetValue(op,_=>new Encounter());
            var fighters=new List<Pawn>();
            foreach(Pawn p in op.Squad)if(Mobile(p,map) && !reserved.Contains(p))fighters.Add(p);
            // Never turn an ordinary safe route or a wall BEFORE the gun
            // into a dedicated turret fight. Pending search is not a clear route.
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
            // An escorted prisoner can be released mid-operation; do not
            // commandeer its assigned escort into the combat wing.
            foreach(Pawn p in reserved)
            {
                e.Slots.Remove(p);e.Shooters.Remove(p);e.Recovering.Remove(p);
                if(Participants.TryGetValue(p,out Encounter owner) && ReferenceEquals(owner,e))Participants.Remove(p);
            }
            Observe(op,map,now);
            if(e.Gun.HitPoints<e.HP){e.HP=e.Gun.HitPoints;e.Progress=now;op.LastProgressTick=now;}
            List<Pawn> recovered=RefreshRecovery(e,fighters,now);
            int guns=0,suppressedGuns=0;
            foreach(Pawn p in fighters)if(DirectGun(p))
            {guns++;if(e.Recovering.Contains(p))suppressedGuns++;}
            int effectiveSlots=0;
            foreach(Pawn p in fighters)if(e.Shooters.Contains(p) && !e.Recovering.Contains(p))effectiveSlots++;
            if(!e.Regrouping && !e.PushBroken && guns>0 &&
               (suppressedGuns*2>=guns || (e.FailedSlots.Count>0 && effectiveSlots*2<guns)))
            {
                e.PushBroken=true;e.Regrouping=true;e.RegroupStart=now;
                var occupied=new HashSet<IntVec3>();
                // CE moves pawns into its own shelter. Never force a suppressed
                // pawn out; allocate a different destination for AFTER recovery.
                foreach(Pawn p in fighters)
                {
                    IntVec3 slot=Position(p,map,e.Gun,e.Shelter,occupied,false,true,true);
                    if(!slot.IsValid)slot=p.Position;
                    e.Slots[p]=slot;occupied.Add(slot);
                }
                Log.Message("[NJR][R038][Firebreak] regroup; suppressed="+suppressedGuns+"/"+guns+
                    " wave="+e.Waves);
            }
            if(e.Regrouping)
            {
                int ready=0;
                var readyCells=new HashSet<IntVec3>();
                foreach(Pawn p in fighters)
                    if(!e.Recovering.Contains(p) && !RHFCombatPlannerV125.IsCombatEmergencyJob(p) &&
                       e.Slots.TryGetValue(p,out IntVec3 slot) && p.Position==slot &&
                       !RHFObjectiveRouteV128.StagingExposedToTurret(map,p.Position) && readyCells.Add(slot))ready++;
                // Only one deliberate renewed assault, after all survivors
                // have recovered and reached distinct, safe rally positions.
                if(e.Waves==1 && ready==fighters.Count && ready>=2 && now-e.RegroupStart>=600 && beforeBarrier)
                {
                    Begin(e,op,map,route,e.Gun,fighters,now);
                    Log.Message("[NJR][R038][Firebreak] recovered coordinated second attempt; shooters="+e.Shooters.Count);
                }
            }
            else if(!e.PushBroken && recovered.Count>0)
            {
                var used=new HashSet<IntVec3>(e.FailedSlots);
                foreach(Pawn p in fighters)if(!recovered.Contains(p) && e.Slots.TryGetValue(p,out IntVec3 old))used.Add(old);
                foreach(Pawn p in recovered)
                {
                    if(!fighters.Contains(p))continue;
                    IntVec3 slot=DirectGun(p)?Position(p,map,e.Gun,e.Shelter,used,true,true,true,e.FailedSlots):IntVec3.Invalid;
                    // No solo return into exposed failed lanes. A safe firing slot
                    // is allowed; otherwise hold at a separate safe position.
                    if(slot.IsValid)e.Shooters.Add(p);
                    else {e.Shooters.Remove(p);slot=Position(p,map,e.Gun,e.Shelter,used,false,true,true);}
                    if(!slot.IsValid)slot=p.Position;
                    e.Slots[p]=slot;used.Add(slot);
                }
            }
            // No loss-count shortcut at one casualty. Do not abandon a fresh
            // coordinated attempt merely because the FIRST wave did no damage.
            // Conversely no infinite series of attacks or safe waits.
            bool noProgress=now-Math.Max(e.Progress,e.WaveStart)>=3000;
            bool regroupStalled=e.Regrouping && now-e.RegroupStart>=2400;
            bool noSecondLine=e.Waves>1 && !e.Regrouping && e.Shooters.Count*2<guns;
            if((fighters.Count*2<=e.InitialCount || guns==0 || regroupStalled || noSecondLine ||
                (!e.Regrouping && noProgress) || now-e.Progress>=7200) && now-e.Initial>=300)
            {
                End(op);board.Barrier=null;board.Breacher=null;
                op.Phase=2;op.PhaseStartTick=now;
                Log.Message("[NJR][R038][Firebreak] abort; mobile="+fighters.Count+" guns="+guns+
                    " suppressed="+suppressedGuns+" noSecondLine="+noSecondLine+" noDamageTicks="+(now-e.Progress));
                board.Decision="R038 failed fire encounter; withdrawal";
                return true;
            }
            board.Threat=e.Gun;board.Barrier=null;board.Breacher=null;
            foreach(Pawn p in fighters)
            {
                if(!e.Slots.TryGetValue(p,out IntVec3 cell)){cell=p.Position;e.Slots[p]=cell;Bind(p,e);}
                bool recovering=e.Recovering.Contains(p);
                if(recovering)cell=p.Position;
                board.Assign(p,recovering?RHFRoleS41.Hold:
                    !e.Regrouping && e.Shooters.Contains(p)?RHFRoleS41.SuppressTurret:RHFRoleS41.Advance,
                    !e.Regrouping && e.Shooters.Contains(p)?e.Gun:null,cell,now);
                Job running=p.jobs?.curJob;
                if(running!=null && !RHFCombatPlannerV125.IsCombatEmergencyJob(p) &&
                   (running.def==JobDefOf.Goto && running.targetA.Cell!=cell ||
                    (running.def==JobDefOf.Wait || running.def==JobDefOf.Wait_Combat) && cell!=p.Position ||
                    running.def==JobDefOf.AttackMelee && running.targetA.Thing==e.Gun &&
                    (p.Position-e.Gun.Position).LengthHorizontalSquared>2))
                    p.jobs.EndCurrentJob(JobCondition.InterruptForced,false);
            }
            board.Decision="R038 turret encounter; regroup="+e.Regrouping+" suppressed="+e.Recovering.Count+" wave="+e.Waves;
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
            // Immediate observed defenders still receive return fire; never
            // choose an independent movement or a distant melee chase here.
            bool regroupMove=e.Regrouping && order.Cell.IsValid && order.Cell!=p.Position;
            bool atSlot=!order.Cell.IsValid || (p.Position-order.Cell).LengthHorizontalSquared<=2;
            // Native emergency jobs retain priority. Otherwise allow return fire
            // from actual cover; do not order an isolated exposed advance.
            if(!regroupMove && DirectGun(p) &&
               (atSlot || e.Regrouping || e.Recovering.Contains(p)) && CanFire(p,op,e.Gun))return JobFor(JobDefOf.AttackStatic,e.Gun,360);
            Job contact=regroupMove || e.Recovering.Contains(p) || !atSlot?null:NJRCombatContactS44.Respond(p,op,p.Position,false,22);
            if(contact!=null && (contact.def==JobDefOf.AttackStatic ||
                contact.def==JobDefOf.AttackMelee && contact.targetA.Thing!=null &&
                (p.Position-contact.targetA.Thing.Position).LengthHorizontalSquared<=2))return contact;
            if(!e.Regrouping && !e.Recovering.Contains(p) && (p.Position-e.Gun.Position).LengthHorizontalSquared<=2 &&
                p.CanReach(e.Gun,PathEndMode.Touch,Danger.Deadly))
                return JobFor(JobDefOf.AttackMelee,e.Gun,180);
            if(e.Recovering.Contains(p))return JobFor(JobDefOf.Wait_Combat,p.Position,60);
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
