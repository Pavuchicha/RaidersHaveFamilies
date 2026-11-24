// NJR R017: A rescue is a tactical operation, not a one-pawn native raid.
// Resolve a mission-sized, faction-relative budget BEFORE the vanilla incident,
// then complete a partially generated GROUND rescue wing inside its native Lord.
// All work happens on incident dispatch; there are no new per-pawn tick scans.
using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace RaidersHaveFamilies
{
    internal static class NJRForceStrengthR017
    {
        internal static int RequiredMembers(RHFStrikeParty party)
        {
            if(party==null)return 0;
            switch((RHFStrikeRole)party.Role)
            {
                case RHFStrikeRole.Rescue: return 5;
                case RHFStrikeRole.Diversion: return 3;
                case RHFStrikeRole.Vengeance: return 5;
                default: return 0;
            }
        }

        // Only actual COMBAT pawn-group options, never a faction's traders,
        // caravaneers, animals or a world-wide list of generic human kinds.
        // The lowest regular combat kind is a conservative reference for a
        // squad-sized floor, while native RimWorld may still buy elite pawns.
        private static readonly Dictionary<FactionDef,List<PawnKindDef>> combatKinds =
            new Dictionary<FactionDef,List<PawnKindDef>>();

        private static List<PawnKindDef> CombatKinds(Faction faction)
        {
            if(faction?.def==null)return new List<PawnKindDef>(0);
            if(combatKinds.TryGetValue(faction.def,out List<PawnKindDef> known))
                return known;
            var result=new List<PawnKindDef>();
            if(faction.def.pawnGroupMakers!=null)
                foreach(PawnGroupMaker maker in faction.def.pawnGroupMakers)
                {
                    if(maker?.kindDef!=PawnGroupKindDefOf.Combat || maker.options==null)continue;
                    foreach(PawnGenOption option in maker.options)
                    {
                        PawnKindDef kind=option?.kind;
                        if(kind?.race?.race?.Humanlike!=true || kind.combatPower<=0f ||
                           result.Contains(kind))continue;
                        result.Add(kind);
                    }
                }
            if(result.Count==0)
            {
                PawnKindDef basic=faction.def.basicMemberKind;
                if(basic?.race?.race?.Humanlike==true && basic.combatPower>0f)
                    result.Add(basic);
            }
            result.Sort((a,b)=>a.combatPower.CompareTo(b.combatPower));
            combatKinds[faction.def]=result;
            return result;
        }

        private static float RegularCombatCost(Faction faction)
        {
            List<PawnKindDef> kinds=CombatKinds(faction);
            if(kinds.Count==0)return 100f;
            // The lowest faction-native combatant can fill ordinary cover,
            // escort and breacher positions without inventing a new pawn kind.
            return Mathf.Max(35f,kinds[0].combatPower);
        }

        internal static float HumanCoreFloor(RHFStrikeParty party)
        {
            int count=RequiredMembers(party);
            return count==0?0f:count*RegularCombatCost(party.Faction);
        }

        internal static void CommitMissionBudget(RHFOperation op,RHFStrikeParty party)
        {
            if(op==null || party?.Faction==null || party.Attempted)return;
            int minimum=RequiredMembers(party);
            if(minimum==0)return;
            float soldierCost=RegularCombatCost(party.Faction);
            // Separate mission floor, not a hidden +300 after the equipment
            // deduction. The extra factor accounts for a native leader/elite
            // choice and the explicitly paid 12% supplies / 23% loadouts.
            float wanted=Mathf.Min(15000f,minimum*soldierCost*1.65f+140f);
            if(wanted<=party.Points)return;
            float extra=wanted-party.Points;
            party.Points=wanted;
            op.ThreatPoints+=extra;
            Log.Message("[NJR][ForceR017] Committed "+minimum+"-member "+
                (RHFStrikeRole)party.Role+" wing of "+party.Faction.Name+
                "; native combat cost="+soldierCost.ToString("F0")+
                "; additional allocated points="+extra.ToString("F0")+
                "; final party budget="+party.Points.ToString("F0"));
        }

        private static bool QualifiedPodEngineer(Pawn pawn,Faction faction)
        {
            return pawn!=null && pawn.Faction==faction &&
                pawn.RaceProps?.Humanlike==true && !pawn.Dead &&
                pawn.health?.capacities?.CapableOf(PawnCapacityDefOf.Manipulation)==true &&
                !pawn.WorkTypeIsDisabled(WorkTypeDefOf.Construction);
        }

        // Ground rescue: retain at least one non-shooter for breaching where
        // present, but aim for 75% native direct-fire weapons. Generate native
        // candidates (including CE ammo) before arrival, never rearm by hand.
        // Replace only non-ranged slots, within the already committed budget.
        private static void BalanceGroundRescue(RHFOperation op,RHFStrikeParty party,List<Pawn> pawns)
        {
            if(party.Drop || party.Role!=(int)RHFStrikeRole.Rescue ||
               NJRForceCompositionS42.LowTech(party.Faction) ||
               RHFMod.Settings?.RoleEquipment!=true)return;
            int humans=0, guns=0;
            float spent=0f;
            foreach(Pawn pawn in pawns)
                if(pawn?.RaceProps?.Humanlike==true && pawn.Faction==party.Faction)
                { humans++; spent+=Mathf.Max(0f,pawn.kindDef?.combatPower??0f); if(NJRRescueFirebreakR034.DirectGun(pawn))guns++; }
            int desired=Math.Min(humans,Math.Max(0,(humans*3+3)/4));
            float remaining=Mathf.Max(0f,party.Points-party.SupplyBudget-party.AnimalBudgetS42-
                party.LoadoutBudgetS42-spent);
            List<PawnKindDef> kinds=CombatKinds(party.Faction);
            int attempts=0;
            for(int i=0;i<pawns.Count && guns<desired && attempts<24;i++)
            {
                Pawn original=pawns[i];
                if(original?.RaceProps?.Humanlike!=true || original.Faction!=party.Faction ||
                   original.Spawned || original.IsWorldPawn() || NJRRescueFirebreakR034.DirectGun(original))continue;
                // Keep actual rescue targets and explicitly recorded family/rescuer
                // identities intact; do not replace an existing world pawn.
                if(op.Targets!=null && op.Targets.Contains(original))continue;
                float cap=original.kindDef?.combatPower??0f;
                var affordable=new List<PawnKindDef>();
                foreach(PawnKindDef kind in kinds)if(kind.combatPower<=cap+remaining)affordable.Add(kind);
                if(affordable.Count==0)continue;
                for(int trial=0;trial<6 && attempts<24;trial++)
                {
                    attempts++;
                    Pawn candidate=null;
                    try
                    {
                        candidate=PawnGenerator.GeneratePawn(affordable[(attempts-1)%affordable.Count],party.Faction);
                        if(candidate==null || candidate.Spawned || candidate.IsWorldPawn())continue;
                        if(!NJRRescueFirebreakR034.DirectGun(candidate))
                        {Find.WorldPawns.PassToWorld(candidate);continue;}
                        // Keep the discarded roster member in the world, not dead.
                        Find.WorldPawns.PassToWorld(original,PawnDiscardDecideMode.KeepForever);
                        remaining=Mathf.Max(0f,remaining+cap-candidate.kindDef.combatPower);
                        pawns[i]=candidate;guns++;break;
                    }
                    catch(Exception ex)
                    {
                        if(candidate!=null && !candidate.Spawned && !candidate.IsWorldPawn() && !pawns.Contains(candidate))
                            Find.WorldPawns.PassToWorld(candidate);
                        Log.Message("[NJR][R035][Roster] native candidate unavailable: "+ex.Message);
                        break;
                    }
                }
            }
            Log.Message("[NJR][R035][Roster] direct fire="+guns+"/"+humans+" target="+desired+" attempts="+attempts);
        }

        // Called at the native Arrive(List<Pawn>, IncidentParms) boundary,
        // AFTER the native PawnGroupMaker made its choices and BEFORE either
        // spawning or native MakeLords. This is the authoritative roster gate:
        // it works for regular walk-in rescue and VIP center-drop capsules.
        internal static void CompleteGeneratedRoster(RHFOperation op,
            RHFStrikeParty party,List<Pawn> pawns)
        {
            if(op==null || party?.Faction==null || pawns==null || !party.Attempted ||
               op.Parties==null || !op.Parties.Contains(party))return;
            int minimum=RequiredMembers(party);
            if(minimum==0)return;
            int humans=0;
            float nativeSpent=0f;
            foreach(Pawn pawn in pawns)
            {
                if(pawn?.RaceProps?.Humanlike!=true || pawn.Faction!=party.Faction)continue;
                humans++;
                nativeSpent+=Mathf.Max(0f,pawn.kindDef?.combatPower ?? 0f);
            }
            bool requiresEngineer=party.Drop && op.TryPod &&
                party.Role==(int)RHFStrikeRole.Rescue;
            bool hasEngineer=false;
            if(requiresEngineer)
                foreach(Pawn pawn in pawns)
                    if(QualifiedPodEngineer(pawn,party.Faction))
                    {hasEngineer=true;break;}
            if(humans>=minimum && (!requiresEngineer || hasEngineer))
            {BalanceGroundRescue(op,party,pawns);return;}
            List<PawnKindDef> kinds=CombatKinds(party.Faction);
            if(kinds.Count==0)
            {
                Log.Error("[NJR][ForceR017] No native Combat PawnKind for "+
                    party.Faction.Name+"; generated only "+humans+"/"+minimum);
                return;
            }
            float humanBudget=Mathf.Max(0f,party.Points-party.SupplyBudget-
                party.AnimalBudgetS42-party.LoadoutBudgetS42-
                (party.Role==(int)RHFStrikeRole.Rescue ? op.PodSupplyCost : 0f));
            float unused=Mathf.Max(0f,humanBudget-nativeSpent);
            int added=0;
            while(humans<minimum)
            {
                PawnKindDef kind=kinds[0];
                float cost=kind.combatPower;
                float additional=Mathf.Max(0f,cost-unused);
                if(party.Points+additional>15000f)break;
                Pawn soldier=null;
                try
                {
                    soldier=PawnGenerator.GeneratePawn(kind,party.Faction);
                    if(soldier==null)break;
                    pawns.Add(soldier);
                    if(additional>0f)
                    {
                        party.Points+=additional;
                        op.ThreatPoints+=additional;
                        Log.Warning("[NJR][ForceR017] Native roster shortage: +"+
                            additional.ToString("F0")+" mission points for "+
                            kind.defName+" from "+party.Faction.Name);
                    }
                    unused=Mathf.Max(0f,unused+additional-cost);
                    added++;humans++;
                }
                catch(Exception e)
                {
                    Log.Error("[NJR][ForceR017] Pawn generation refused: "+e);
                    if(soldier!=null && !soldier.Spawned && !pawns.Contains(soldier))
                        soldier.Destroy(DestroyMode.Vanish);
                    break;
                }
            }
            // R019: five armed soldiers are not an air rescue team if none
            // can perform Construction. Add ONE qualified native combatant
            // before the real drop pods are instantiated; never conjure an
            // engineer at the prison later. All extra strength is paid.
            if(requiresEngineer && !hasEngineer)
            {
                int attempts=0;
                foreach(PawnKindDef kind in kinds)
                {
                    int trialsForKind=0;
                    while(attempts<8 && trialsForKind<3 && !hasEngineer)
                    {
                        attempts++;trialsForKind++;
                        Pawn candidate=null;
                        try
                        {
                            candidate=PawnGenerator.GeneratePawn(kind,party.Faction);
                            if(!QualifiedPodEngineer(candidate,party.Faction))
                            {candidate?.Destroy(DestroyMode.Vanish);continue;}
                            float need=Mathf.Max(0f,kind.combatPower-unused);
                            if(party.Points+need>15000f)
                            {candidate.Destroy(DestroyMode.Vanish);break;}
                            pawns.Add(candidate);
                            party.Points+=need;op.ThreatPoints+=need;
                            unused=Mathf.Max(0f,unused+need-kind.combatPower);
                            hasEngineer=true;
                            Log.Message("[NJR][AirR019] Real pod engineer "+
                                candidate.LabelShortCap+" included in landing; budget +"+
                                need.ToString("F0"));
                        }
                        catch(Exception e)
                        {
                            if(candidate!=null && !candidate.Spawned &&
                               !pawns.Contains(candidate))candidate.Destroy(DestroyMode.Vanish);
                            Log.Warning("[NJR][AirR019] Engineer generation: "+e.Message);
                            break;
                        }
                    }
                    if(hasEngineer || attempts>=8)break;
                }
                if(!hasEngineer)
                    Log.Error("[NJR][AirR019] No qualified Construction pawn "+
                        "for funded VIP drop. Air extraction may fall back; "+
                        "check faction's native PawnKind labor tags.");
            }
            BalanceGroundRescue(op,party,pawns);
            Log.Message("[NJR][ForceR017] Native list before arrival: "+
                party.Faction.Name+", required="+minimum+", actual="+humans+
                ", native supplements="+added+", point commitment="+
                party.Points.ToString("F0"));
            if(humans<minimum)
                Log.Error("[NJR][ForceR017] Combat maker cannot fund "+
                    "a complete wing under 15000 points: "+humans+"/"+minimum);
        }

        // A vanilla Combat PawnGroupMaker may spend an entire budget on one
        // elite, even when the budget was sized for several ordinary fighters.
        // Supplement ONLY actual ground rescuers, in the existing native lord,
        // using that faction's own combat pawn kinds and a real, logged budget.
        // Never materialize a soldier beside a delayed incoming drop pod.
        internal static void CompleteGroundRescue(RHFOperation op,
            RHFStrikeParty party,Map map,IntVec3 spawnCenter)
        {
            if(op==null || party==null || map==null || party.Drop ||
               party.Role!=(int)RHFStrikeRole.Rescue || !party.Succeeded ||
               party.Members>=RequiredMembers(party))return;
            List<PawnKindDef> candidates=CombatKinds(party.Faction);
            if(candidates.Count==0)
            {
                Log.Error("[NJR][ForceR017] Cannot complete rescue wing: faction "+
                    party.Faction.Name+" has no humanlike combat PawnGroupMaker options.");
                return;
            }
            Pawn leader=null;
            foreach(Pawn soldier in op.Squad)
                if(soldier?.Spawned==true && !soldier.Dead &&
                   soldier.Faction==party.Faction && soldier.Map==map &&
                   soldier.GetLord()!=null) {leader=soldier;break;}
            Lord nativeLord=leader?.GetLord();
            if(nativeLord==null || nativeLord.faction!=party.Faction)
            {
                Log.Warning("[NJR][ForceR017] Native rescue Lord not ready; "+
                    "cannot attach reinforcing soldiers without splitting AI authority.");
                return;
            }
            List<IntVec3> cells=RHFRescueGroundEntryS41.DeploymentCells(map,spawnCenter);
            int placed=0;
            float originalHumanBudget=Mathf.Max(0f,party.Points-
                party.SupplyBudget-party.AnimalBudgetS42-party.LoadoutBudgetS42);
            float nativeSpent=0f;
            foreach(Pawn member in op.Squad)
                if(member?.Faction==party.Faction && member.Spawned &&
                   member.Map==map && member.kindDef!=null)
                    nativeSpent+=Mathf.Max(0f,member.kindDef.combatPower);
            // A finite, real budget; native spending is reconciled first.
            float spare=Mathf.Max(0f,originalHumanBudget-nativeSpent);
            for(int index=0;index<cells.Count &&
                party.Members<RequiredMembers(party);index++)
            {
                IntVec3 cell=cells[index];
                if(cell.GetFirstPawn(map)!=null || cell.GetEdifice(map)!=null)continue;
                PawnKindDef kind=candidates[0];
                float cost=kind.combatPower;
                if(cost>spare)
                {
                    float added=cost-spare;
                    if(party.Points+added>15000f)break;
                    // This is an explicit mission strength commitment, NOT
                    // an unpriced pawn: the operator can inspect the delta.
                    party.Points+=added;
                    op.ThreatPoints+=added;
                    spare+=added;
                    Log.Warning("[NJR][ForceR017] Vanilla underfilled rescue: "+
                        "allocated another "+added.ToString("F0")+
                        " points for a real faction combatant.");
                }
                Pawn created=null;
                try
                {
                    created=PawnGenerator.GeneratePawn(kind,party.Faction);
                    if(created==null)continue;
                    GenSpawn.Spawn(created,cell,map);
                    nativeLord.AddPawn(created);
                    if(!op.Squad.Contains(created))
                    {
                        op.Squad.Add(created);
                        party.Members++;
                        RHFTactical.Register(op,party,created);
                    }
                    spare-=cost;
                    placed++;
                }
                catch(Exception e)
                {
                    Log.Error("[NJR][ForceR017] Real rescue reinforcement failed: "+e);
                    if(created!=null && created.Spawned &&
                       !op.Squad.Contains(created))created.Destroy(DestroyMode.Vanish);
                    break;
                }
            }
            Log.Message("[NJR][ForceR017] Rescue roster "+party.Faction.Name+
                ": actual="+party.Members+", required="+RequiredMembers(party)+
                ", reinforcement="+placed+", committed points="+
                party.Points.ToString("F0")+", spawn="+spawnCenter);
            if(party.Members<RequiredMembers(party))
                Log.Error("[NJR][ForceR017] UNDERSTRENGTH rescue wing: "+
                    party.Members+"/"+RequiredMembers(party)+
                    ". Inspect RimWorld faction/lord/spawn diagnostics.");
        }
    }

    // The native incident owns the very same List<Pawn> used for pod arrival,
    // the raid letter AND the Lord. Adding soldiers before Arrive makes them
    // arrive in REAL capsules and join the normal operation, not materialize.
    [HarmonyPatch(typeof(PawnsArrivalModeWorker_CenterDrop),
        nameof(PawnsArrivalModeWorker_CenterDrop.Arrive))]
    internal static class NJRCenterDropRosterPatchR017
    {
        [HarmonyPrefix]
        private static void Prefix(List<Pawn> pawns,IncidentParms parms)
        {
            if(parms?.target is not Map map ||
               parms.raidArrivalMode!=PawnsArrivalModeDefOf.CenterDrop)return;
            RHFOperation operation=RHFMissionIntegrityV130.ActiveRaidLetterOperation;
            RHFStrikeParty party=RHFOperationController.ActiveRaidParty(map,operation);
            NJRForceStrengthR017.CompleteGeneratedRoster(operation,party,pawns);
        }
    }
}
