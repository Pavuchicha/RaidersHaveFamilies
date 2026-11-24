// NJR S4.2 — role-aware equipment profiles funded from the SAME raid points.
// One-shot on Pawn.SpawnSetup; no ordinary background ticks. Mod faction
// PawnKind tags and existing CE weapons/ammunition are respected.
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using UnityEngine;

namespace RaidersHaveFamilies
{
    internal static class NJRAdaptiveLoadoutsS42
    {
        private static bool? combatExtended;
        private static bool CombatExtended
        {
            get
            {
                if(combatExtended.HasValue)return combatExtended.Value;
                combatExtended=false;
                foreach(ModContentPack mod in LoadedModManager.RunningModsListForReading)
                    if(mod?.PackageId!=null && mod.PackageId.IndexOf("combatextended",
                        StringComparison.OrdinalIgnoreCase)>=0) {combatExtended=true;break;}
                return combatExtended.Value;
            }
        }
        private static readonly string[] Close={"Gun_HeavySMG","Gun_ChainShotgun",
            "Gun_MachinePistol","Gun_AssaultRifle","Gun_ChargeRifle"};
        private static readonly string[] Medium={"Gun_AssaultRifle","Gun_LMG",
            "Gun_ChargeRifle","Gun_HeavySMG","Gun_Minigun"};
        private static readonly string[] Marksman={"Gun_SniperRifle","Gun_ChargeLance",
            "Gun_BoltActionRifle","Gun_ChargeRifle","Gun_AssaultRifle"};
        private static readonly string[] Heavy={"Gun_Minigun","Gun_ChargeRifle",
            "Gun_TripleRocket","Gun_DoomsdayRocket"};
        private static readonly string[] Primitive={"MeleeWeapon_Spear","MeleeWeapon_Gladius",
            "MeleeWeapon_LongSword","MeleeWeapon_Mace","Bow_Great","Bow_Short",
            "Gun_BoltActionRifle"};
        private static readonly string[] MeleeModern={"MeleeWeapon_Mace",
            "MeleeWeapon_ZeusHammer","MeleeWeapon_PlasmaSword","MeleeWeapon_LongSword"};
        private static readonly string[] BeamNames={"laser","beam","charge","pulse","plasma"};
        // Populated lazily per PawnKind when that kind first arrives. A new raid
        // does not rescan the entire modded ThingDef registry for every soldier.
        private static readonly Dictionary<PawnKindDef,List<ThingDef>> kindGunCache =
            new Dictionary<PawnKindDef,List<ThingDef>>();
        private static List<ThingDef> FactionTaggedGuns(PawnKindDef kind)
        {
            if(kind==null)return new List<ThingDef>();
            if(kindGunCache.TryGetValue(kind,out List<ThingDef> cached))return cached;
            var found=new List<ThingDef>(64);
            if(kind.weaponTags!=null)
                foreach(ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
                {
                    if(def==null || !def.IsRangedWeapon || def.weaponTags==null)continue;
                    foreach(string a in kind.weaponTags)
                    {
                        if(!def.weaponTags.Contains(a))continue;
                        found.Add(def);
                        break;
                    }
                }
            kindGunCache[kind]=found;
            return found;
        }


        private static int VarietySeed(RHFOperation op,RHFStrikeParty party,Pawn pawn)
        {
            unchecked
            {
                int seed=pawn.thingIDNumber*1103515245;
                seed^=(op?.CreatedTick??0)*13007;
                seed^=(party?.Members??0)*1619;
                seed^=(int)(party?.Points??0f)*17;
                return seed&int.MaxValue;
            }
        }
        private static bool MatchesKind(Pawn pawn,ThingDef weapon)
        {
            if(pawn?.kindDef?.weaponTags==null || weapon?.weaponTags==null)return false;
            foreach(string a in pawn.kindDef.weaponTags)
                foreach(string b in weapon.weaponTags)
                    if(a==b)return true;
            return false;
        }
        private static bool IsBeamLike(ThingDef weapon)
        {
            string n=weapon?.defName?.ToLowerInvariant()??"";
            foreach(string part in BeamNames)if(n.Contains(part))return true;
            return false;
        }
        private static bool Accessible(ThingDef def,Faction faction,Pawn pawn,
            bool primitive,bool special)
        {
            if(def==null || (!def.IsRangedWeapon && !def.IsMeleeWeapon) ||
               def.BaseMarketValue<=0f || faction?.def==null)return false;
            TechLevel tech=faction.def.techLevel;
            // A faction with industrial manufacture can field occasional
            // imported spacer weapons only at elevated storyteller budgets.
            TechLevel ceiling=tech>=TechLevel.Industrial ?
                (tech<TechLevel.Spacer ? TechLevel.Spacer : tech) : tech;
            if(def.techLevel>ceiling)return false;
            if(primitive && def.IsRangedWeapon && def.techLevel>tech)return false;
            if(!special && def.defName=="Gun_DoomsdayRocket")return false;
            // Modded faction kinds often specify custom weapon tags. Keep
            // their native weapon style if the candidate does not match it.
            string pack=def.modContentPack?.PackageId ?? "";
            bool official=pack.StartsWith("ludeon.rimworld",StringComparison.OrdinalIgnoreCase);
            if(!official && pawn.kindDef?.weaponTags?.Count>0 &&
               !MatchesKind(pawn,def) && !IsBeamLike(def))return false;
            return true;
        }
        private static ThingDef Choose(Pawn pawn,Faction faction,string[] profiles,
            float capacity,int seed,bool primitive,bool special)
        {
            var candidates=new List<ThingDef>(16);
            foreach(string key in profiles)
            {
                ThingDef def=DefDatabase<ThingDef>.GetNamedSilentFail(key);
                if(Accessible(def,faction,pawn,primitive,special) &&
                   def.BaseMarketValue<=capacity && !candidates.Contains(def))
                    candidates.Add(def);
            }
            // Support modded faction guns, but cache the registry scan by
            // PawnKind. On later arrivals we only filter the small local set.
            if(!primitive && pawn.kindDef?.weaponTags!=null)
            {
                foreach(ThingDef modGun in FactionTaggedGuns(pawn.kindDef))
                {
                    if(candidates.Count>=24)break;
                    if(!Accessible(modGun,faction,pawn,false,special) ||
                       modGun.BaseMarketValue>capacity || candidates.Contains(modGun))continue;
                    candidates.Add(modGun);
                }
            }
            if(candidates.Count==0)return null;
            return candidates[seed%candidates.Count];
        }
        private static bool ReplaceWeapon(Pawn pawn,ThingDef def,ThingDef stuff,
            float remaining,float previousValue,out float paid)
        {
            paid=0f;
            ThingWithComps previous=pawn?.equipment?.Primary;
            ThingWithComps replacement=null;
            if(pawn?.equipment==null || def==null)return false;
            float diff=0f;
            try
            {
                replacement=ThingMaker.MakeThing(def,stuff) as ThingWithComps;
                if(replacement==null)return false;
                // Bill the actually created weapon, including material cost.
                diff=Mathf.Max(0f,replacement.MarketValue-previousValue);
                if(diff>remaining)
                {replacement.Destroy(DestroyMode.Vanish);return false;}
                if(previous!=null)pawn.equipment.Remove(previous);
                pawn.equipment.AddEquipment(replacement);
                if(pawn.equipment.Primary!=replacement)
                    throw new InvalidOperationException("Replacement weapon was not installed");
            }
            catch(Exception e)
            {
                // Third-party equipment hooks can THROW AFTER successfully
                // installing the replacement. Never roll back the installed
                // gun merely because a later callback raised an exception.
                if(replacement!=null && pawn.equipment.Primary==replacement)
                {
                    Log.Warning("[NJR][KitS42] Weapon installed despite hook error: "+e.Message);
                }
                else
                {
                    if(replacement!=null && !replacement.Destroyed)
                        try { replacement.Destroy(DestroyMode.Vanish); }
                        catch(Exception cleanup) { Log.Warning("[NJR][KitS42] Weapon cleanup: "+cleanup.Message); }
                    if(previous!=null && !previous.Destroyed && pawn.equipment.Primary==null)
                        try { pawn.equipment.AddEquipment(previous); }
                        catch(Exception rollback) { Log.Error("[NJR][KitS42] Cannot restore original weapon: "+rollback); }
                    Log.Warning("[NJR][KitS42] Weapon equip failed: "+e.Message);
                    return false;
                }
            }
            // Dispose the old item only AFTER the new weapon is confirmed
            // equipped. A failure while destroying the old item must NEVER
            // disarm the pawn, roll back the bill or remove the new weapon.
            paid=diff;
            if(previous!=null && !previous.Destroyed)
                try { previous.Destroy(DestroyMode.Vanish); }
                catch(Exception e) { Log.Warning("[NJR][KitS42] Old weapon cleanup failed: "+e.Message); }
            return true;
        }
        private static float ArmorUpgrade(Pawn pawn,RHFStrikeParty party,bool heavy)
        {
            if(pawn?.apparel==null || party==null)return 0f;
            foreach(Apparel worn in pawn.apparel.WornApparel)
                if(worn?.def?.defName!=null &&
                   (worn.def.defName.Contains("MarineArmor") ||
                    worn.def.defName.Contains("CataphractArmor") ||
                    worn.def.defName.Contains("PlateArmor")))return 0f;
            bool spacer=party.Faction.def.techLevel>=TechLevel.Spacer;
            ThingDef armor=DefDatabase<ThingDef>.GetNamedSilentFail(
                heavy && spacer ? "Apparel_MarineArmor" : "Apparel_FlakVest");
            if(armor==null || armor.BaseMarketValue<=0f ||
               armor.BaseMarketValue>party.LoadoutBudgetS42-party.LoadoutSpentS42 ||
               armor.techLevel>party.Faction.def.techLevel)return 0f;
            if(!NJRApparelCompatibilityS43.CanAdd(pawn,armor))return 0f;
            Apparel apparel=null;
            try
            {
                apparel=ThingMaker.MakeThing(armor) as Apparel;
                if(apparel==null)return 0f;
                // Modded stuff/quality may differ from BaseMarketValue.
                float actual=apparel.MarketValue;
                if(actual>party.LoadoutBudgetS42-party.LoadoutSpentS42)
                {apparel.Destroy(DestroyMode.Vanish);return 0f;}
                pawn.apparel.Wear(apparel);
                if(pawn.apparel.WornApparel.Contains(apparel))return actual;
                if(!apparel.Destroyed && !apparel.Spawned && apparel.ParentHolder==null)
                    apparel.Destroy(DestroyMode.Vanish);
                return 0f;
            }
            catch(Exception e)
            {
                Log.Warning("[NJR][KitS42] Armor not wearable: "+e.Message);
                // A modded Wear hook may throw AFTER attaching the item.
                // Roll back the optional upgrade instead of leaving free gear.
                if(apparel!=null && pawn.apparel.WornApparel.Contains(apparel))
                    try { pawn.apparel.Remove(apparel); }
                    catch(Exception rollback) { Log.Warning("[NJR][KitS42] Armor rollback: "+rollback.Message); }
                if(apparel!=null && !apparel.Destroyed && !apparel.Spawned &&
                   apparel.ParentHolder==null)apparel.Destroy(DestroyMode.Vanish);
                return 0f;
            }
        }
        internal static void Equip(RHFOperation op,RHFStrikeParty party,RHFTacticalMember member)
        {
            Pawn pawn=member?.Pawn;
            if(op==null || party==null || pawn?.Faction!=party.Faction ||
               pawn.equipment==null || party.LoadoutBudgetS42<=party.LoadoutSpentS42 ||
               RHFMod.Settings?.RoleEquipment!=true)return;
            int seed=VarietySeed(op,party,pawn);
            float points=party.Points;
            bool primitive=NJRForceCompositionS42.LowTech(party.Faction);
            bool heavyTier=points>=4500f;
            bool apexTier=points>=8000f;
            bool ranged=pawn.equipment.Primary?.def?.IsRangedWeapon==true;
            // CE's ammo and loadout generation remains in charge of ranged guns.
            // Do not equip an unactionable weapon with no rounds.
            if(op.Type==(int)RHFOperationType.Vengeance &&
               points>=8500f && seed%9==0 && party.HeavyWeaponsIssuedS42==0 &&
               NJROrbitalSupportS42.TryIssue(party,member))return;
            if(CombatExtended && ranged)return;
            RHFCombatRole role=(RHFCombatRole)member.Role;
            if(role==RHFCombatRole.Breacher || role==RHFCombatRole.Medic)return;
            string[] catalogue=primitive?Primitive:
                role==RHFCombatRole.Cover?Marksman:
                role==RHFCombatRole.Flanker?Close:
                role==RHFCombatRole.Escort?Medium:
                heavyTier && seed%5==0?Heavy:
                seed%3==0?Close:Medium;
            bool heavySpecial=!primitive && heavyTier &&
                role!=RHFCombatRole.Escort && seed%7==0 &&
                party.HeavyWeaponsIssuedS42<Math.Max(1,(int)(points/7000f));
            if(heavySpecial)
                catalogue=apexTier ? new []{"Gun_DoomsdayRocket","Gun_TripleRocket",
                    "Gun_Minigun","Gun_ChargeRifle"} :
                    new []{"Gun_TripleRocket","Gun_Minigun","Gun_ChargeRifle"};
            // A portion of heavy assaults use melee armor and uranium maces
            // (or existing royalty weapons), rather than only assault rifles.
            bool meleePreset=role==RHFCombatRole.Assault &&
                seed%7==1 && !primitive && points>=1700f &&
                party.Role!=(int)RHFStrikeRole.Rescue;
            if(meleePreset)catalogue=MeleeModern;
            ThingWithComps old=pawn.equipment.Primary;
            float oldValue=old?.MarketValue??0f;
            // Upgrades are billed on value DIFFERENCE, preserving value of
            // equipment already included in the native raid pawn budget.
            float maxValue=oldValue+Mathf.Max(0f,party.LoadoutBudgetS42-
                party.LoadoutSpentS42);
            ThingDef selected=Choose(pawn,party.Faction,catalogue,maxValue,
                seed/3,primitive,heavySpecial);
            if(selected==null || selected==old?.def)return;
            if(CombatExtended && selected.IsRangedWeapon)return;
            ThingDef stuff=selected.MadeFromStuff ?
                (selected.defName=="MeleeWeapon_Mace" && !primitive ?
                    ThingDefOf.Uranium : GenStuff.DefaultStuffFor(selected)) : null;
            float difference;
            if(!ReplaceWeapon(pawn,selected,stuff,
                party.LoadoutBudgetS42-party.LoadoutSpentS42,oldValue,
                out difference))return;
            member.WeaponMissing=pawn.equipment.Primary==null;
            member.PresetS42=1+seed%8;
            member.GearCostS42=difference;
            party.LoadoutSpentS42+=difference;
            if(heavySpecial && (selected.defName=="Gun_DoomsdayRocket" ||
                selected.defName=="Gun_TripleRocket"))party.HeavyWeaponsIssuedS42++;
            if(meleePreset && selected.IsMeleeWeapon)
                party.LoadoutSpentS42+=ArmorUpgrade(pawn,party,heavyTier);
        }
    }
}
