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
            internal int Initial, Progress, HP, InitialCount, WaveStart, Waves;
            internal bool Regrouping;
            internal bool PushBroken;
            internal IntVec3 Shelter;
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
            e.Slots.Clear();e.Recovering.Clear();e.Shooters.Clear();
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
        private static bool CoverJob(Job job)
        {
            string n=job?.def?.defName ?? "";
            return n.IndexOf("RunForCover",StringComparison.OrdinalIgnoreCase)>=0 ||
                n.IndexOf("TakeCover",StringComparison.OrdinalIgnoreCase)>=0 ||
                n.IndexOf("SeekCover",StringComparison.OrdinalIgnoreCase)>=0 ||
                n.IndexOf("Suppress",StringComparison.OrdinalIgnoreCase)>=0 ||
                n.IndexOf("Flee",StringComparison.OrdinalIgnoreCase)>=0;
        }
        internal static void Observe(RHFOperation op,Map map,int now)
        {
            if(!IsActive(op))return;
            Encounter e=Encounters.GetValue(op,_=>new Encounter());
            foreach(Pawn p in e.Slots.Keys)
                if(Mobile(p,map) && CoverJob(p.jobs?.curJob))ObserveCover(p,p.jobs.curJob);
        }
        // A CE cover job can start AND finish between 90-tick decisions.
        // Observe its actual native start; do not cancel or modify any job.
        internal static void ObserveCover(Pawn p,Job job)
        {
            if(p==null || !CoverJob(job) || !Participants.TryGetValue(p,out Encounter e) ||
               !e.Active || e.Operation?.Phase!=1 || e.Regrouping || !e.Slots.ContainsKey(p))return;
            if(e.Recovering.Add(p))
            {
                e.FailedSlots.Add(e.Slots[p]);
                int disruptedShooters=0;
                foreach(Pawn shooter in e.Shooters)
                    if(e.Recovering.Contains(shooter))disruptedShooters++;
                // One suppressed soldier stops his own advance, not all fire.
                // Once a majority of the fire wing is disrupted, stop the push.
                if((e.Shooters.Count>0 && disruptedShooters*2>=e.Shooters.Count) ||
                   e.Recovering.Count*2>=e.Slots.Count)e.PushBroken=true;
                Log.Message("[NJR][R034][Firebreak] native cover; no repeat charge: "+p.LabelShortCap);
            }
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
            HashSet<IntVec3> taken,bool shooting,bool safeOnly,bool leavingFire=false)
        {
            var options=new List<IntVec3>();
            var costs=new Dictionary<IntVec3,int>();
            float range=RHFCombatPlannerV125.WeaponRange(p);
            // Bounded local candidate search; only 24 native path queries.
            int radius=shooting?(safeOnly?18:10):5;
            for(int x=-radius;x<=radius;x++)for(int z=-radius;z<=radius;z++)
            {
                IntVec3 c=front+new IntVec3(x,0,z);
                if(x*x+z*z>radius*radius || !Usable(map,p,c,taken))continue;
                if(shooting && ((c-gun.Position).LengthHorizontalSquared>range*range ||
                   !GenSight.LineOfSight(c,gun.Position,map)))continue;
                if(safeOnly && RHFObjectiveRouteV128.StagingExposedToTurret(map,c))continue;
                if(!RHFObjectiveRouteV128.FormationLegSafe(p.Position,c,map,!safeOnly || leavingFire))continue;
                options.Add(c);costs[c]=Score(p,map,front,c,safeOnly);
            }
            options.Sort((a,b)=>costs[a].CompareTo(costs[b]));
            for(int i=0;i<options.Count && i<24;i++)
                if(p.CanReach(options[i],PathEndMode.OnCell,Danger.Deadly))return options[i];
            return IntVec3.Invalid;
        }
        private static int Score(Pawn p,Map map,IntVec3 front,IntVec3 c,bool safeOnly)
        {
            return (p.Position-c).LengthHorizontalSquared+(front-c).LengthHorizontalSquared*3+
                (!safeOnly && RHFObjectiveRouteV128.StagingExposedToTurret(map,c)?250:0);
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
            e.Slots.Clear();e.Recovering.Clear();e.Shooters.Clear();
            int first=route.FirstTurretExposureIndex;
            int last=route.FirstBarrierIndex>=0?route.FirstBarrierIndex-1:route.Cells.Count-1;
            IntVec3 front=route.Cells[Math.Min(last,Math.Min(route.Cells.Count-1,first+6))];
            IntVec3 shelter=route.Cells[Math.Max(0,first-5)];
            if(e.Waves==1)e.Shelter=shelter;
            else shelter=e.Shelter;
            var used=new HashSet<IntVec3>(e.FailedSlots);
            foreach(Pawn p in fighters)Bind(p,e);
            // Try actual protected firing lanes first. Do not send the axes
            // through gunfire when the rifles can engage from outside it.
            foreach(Pawn p in fighters)
            {
                if(!DirectGun(p))continue;
                IntVec3 slot=Position(p,map,gun,shelter,used,true,true);
                if(slot.IsValid){e.Slots[p]=slot;used.Add(slot);e.Shooters.Add(p);}
            }
            bool protectedFire=e.Shooters.Count>0;
            // Otherwise commit a SINGLE group movement with stable separate
            // endpoints. Do not reissue a lost advance after native cover.
            foreach(Pawn p in fighters)
            {
                if(e.Slots.ContainsKey(p))continue;
                bool gunner=DirectGun(p);
                IntVec3 slot=gunner&&!protectedFire?Position(p,map,gun,front,used,true,false):IntVec3.Invalid;
                if(slot.IsValid)e.Shooters.Add(p);
                else slot=Position(p,map,gun,shelter,used,false,true);
                if(!slot.IsValid)slot=p.Position;
                e.Slots[p]=slot;used.Add(slot);
            }
            Log.Message("[NJR][R034][Firebreak] commit members="+fighters.Count+
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
            // A failed attack is a real failed operation, not a license to
            // repeatedly sacrifice the same pawns in a CE cover/advance loop.
            int free=0;
            foreach(Pawn p in fighters)if(!e.Recovering.Contains(p))free++;
            if((fighters.Count*2<=e.InitialCount || now-e.Progress>=(e.PushBroken?1200:3000) ||
                (e.PushBroken && now-e.Progress>=900 && free==0)) && now-e.Initial>=300)
            {
                End(op);board.Barrier=null;board.Breacher=null;
                op.Phase=2;op.PhaseStartTick=now;
                Log.Message("[NJR][R034][Firebreak] abort; mobile="+fighters.Count+" available="+free+
                    " noDamageTicks="+(now-e.Progress));
                board.Decision="R034 failed fire encounter; withdrawal";
                return true;
            }
            // Loss of the fire wing breaks the advance for the entire wing.
            // Do not treat arrival at a shelter as proof CE suppression ended.
            // Hold the positions CE actually reached; return fire remains legal.
            // Never automatically launch another exposed wave at intact guns.
            if(e.PushBroken)
            {
                foreach(Pawn p in fighters)
                    if(e.Slots.ContainsKey(p))e.Slots[p]=p.Position;
            }
            board.Threat=e.Gun;board.Barrier=null;board.Breacher=null;
            foreach(Pawn p in fighters)
            {
                if(!e.Slots.TryGetValue(p,out IntVec3 cell)){cell=p.Position;e.Slots[p]=cell;Bind(p,e);}
                bool recovering=e.PushBroken || e.Recovering.Contains(p);
                if(recovering)cell=p.Position;
                board.Assign(p,recovering?RHFRoleS41.Hold:
                    e.Shooters.Contains(p)?RHFRoleS41.SuppressTurret:RHFRoleS41.Advance,
                    e.Shooters.Contains(p)?e.Gun:null,cell,now);
                Job running=p.jobs?.curJob;
                if(running!=null && !RHFCombatPlannerV125.IsCombatEmergencyJob(p) &&
                   (running.def==JobDefOf.Goto && running.targetA.Cell!=cell ||
                    running.def==JobDefOf.Wait && cell!=p.Position ||
                    running.def==JobDefOf.AttackMelee && running.targetA.Thing==e.Gun &&
                    (p.Position-e.Gun.Position).LengthHorizontalSquared>2))
                    p.jobs.EndCurrentJob(JobCondition.InterruptForced,false);
            }
            board.Decision="R034 committed turret encounter; recovered="+e.Recovering.Count;
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
            if(!regroupMove && DirectGun(p) && CanFire(p,op,e.Gun))return JobFor(JobDefOf.AttackStatic,e.Gun,240);
            Job contact=regroupMove?null:NJRCombatContactS44.Respond(p,op,p.Position,false,22);
            if(contact!=null && (contact.def==JobDefOf.AttackStatic ||
                contact.def==JobDefOf.AttackMelee && contact.targetA.Thing!=null &&
                (p.Position-contact.targetA.Thing.Position).LengthHorizontalSquared<=2))return contact;
            if((p.Position-e.Gun.Position).LengthHorizontalSquared<=2 &&
                p.CanReach(e.Gun,PathEndMode.Touch,Danger.Deadly))
                return JobFor(JobDefOf.AttackMelee,e.Gun,180);
            if(e.PushBroken || e.Recovering.Contains(p))return JobFor(JobDefOf.Wait_Combat,p.Position,60);
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
               name.IndexOf("Flee",StringComparison.OrdinalIgnoreCase)<0)return;
            NJRRescueFirebreakR034.ObserveCover(PawnField.GetValue(__instance) as Pawn,__0);
        }
    }
}
