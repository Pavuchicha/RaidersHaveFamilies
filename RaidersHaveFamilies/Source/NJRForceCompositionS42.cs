// NJR S4.2: budgeted technology doctrine. A beast pack is a tactical asset,
// NOT a separate raid scenario or a free gift to an incident.
// All potentially expensive work runs only at raid creation/arrival.
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace RaidersHaveFamilies
{
    internal static class NJRForceCompositionS42
    {
        internal static bool LowTech(Faction faction) =>
            faction?.def != null && faction.def.techLevel < TechLevel.Industrial;
        internal static bool CanUsePods(Faction faction) =>
            faction?.def != null && faction.def.techLevel >= TechLevel.Industrial;
        internal static bool CanUseSiege(Faction faction) => CanUsePods(faction);

        internal static void Reserve(RHFOperation op,RHFStrikeParty party)
        {
            if(party?.Faction==null || party.Points<=0f)return;
            // The vanilla PawnGroupMaker consumes the remaining portion.
            // No hard-coded inflation for small colonies, no extra raid points.
            party.AnimalBudgetS42 = 0f;
            party.LoadoutBudgetS42 = 0f;
            if(LowTech(party.Faction) && op!=null &&
               (op.Type==(int)RHFOperationType.Vengeance ||
                party.Role==(int)RHFStrikeRole.Diversion) && party.Points>=450f)
                party.AnimalBudgetS42 = Mathf.Min(party.Points*0.42f,5500f);
            if(RHFMod.Settings?.RoleEquipment == true && party.Points>=650f)
                party.LoadoutBudgetS42 = Mathf.Min(party.Points*0.23f,3600f);
            // Protect a meaningful vanilla attacking core and the earlier
            // separate tool/medical budget. This clamp is symmetric across tech.
            float core=party.Role==(int)RHFStrikeRole.Rescue ?
                Mathf.Max(350f,NJRForceStrengthR017.HumanCoreFloor(party)) : 350f;
            float available=Mathf.Max(0f,party.Points-party.PodCostR060-party.SupplyBudget-core);
            float reserved=party.AnimalBudgetS42+party.LoadoutBudgetS42;
            if(reserved>available && reserved>0f)
            {
                float factor=available/reserved;
                party.AnimalBudgetS42*=factor;
                party.LoadoutBudgetS42*=factor;
            }
        }

        private static readonly string[] FallbackAnimals =
            {"Warg","Bear_Grizzly","Wolf","Cougar","Bear_Polar","Megasloth"};

        // FactionDef is immutable during a RimWorld game. Cache per-def
        // options, including an empty result, so future arrivals do not
        // traverse all modded PawnGroupMaker options again.
        private static readonly Dictionary<FactionDef,List<PawnKindDef>> animalCandidates =
            new Dictionary<FactionDef,List<PawnKindDef>>();

        private static List<PawnKindDef> CandidateAnimals(Faction faction)
        {
            if(faction?.def==null)return new List<PawnKindDef>(0);
            if(animalCandidates.TryGetValue(faction.def,out List<PawnKindDef> cached))
                return cached;
            var kinds=new List<PawnKindDef>(20);
            // Most modded factions declare their actual pawn types here.
            // NEVER assume every modded tribe breeds vanilla wargs.
            if(faction?.def?.pawnGroupMakers!=null)
                foreach(PawnGroupMaker maker in faction.def.pawnGroupMakers)
                    if(maker?.options!=null)
                        foreach(PawnGenOption option in maker.options)
                        {
                            PawnKindDef kind=option?.kind;
                            if(kind?.race?.race?.Animal==true && kind.combatPower>0f &&
                               !kinds.Contains(kind) && kinds.Count<20)kinds.Add(kind);
                        }
            // Fallback only for factions without a faction-specific combat animal.
            if(kinds.Count==0)
                foreach(string name in FallbackAnimals)
                {
                    PawnKindDef kind=DefDatabase<PawnKindDef>.GetNamedSilentFail(name);
                    if(kind?.race?.race?.Animal==true && kind.combatPower>0f)
                        kinds.Add(kind);
                }
            animalCandidates[faction.def]=kinds;
            return kinds;
        }

        internal static void DeployBudgetedAnimals(RHFOperation op,RHFStrikeParty party,Map map)
        {
            if(op==null || party==null || map==null || !party.Succeeded ||
               !LowTech(party.Faction) || party.AnimalBudgetS42<30f ||
               !(op.Type==(int)RHFOperationType.Vengeance ||
                 party.Role==(int)RHFStrikeRole.Diversion))return;
            Pawn leader=null;
            // Exact operation roster, not all pawns of the faction on the map.
            foreach(Pawn p in op.Diversion)
                if(p?.Faction==party.Faction && p.Spawned && p.Map==map &&
                   !p.Dead && p.GetLord()!=null) {leader=p;break;}
            if(leader==null)
                foreach(Pawn p in op.Squad)
                    if(p?.Faction==party.Faction && p.Spawned && p.Map==map &&
                       !p.Dead && p.GetLord()!=null) {leader=p;break;}
            if(leader==null)return;
            Lord lord=leader.GetLord();
            if(lord==null || lord.faction!=party.Faction)return;
            List<PawnKindDef> kinds=CandidateAnimals(party.Faction);
            if(kinds.Count==0)return;
            int generated=0;
            var affordable=new List<PawnKindDef>(kinds.Count);
            float available=party.AnimalBudgetS42;
            // More primitive parties can use many weaker animals, or fewer
            // expensive monsters. The RNG changes the pack each raid.
            while(available>=30f)
            {
                affordable.Clear();
                foreach(PawnKindDef kind in kinds)
                    if(kind.combatPower<=available && kind.combatPower>=15f)
                        affordable.Add(kind);
                if(affordable.Count==0)break;
                PawnKindDef selected=affordable[Rand.Range(0,affordable.Count)];
                IntVec3 spawn=IntVec3.Invalid;
                // Search every actual deployment tile in expanding rings, with
                // no random-attempt cap that discards affordable reinforcements.
                int maxRadius=Math.Max(map.Size.x,map.Size.z);
                for(int radius=0;radius<=maxRadius && !spawn.IsValid;radius++)
                for(int dx=-radius;dx<=radius && !spawn.IsValid;dx++)
                for(int dz=-radius;dz<=radius;dz++)
                {
                    if(Math.Max(Math.Abs(dx),Math.Abs(dz))!=radius)continue;
                    IntVec3 c=leader.Position+new IntVec3(dx,0,dz);
                    if(c.InBounds(map) && c.Standable(map) && c.GetEdifice(map)==null &&
                       c.GetFirstPawn(map)==null){spawn=c;break;}
                }
                if(!spawn.IsValid)break;
                Pawn animal=null;
                try
                {
                    animal=PawnGenerator.GeneratePawn(selected,party.Faction);
                    if(animal==null)break;
                    GenSpawn.Spawn(animal,spawn,map);
                    lord.AddPawn(animal);
                    op.NativeAnimalsS41 ??= new List<Pawn>();
                    op.NativeAnimalsS41.Add(animal);
                    available-=selected.combatPower;
                    party.AnimalSpentS42+=selected.combatPower;
                    generated++;
                }
                catch(Exception e)
                {
                    Log.Warning("[NJR][BeastsS42] Native animal generation failed: "+e.Message);
                    if(animal!=null && animal.Spawned)animal.Destroy(DestroyMode.Vanish);
                    break;
                }
            }
            if(generated>0)
                Log.Message("[NJR][BeastsS42] "+party.Faction.Name+" / "+generated+
                    " real animals; cost "+party.AnimalSpentS42+" of reserved "+
                    party.AnimalBudgetS42+" raid points; native Lord preserved");
        }
    }
}
