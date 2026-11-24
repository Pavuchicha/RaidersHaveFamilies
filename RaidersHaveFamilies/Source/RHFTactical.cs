// RHF v0.9.0 — faction-safe, bounded tactical role assignment and physical mission supplies.
// No whole-map tick scans, no PawnGenerator transpiler, no weapon/ammo replacement.
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RaidersHaveFamilies
{
    public enum RHFCombatRole
    {
        Assault = 0,
        Breacher = 1,
        Escort = 2,
        Cover = 3,
        Medic = 4,
        Flanker = 5
    }

    public sealed class RHFTacticalMember : IExposable
    {
        public Pawn Pawn;
        public Faction Faction;
        public int Role;
        public bool Equipped;
        public bool WeaponMissing;
        public bool MedicalSupplies;
        public int KitIssued;
        public int LastTacticalOrder;
        public int PresetS42;
        public float GearCostS42;
        public Thing OrbitalTargeterS42;
        public bool OrbitalUsedS42;
        public RHFTacticalMember() { }
        public RHFTacticalMember(Pawn pawn, RHFCombatRole role)
        {
            Pawn = pawn;
            Faction = pawn.Faction;
            Role = (int)role;
        }
        public void ExposeData()
        {
            Scribe_References.Look(ref Pawn, "pawn");
            Scribe_References.Look(ref Faction, "faction");
            Scribe_Values.Look(ref Role, "role", 0);
            Scribe_Values.Look(ref Equipped, "equipped", false);
            Scribe_Values.Look(ref WeaponMissing, "weaponMissing", false);
            Scribe_Values.Look(ref MedicalSupplies, "medicalSupplies", false);
            Scribe_Values.Look(ref KitIssued, "kitIssued", 0);
            Scribe_Values.Look(ref LastTacticalOrder, "lastTacticalOrder", 0);
            Scribe_Values.Look(ref PresetS42,"njrWeaponPresetS42",0);
            Scribe_Values.Look(ref GearCostS42,"njrWeaponCostS42",0f);
            Scribe_References.Look(ref OrbitalTargeterS42,"njrOrbitalItemS42");
            Scribe_Values.Look(ref OrbitalUsedS42,"njrOrbitalUsedS42",false);
        }
    }

    public static class RHFTactical
    {
        public static string RoleName(int role)
        {
            switch ((RHFCombatRole)role)
            {
                case RHFCombatRole.Breacher: return "сапёр";
                case RHFCombatRole.Escort: return "сопровождение";
                case RHFCombatRole.Cover: return "огневое прикрытие";
                case RHFCombatRole.Medic: return "санитар";
                case RHFCombatRole.Flanker: return "фланговый стрелок";
                default: return "штурмовик";
            }
        }

        // Demolition axes are existing RimWorld weapons. No RHF explosive kit.
        private const float MedicalKitCost = 35f;
        // No arbitrarily capped set of mission participants or visible defenders.
        private static readonly Dictionary<string, ThingDef> meleeFallbackCache =
            new Dictionary<string, ThingDef>();

        public static void Register(RHFOperation op, RHFStrikeParty party, Pawn pawn)
        {
            if (op == null || party == null || pawn == null || pawn.RaceProps == null ||
                !pawn.RaceProps.Humanlike || op.TacticalMembers == null) return;
            for (int i = 0; i < op.TacticalMembers.Count; i++)
                if (op.TacticalMembers[i]?.Pawn == pawn) return;

            int ordinal = Math.Max(0, party.Members - 1);
            bool capable = pawn.health?.capacities != null &&
                pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation);
            bool ranged = pawn.equipment?.Primary?.def?.IsRangedWeapon == true;
            bool rescue = party.Role == (int)RHFStrikeRole.Rescue;
            RHFCombatRole role;
            if (rescue)
            {
                // At least one physical axe specialist, then a second once
                // the actually spawned rescue wing contains six soldiers.
                // An unrelated faction/wing cannot satisfy this requirement.
                bool equippedSapper=false;
                foreach(RHFTacticalMember previous in op.TacticalMembers)
                    if(previous?.Faction==party.Faction && previous.Pawn!=null &&
                       previous.Pawn.Spawned && !previous.Pawn.Dead &&
                       previous.Role==(int)RHFCombatRole.Breacher &&
                       IsBreachAxe(previous.Pawn)) {equippedSapper=true;break;}
                if(capable && !equippedSapper)
                    role = RHFCombatRole.Breacher;
                else if (ordinal % 7 == 1 || ordinal % 7 == 4) role = RHFCombatRole.Escort;
                else if (ordinal % 7 == 3 && capable) role = RHFCombatRole.Medic;
                else if (ranged) role = RHFCombatRole.Cover;
                else role = RHFCombatRole.Assault;
            }
            else
            {
                if (ranged && ordinal % 5 == 0) role = RHFCombatRole.Cover;
                else if (capable && ordinal % 7 == 2 &&
                    (party.Breach || party.Faction?.def?.techLevel >= TechLevel.Industrial))
                    role = RHFCombatRole.Breacher;
                else if (ranged && ordinal % 6 == 3) role = RHFCombatRole.Flanker;
                else role = RHFCombatRole.Assault;
            }

            RHFTacticalMember member = new RHFTacticalMember(pawn, role);
            op.TacticalMembers.Add(member);
            // One-shot supply issue on arrival, not a recurring spawn/tick patch.
            Supply(op, party, member);
        }

        public static RHFTacticalMember Member(RHFOperation op, Pawn pawn)
        {
            if (op?.TacticalMembers == null || pawn == null) return null;
            for (int i = 0; i < op.TacticalMembers.Count; i++)
                if (op.TacticalMembers[i]?.Pawn == pawn) return op.TacticalMembers[i];
            return null;
        }

        private static bool Carry(RHFTacticalMember member, ThingDef def, int count)
        {
            if (member?.Pawn?.inventory?.innerContainer == null || def == null || count <= 0)
                return false;
            Thing stack = null;
            try
            {
                stack = ThingMaker.MakeThing(def);
                stack.stackCount = count;
                if (member.Pawn.inventory.innerContainer.TryAdd(stack)) return true;
                if (!stack.Destroyed) stack.Destroy();
            }
            catch (Exception e)
            {
                if (stack != null && !stack.Destroyed) stack.Destroy();
                if (RHFMod.Settings?.VerboseLogging == true)
                    Log.Warning("[RHF][Equipment] supply rejected by pawn inventory: " + e.Message);
            }
            return false;
        }

        private static void Supply(RHFOperation op, RHFStrikeParty party, RHFTacticalMember member)
        {
            Pawn pawn = member.Pawn;
            if (pawn == null || !pawn.Spawned || member.Equipped) return;
            member.Equipped = true;
            if (RHFMod.Settings?.RoleEquipment != true)
            {
                member.WeaponMissing = pawn.equipment?.Primary == null;
                if (member.WeaponMissing) party.UnarmedCount++;
                return; // explicit user opt-out also disables weapon fallback
            }
            // Preserve all pawnkind/faction-generated guns, armor, weapons and CE ammo.
            // Only when an otherwise combat-capable humanlike is *completely unarmed*,
            // try a melee fallback that shares its faction-specific PawnKind weapon tags.
            if (pawn.equipment?.Primary == null && pawn.equipment != null &&
                pawn.RaceProps.ToolUser && pawn.health?.capacities != null &&
                pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                TryFactionMeleeFallback(pawn);
            member.WeaponMissing = pawn.equipment?.Primary == null;
            if (member.WeaponMissing) party.UnarmedCount++;

            if (member.Role == (int)RHFCombatRole.Breacher)
            {
                if(!IssueBreachAxe(pawn,party))
                    member.Role=(int)RHFCombatRole.Assault;
            }
            else if (member.Role == (int)RHFCombatRole.Medic)
            {
                ThingDef medical = pawn.Faction?.def?.techLevel >= TechLevel.Industrial ?
                    ThingDefOf.MedicineIndustrial : ThingDefOf.MedicineHerbal;
                if (party.SupplySpent + MedicalKitCost <= party.SupplyBudget &&
                    Carry(member, medical, 2))
                { member.MedicalSupplies = true; party.SupplySpent += MedicalKitCost; party.Medics++; }
                else member.Role = (int)RHFCombatRole.Escort;
            }
            if (member.Role == (int)RHFCombatRole.Cover && member.WeaponMissing)
                member.Role = (int)RHFCombatRole.Assault;
            NJRAdaptiveLoadoutsS42.Equip(op,party,member);
        }

        // This fallback is faction/tag/tech constrained. It does not invent guns or
        // ammunition, so Combat Extended and faction-specific firearm loaders retain control.
        private static void TryFactionMeleeFallback(Pawn pawn)
        {
            PawnKindDef kind = pawn.kindDef;
            if (kind == null || kind.weaponTags == null || kind.weaponTags.Count == 0) return;
            string cacheKey = kind.defName + "|" + (pawn.Faction?.def?.techLevel.ToString() ?? "?");
            ThingDef choice;
            if (!meleeFallbackCache.TryGetValue(cacheKey, out choice))
            {
                choice = null;
                float best = float.MaxValue;
                List<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
                for (int i = 0; i < all.Count; i++)
                {
                    ThingDef def = all[i];
                    if (def == null || !def.IsMeleeWeapon || def.MadeFromStuff ||
                        def.weaponTags == null || def.weaponTags.Count == 0 ||
                        (int)def.techLevel > (int)(pawn.Faction?.def?.techLevel ?? TechLevel.Neolithic))
                        continue;
                    bool tagsMatch = false;
                    for (int a=0;a<kind.weaponTags.Count && !tagsMatch;a++)
                        for (int b=0;b<def.weaponTags.Count;b++)
                            if (kind.weaponTags[a] == def.weaponTags[b]) { tagsMatch=true;break; }
                    if (!tagsMatch) continue;
                    float price = def.BaseMarketValue;
                    if (price > 0f && price < best && price <= Math.Max(50f, kind.weaponMoney.max * 1.2f))
                    { choice = def; best = price; }
                }
                if (meleeFallbackCache.Count < 512) meleeFallbackCache[cacheKey] = choice;
            }
            if (choice == null) return;
            try
            {
                ThingWithComps melee = ThingMaker.MakeThing(choice) as ThingWithComps;
                if (melee != null) pawn.equipment.AddEquipment(melee);
            }
            catch (Exception e)
            {
                if (RHFMod.Settings?.VerboseLogging == true)
                    Log.Warning("[RHF][Equipment] pawnkind melee fallback rejected: " + e.Message);
            }
        }

        public static bool IsBreachAxe(Pawn pawn)
        {
            return pawn?.equipment?.Primary?.def?.defName=="MeleeWeapon_BreachAxe";
        }

        private static bool IssueBreachAxe(Pawn pawn,RHFStrikeParty party)
        {
            if(pawn?.equipment==null || pawn.Map==null ||
               pawn.health?.capacities==null ||
               !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) ||
               RHFMod.Settings?.RoleEquipment != true)return false;
            if(IsBreachAxe(pawn))return true;
            ThingDef weapon=DefDatabase<ThingDef>.GetNamedSilentFail("MeleeWeapon_BreachAxe");
            if(weapon==null || !weapon.IsMeleeWeapon)return false;
            ThingWithComps replacement=null;
            ThingWithComps previous=pawn.equipment.Primary;
            try
            {
                // Replace one normally generated weapon rather than giving
                // an extra unlimited item. The standard RimWorld / CE melee
                // verbs and Demolish damage still own every attack.
                replacement=ThingMaker.MakeThing(weapon,GenStuff.DefaultStuffFor(weapon)) as ThingWithComps;
                if(replacement==null)return false;
                if(previous!=null)pawn.equipment.Remove(previous);
                pawn.equipment.AddEquipment(replacement);
                if(previous!=null && !previous.Destroyed)previous.Destroy();
                if(party!=null)party.BreachKits++;
                return true;
            }
            catch(Exception e)
            {
                if(replacement!=null && pawn.equipment.Primary==replacement)
                    pawn.equipment.Remove(replacement);
                if(replacement!=null && !replacement.Destroyed)replacement.Destroy();
                if(previous!=null && !previous.Destroyed && pawn.equipment.Primary==null)
                    pawn.equipment.AddEquipment(previous);
                Log.Warning("[RHF][MeleeBreach] Couldn't issue vanilla axe: "+e.Message);
                return false;
            }
        }

        public static void EnsureBreachTeam(RHFOperation op,Map map)
        {
            if(op?.Squad==null || op.TacticalMembers==null || map==null ||
               op.Squad.Count<6 || RHFMod.Settings?.RoleEquipment!=true)return;
            // A second axe for larger rescue wings; once per operation,
            // never re-equip a dead or withdrawn unit every tactical tick.
            int axes=0;
            foreach(Pawn p in op.Squad)
                if(p!=null && p.Map==map && !p.Dead && IsBreachAxe(p))axes++;
            if(axes>=2)return;
            foreach(RHFStrikeParty party in op.Parties)
            {
                if(party==null || party.Role!=(int)RHFStrikeRole.Rescue ||
                   !party.Succeeded)continue;
                for(int i=0;i<op.Squad.Count;i++)
                {
                    Pawn candidate=op.Squad[i];
                    RHFTacticalMember member=Member(op,candidate);
                    if(candidate==null || candidate.Faction!=party.Faction ||
                       !candidate.Spawned || candidate.Map!=map ||
                       candidate.Dead || candidate.Downed ||
                       member==null || IsBreachAxe(candidate) ||
                       member.Role==(int)RHFCombatRole.Escort ||
                       member.Role==(int)RHFCombatRole.Medic ||
                       NJRRescueFirebreakR034.DirectGun(candidate))continue;
                    if(IssueBreachAxe(candidate,party))
                    {
                        member.Role=(int)RHFCombatRole.Breacher;
                        return;
                    }
                }
            }
        }

        // At most one suppression order per decision and a bounded enemy shortlist.
        // Ordinary combatants, modded combat verbs and CE aiming remain vanilla-owned.
        public static bool TryCover(Map map, RHFOperation op, Pawn fighter, Pawn captive, int now)
        {
            RHFTacticalMember member = Member(op, fighter);
            if (member == null || member.Role != (int)RHFCombatRole.Cover ||
                fighter.equipment?.Primary?.def?.IsRangedWeapon != true ||
                now - member.LastTacticalOrder < 900 || fighter.Downed || fighter.Map != map)
                return false;
            Pawn enemy = null;
            float nearest = 900f;
            foreach (Pawn possible in map.mapPawns.FreeColonistsSpawned)
            {
                if (possible == null || possible.Dead || possible.Downed || possible.Map != map) continue;
                float d = (possible.Position-fighter.Position).LengthHorizontalSquared;
                if (d > 625f || d >= nearest) continue;
                // Do not direct fire through a captive in a tightly enclosed room.
                if (captive != null && (possible.Position-captive.Position).LengthHorizontalSquared < 5) continue;
                // A tactical spotter must actually see the target. This
                // prevents issuing blind shots through sealed prison walls.
                if (!GenSight.LineOfSight(fighter.Position, possible.Position, map)) continue;
                enemy = possible; nearest = d;
            }
            if (enemy == null) return false;
            Job job = JobMaker.MakeJob(JobDefOf.AttackStatic, enemy);
            if (fighter.jobs == null) return false;
            fighter.jobs.TryTakeOrderedJob(job);
            member.LastTacticalOrder = now;
            return true;
        }

        // Called only once per strategic pulse for vengeance/diversion: no new map-wide AI tick.
        // Largely defer to the vanilla Lord; only one or two equipped supports get target orders.
        public static void PressureAssault(Map map, RHFOperation op, int now)
        {
            if (op?.TacticalMembers == null || now % 1200 > 300) return;
            int issued = 0;
            for (int i=0;i<op.TacticalMembers.Count;i++)
            {
                RHFTacticalMember member=op.TacticalMembers[i];
                Pawn pawn=member?.Pawn;
                if (pawn == null || pawn.Dead || pawn.Downed || pawn.Map!=map ||
                    (member.Role != (int)RHFCombatRole.Cover &&
                     member.Role != (int)RHFCombatRole.Flanker)) continue;
                if (member.Role == (int)RHFCombatRole.Cover)
                { if (TryCover(map, op, pawn, null, now)) issued++; }
                else if (TryFlank(map, member, now)) issued++;
            }
        }


        // Only a single short reposition per group every 1200 ticks; do not
        // override ranged attacks or Lords continuously. Fallback to vanilla AI.
        private static bool TryFlank(Map map, RHFTacticalMember member, int now)
        {
            Pawn scout = member?.Pawn;
            if (scout == null || scout.Map != map || scout.Downed || scout.jobs == null ||
                member.Role != (int)RHFCombatRole.Flanker ||
                now - member.LastTacticalOrder < 1200) return false;
            Pawn enemy = null;
            float nearest = 900f;
            foreach (Pawn candidate in map.mapPawns.FreeColonistsSpawned)
            {
                if (candidate == null || candidate.Downed || candidate.Dead) continue;
                float dist = (candidate.Position-scout.Position).LengthHorizontalSquared;
                if (dist >= nearest || dist < 49f) continue;
                nearest = dist; enemy = candidate;
            }
            if (enemy == null) return false;
            IntVec3 dx = scout.Position.x <= enemy.Position.x ? IntVec3.West : IntVec3.East;
            IntVec3 side = scout.Position.z <= enemy.Position.z ? IntVec3.South : IntVec3.North;
            IntVec3 waypoint = enemy.Position + dx * 6 + side * 5;
            if (!waypoint.InBounds(map) || !waypoint.Standable(map) ||
                !scout.CanReach(waypoint, PathEndMode.OnCell, Danger.Deadly)) return false;
            scout.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, waypoint));
            member.LastTacticalOrder = now;
            return true;
        }

        public static string Audit(RHFOperationController controller)
        {
            return controller == null ? "NJR: контроллер операции отсутствует." : controller.TacticalDebugStatus();
        }
    }
}
