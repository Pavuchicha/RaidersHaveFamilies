// NJR S4.2.2: a real, paid, ONE-USE orbital targeting apparel, not free
// scripted explosions. The actual vanilla Verb_PowerBeam owns warmup and shot.
// Fire only at physically observed targets far from friendly soldiers / VIPs.
// No outside-combat polling; use the typed vanilla 1.7 verb owner API.
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RaidersHaveFamilies
{
    internal static class NJROrbitalSupportS42
    {
        internal static bool TryIssue(RHFStrikeParty party,RHFTacticalMember member)
        {
            Pawn pawn=member?.Pawn;
            if(pawn?.apparel==null || party==null ||
               !NJRForceCompositionS42.CanUsePods(party.Faction) ||
               party.HeavyWeaponsIssuedS42>0)return false;
            ThingDef def=DefDatabase<ThingDef>.GetNamedSilentFail("OrbitalTargeterPowerBeam");
            if(def==null || def.BaseMarketValue<=0f ||
               party.LoadoutBudgetS42-party.LoadoutSpentS42<def.BaseMarketValue)return false;
            if(!NJRApparelCompatibilityS43.CanAdd(pawn,def))return false;
            Apparel item=null;
            try
            {
                item=ThingMaker.MakeThing(def) as Apparel;
                if(item==null)return false;
                float actualCost=item.MarketValue;
                if(actualCost>party.LoadoutBudgetS42-party.LoadoutSpentS42)
                {item.Destroy(DestroyMode.Vanish);return false;}
                pawn.apparel.Wear(item);
                if(!pawn.apparel.WornApparel.Contains(item))
                {
                    if(!item.Destroyed && !item.Spawned && item.ParentHolder==null)
                        item.Destroy(DestroyMode.Vanish);
                    return false;
                }
                party.LoadoutSpentS42+=actualCost;
                party.HeavyWeaponsIssuedS42++;
                member.OrbitalTargeterS42=item;
                member.GearCostS42+=actualCost;
                member.PresetS42=9; // strategic fire support
                return true;
            }
            catch(Exception e)
            {
                Log.Warning("[NJR][OrbitalS42] Belt cannot be worn: "+e.Message);
                // Optional utility apparel must not survive a failed equip as
                // an unpaid belt when another mod throws after Wear().
                if(item!=null && pawn.apparel.WornApparel.Contains(item))
                    try { pawn.apparel.Remove(item); }
                    catch(Exception rollback) { Log.Warning("[NJR][OrbitalS42] Belt rollback: "+rollback.Message); }
                if(item!=null && !item.Destroyed && item.ParentHolder==null)
                    item.Destroy(DestroyMode.Vanish);
                return false;
            }
        }

        // RimWorld 1.7: orbital utility belts use CompApparelVerbOwner.
        // Its public AllVerbs/VerbTracker is the authoritative source;
        // searching private fields by string was unnecessary and fragile.
        // A mod-specific targeter with another comp simply cannot be used by
        // our optional AI without a dedicated integration.
        private static Verb FindOrbitalVerb(Thing item)
        {
            ThingWithComps compItem=item as ThingWithComps;
            if(compItem?.AllComps==null)return null;
            foreach(ThingComp comp in compItem.AllComps)
                if(comp is CompApparelVerbOwner owner)
                    foreach(Verb verb in owner.AllVerbs)
                        if(verb is Verb_PowerBeam)return verb;
            return null;
        }

        private static bool FriendlyNear(RHFOperation op,Map map,IntVec3 position)
        {
            if(op?.Targets!=null)
                foreach(Pawn vip in op.Targets)
                    if(vip!=null && !vip.Dead && vip.MapHeld==map &&
                       (vip.Position-position).LengthHorizontalSquared<225)return true;
            foreach(Pawn ally in op.Squad)
                if(ally!=null && ally.Map==map && !ally.Dead &&
                   (ally.Position-position).LengthHorizontalSquared<225)return true;
            foreach(Pawn ally in op.Diversion)
                if(ally!=null && ally.Map==map && !ally.Dead &&
                   (ally.Position-position).LengthHorizontalSquared<225)return true;
            if(op.Freed!=null)
                foreach(Pawn vip in op.Freed)
                    if(vip!=null && !vip.Dead && vip.MapHeld==map &&
                       (vip.Position-position).LengthHorizontalSquared<225)return true;
            if(op.NativeAnimalsS41!=null)
                foreach(Pawn ally in op.NativeAnimalsS41)
                    if(ally!=null && ally.Map==map && !ally.Dead &&
                       (ally.Position-position).LengthHorizontalSquared<225)return true;
            // An abandoned construction blueprint is still valuable to the
            // mission even if the engineer just walked away from the site.
            if(op.Type==(int)RHFOperationType.Rescue && op.Phase==3 &&
               ((op.PodCell.IsValid &&
                 (op.PodCell-position).LengthHorizontalSquared<225) ||
                (op.PodLauncherCell.IsValid &&
                 (op.PodLauncherCell-position).LengthHorizontalSquared<225)))
                return true;
            return false;
        }

        internal static void Tick(RHFOperation op,Map map,int now)
        {
            if(op?.TacticalMembers==null || map==null ||
               (op.Type!=(int)RHFOperationType.Vengeance &&
                op.Type!=(int)RHFOperationType.Rescue) ||
               (op.Type==(int)RHFOperationType.Rescue &&
                (op.Phase==2 || op.Phase==4)) || now%300>120)return;
            foreach(RHFTacticalMember member in op.TacticalMembers)
            {
                Pawn carrier=member?.Pawn;
                if(member==null || member.OrbitalUsedS42 || member.OrbitalTargeterS42==null ||
                   carrier==null || carrier.Dead || carrier.Downed ||
                   carrier.Map!=map || carrier.apparel==null ||
                   !(member.OrbitalTargeterS42 is Apparel belt) ||
                   !carrier.apparel.WornApparel.Contains(belt))continue;
                Verb orbital=FindOrbitalVerb(belt);
                if(orbital==null)continue;
                Pawn chosen=null;
                int nearbyBest=-1;
                foreach(Pawn target in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
                {
                    if(target==null || target.Dead || target.Downed ||
                       (target.Position-carrier.Position).LengthHorizontalSquared>1800 ||
                       !GenSight.LineOfSight(carrier.Position,target.Position,map) ||
                       FriendlyNear(op,map,target.Position))continue;
                    int clustered=0;
                    foreach(Pawn witness in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
                        if(witness!=null && !witness.Dead &&
                           (witness.Position-target.Position).LengthHorizontalSquared<64)
                            clustered++;
                    if(clustered>nearbyBest){nearbyBest=clustered;chosen=target;}
                }
                if(chosen==null)continue; // next carrier might have a safe target
                try
                {
                    // Vanilla verb does actual aiming, charge consumption and
                    // physical orbital projectile; never spawn a fake blast.
                    if(orbital.TryStartCastOn(new LocalTargetInfo(chosen.Position)))
                    {
                        member.OrbitalUsedS42=true; // once per paid targeter
                        Log.Message("[NJR][OrbitalS42] Actual beam target selected by "+
                            carrier.LabelShortCap+" at "+chosen.Position);
                    }
                }
                catch(Exception e)
                { Log.Warning("[NJR][OrbitalS42] Real verb refused: "+e.Message);
                  member.OrbitalUsedS42=true; }
                return;
            }
        }
    }
}
