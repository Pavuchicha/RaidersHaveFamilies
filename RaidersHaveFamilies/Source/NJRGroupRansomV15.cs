// NJR V15 - atomic group ransom, independent of any tactical raid.
// A group contract is saved on each prisoner. It pays exactly ONCE, only
// after every named captive is physically and safely released from the map.
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RaidersHaveFamilies
{
    internal static class NJRGroupRansomV15
    {
        private const int MaximumGroup=12;
        private const int CandidatePreview=24;

        internal static List<RHFHostageRecord> Candidates(Faction faction,Map map)
        {
            var matches=new List<RHFHostageRecord>();
            var ledger=Current.Game?.GetComponent<RHFGameLedger>();
            if(faction==null || map==null || ledger?.Hostages==null)return matches;
            foreach(RHFHostageRecord r in ledger.Hostages)
            {
                if(matches.Count>=CandidatePreview)break;
                if(r?.OriginalFaction!=faction || r.Recruited || r.Paid ||
                   r.Negotiated || r.GroupRansomId!=0 ||
                   r.Pawn?.Map!=map || !RHFHostageDiplomacy.HostageEligible(r.Pawn))continue;
                matches.Add(r);
            }
            return matches;
        }

        internal static bool Sign(Faction faction,List<RHFHostageRecord> group,
            Pawn negotiator,int expectedTotal,out int total)
        {
            total=0;
            var ledger=Current.Game?.GetComponent<RHFGameLedger>();
            if(ledger==null || faction==null || group==null ||
               group.Count<2 || group.Count>MaximumGroup)return false;
            Map map=group[0]?.Pawn?.Map;
            if(map==null || map.Parent==null)return false;
            var unique=new HashSet<Pawn>();
            foreach(RHFHostageRecord r in group)
            {
                if(r==null || !ledger.Hostages.Contains(r) || r.OriginalFaction!=faction ||
                   r.Negotiated || r.Recruited || r.Paid || r.GroupRansomId!=0 ||
                   r.Pawn?.Map!=map || !RHFHostageDiplomacy.HostageEligible(r.Pawn) ||
                   !unique.Add(r.Pawn))return false;
                int price=RHFHostageDiplomacy.NegotiatedPrice(r,negotiator);
                if(price<1 || total>int.MaxValue-price)return false;
                total+=price;
            }
            if(total!=expectedTotal)return false; // never silently change quoted terms
            int id=ledger.NextGroupRansomId;
            if(id<=0 || id==int.MaxValue)return false;
            ledger.NextGroupRansomId=id+1;
            foreach(RHFHostageRecord r in group)
            {
                r.GroupRansomId=id;
                r.GroupRansomMembers=group.Count;
                r.GroupRansomSilver=total;
                r.GroupReleaseConfirmed=false;
                r.GroupOriginMapId=map.Parent?.GetUniqueLoadID() ?? "";
                r.GroupPaymentAttempted=false;
                r.PendingSilver=0; // A group delivers one combined payment.
                r.PendingGoodwill=false;
                r.Negotiated=true;
                r.Refused=false;
                r.OfferDeadlineTick=0;
            }
            return true;
        }

        internal static void Abort(RHFGameLedger ledger,int id,string cause)
        {
            if(ledger?.Hostages==null || id<=0)return;
            int count=0;
            for(int i=ledger.Hostages.Count-1;i>=0;i--)
            {
                RHFHostageRecord r=ledger.Hostages[i];
                if(r==null || r.GroupRansomId!=id)continue;
                bool exited=r.GroupReleaseConfirmed;
                r.GroupRansomId=0;
                r.GroupRansomMembers=0;
                r.GroupRansomSilver=0;
                r.GroupReleaseConfirmed=false;
                r.GroupOriginMapId="";
                r.GroupPaymentAttempted=false;
                r.Negotiated=false;
                r.PendingSilver=0;
                r.PendingGoodwill=false;
                if(exited)ledger.Hostages.RemoveAt(i);
                if(exited)ledger.InvalidateHostageIndex();
                count++;
            }
            if(count>0)Log.Warning("[NJR][GroupRansom] #"+id+" aborted: "+cause+
                "; members="+count+"; silver paid=0");
        }

        // Runs only at the existing sparse diplomatic audit cadence. A missing,
        // dead, sold or recruited participant invalidates the WHOLE contract.
        internal static void Audit(RHFGameLedger ledger)
        {
            if(ledger?.Hostages==null)return;
            var bad=new HashSet<int>();
            foreach(RHFHostageRecord r in ledger.Hostages)
            {
                if(r==null || r.GroupRansomId<=0 || r.GroupReleaseConfirmed)continue;
                // Vanilla removes prison status as soon as Release is ordered,
                // LONG before the pawn crosses the map edge. Do not cancel
                // the group for a released pawn that is still physically here.
                if(r.Pawn==null || r.Pawn.Dead || r.Recruited ||
                    r.Pawn.IsSlaveOfColony ||
                    (!r.Pawn.Spawned && !r.GroupReleaseConfirmed))
                    bad.Add(r.GroupRansomId);
            }
            foreach(int id in bad)Abort(ledger,id,"participant no longer held");
        }

        // All physically returned members are still represented in the ledger
        // until payment. Dialogues can explicitly request a delivery retry when
        // no suitable drop cell existed BEFORE any transport pods were spawned.
        // The attempt flag blocks unsafe retries after a partial native failure.
        internal static bool HasPendingDelivery(Faction faction)
        {
            var ledger=Current.Game?.GetComponent<RHFGameLedger>();
            if(ledger?.Hostages==null || faction==null)return false;
            if(UncertainDeliveryIds(faction).Count>0)return true;
            foreach(RHFHostageRecord record in ledger.Hostages)
                if(record?.OriginalFaction==faction && record.GroupRansomId>0 &&
                   Ready(ledger,record.GroupRansomId,out RHFHostageRecord _))return true;
            return false;
        }

        internal static string DebugStatus()
        {
            RHFGameLedger ledger=Current.Game?.GetComponent<RHFGameLedger>();
            if(ledger?.Hostages==null)return "NJR: журнал заложников недоступен.";
            var output=new System.Text.StringBuilder();
            output.AppendLine("NJR: групповые договоры (только чтение)");
            var seen=new HashSet<int>();
            foreach(RHFHostageRecord r in ledger.Hostages)
            {
                if(r?.GroupRansomId<=0 || !seen.Add(r.GroupRansomId))continue;
                int released=0,found=0;
                bool uncertain=false;
                foreach(RHFHostageRecord member in ledger.Hostages)
                {
                    if(member?.GroupRansomId!=r.GroupRansomId)continue;
                    found++;
                    if(member.GroupReleaseConfirmed)released++;
                    if(member.GroupPaymentAttempted)uncertain=true;
                }
                output.Append("#").Append(r.GroupRansomId).Append(" ")
                    .Append(r.OriginalFaction?.Name??"?").Append(": ")
                    .Append(released).Append("/").Append(r.GroupRansomMembers)
                    .Append(" вышли, ").Append(r.GroupRansomSilver).Append(" серебра; ")
                    .Append(uncertain?"отправка НАЧИНАЛАСЬ, повтор запрещён":
                        released==r.GroupRansomMembers && found==r.GroupRansomMembers?
                        "готов к отправке при безопасной площадке":"ожидает освобождения")
                    .AppendLine();
                if(seen.Count>=32){output.AppendLine("... ограничение вывода 32 договора");break;}
            }
            if(seen.Count==0)output.AppendLine("Активных групповых договоров нет.");
            return output.ToString();
        }

        // A failed native skyfaller spawn may be partially successful. Show
        // that debt in the diplomacy UI, but never provide an unsafe retry.
        internal static List<int> UncertainDeliveryIds(Faction faction)
        {
            var result=new List<int>();
            var ledger=Current.Game?.GetComponent<RHFGameLedger>();
            if(ledger?.Hostages==null || faction==null)return result;
            foreach(RHFHostageRecord record in ledger.Hostages)
                if(record?.OriginalFaction==faction && record.GroupRansomId>0 &&
                   record.GroupPaymentAttempted &&
                   !result.Contains(record.GroupRansomId))
                    result.Add(record.GroupRansomId);
            return result;
        }

        internal static List<int> PendingDeliveryIds(Faction faction)
        {
            var result=new List<int>();
            var ledger=Current.Game?.GetComponent<RHFGameLedger>();
            if(ledger?.Hostages==null || faction==null)return result;
            foreach(RHFHostageRecord record in ledger.Hostages)
                if(record?.OriginalFaction==faction && record.GroupRansomId>0 &&
                   !result.Contains(record.GroupRansomId) &&
                   Ready(ledger,record.GroupRansomId,out RHFHostageRecord _))
                    result.Add(record.GroupRansomId);
            return result;
        }

        private static bool Ready(RHFGameLedger ledger,int id,
            out RHFHostageRecord first)
        {
            first=null;
            if(ledger?.Hostages==null || id<=0)return false;
            int seen=0;
            foreach(RHFHostageRecord member in ledger.Hostages)
            {
                if(member?.GroupRansomId!=id)continue;
                if(first==null)first=member;
                if(member.GroupRansomMembers<2 || member.GroupRansomMembers>MaximumGroup ||
                   member.GroupPaymentAttempted || !member.GroupReleaseConfirmed ||
                   member.GroupRansomSilver<1 ||
                   member.GroupRansomMembers!=first.GroupRansomMembers ||
                   member.GroupRansomSilver!=first.GroupRansomSilver ||
                   member.OriginalFaction!=first.OriginalFaction ||
                   member.GroupOriginMapId!=first.GroupOriginMapId)
                    return false;
                seen++;
            }
            return first!=null && seen==first.GroupRansomMembers;
        }

        internal static string RequestDelivery(Faction faction,Map destination,int id)
        {
            RHFGameLedger ledger=Current.Game?.GetComponent<RHFGameLedger>();
            if(!Ready(ledger,id,out RHFHostageRecord first) ||
               first.OriginalFaction!=faction)
                return "Договор уже оплачен, нарушен или его доставка была начата; повтор запрещён.";
            if(destination==null || destination.Parent==null ||
               (!string.IsNullOrEmpty(first.GroupOriginMapId) &&
                first.GroupOriginMapId!=destination.Parent.GetUniqueLoadID()))
                return "Доставку можно запросить только с карты поселения, где был заключён договор.";
            if(TryDeliver(ledger,id,destination))
                return "Капсулы с "+first.GroupRansomSilver+" серебра отправлены к поселению.";
            return "Место для доставки пока не найдено. Договор сохранён, деньги не списаны. " +
                "Освободите площадку и повторите запрос позже.";
        }

        private static bool TryDeliver(RHFGameLedger ledger,int id,Map destination)
        {
            if(!Ready(ledger,id,out RHFHostageRecord first) ||
               destination==null || destination.Parent==null)return false;
            string siteId=destination.Parent.GetUniqueLoadID();
            // R001 save compatibility: historical contracts did not persist the
            // origin. On the physical last exit, adopt that actual event map.
            if(!string.IsNullOrEmpty(first.GroupOriginMapId) &&
               first.GroupOriginMapId!=siteId)return false;
            IntVec3 landing=RHFHostageDiplomacy.SafeRansomDrop(destination);
            if(!landing.IsValid || !landing.InBounds(destination))
            {
                Log.Warning("[NJR][GroupRansom] #"+id+" pending: no clear landing cell");
                return false; // absolutely no payout attempt was made
            }
            int silverCount=first.GroupRansomSilver;
            int members=first.GroupRansomMembers;
            var cargo=new List<Thing>();
            for(int left=silverCount;left>0;)
            {
                Thing silver=ThingMaker.MakeThing(ThingDefOf.Silver);
                int count=Math.Min(left,silver.def.stackLimit);
                silver.stackCount=count;
                cargo.Add(silver);
                left-=count;
            }
            // Once the native spawn call begins, we do NOT attempt another
            // payment automatically: DropThingsNear may have placed some pods
            // before throwing, and replaying the batch would duplicate silver.
            foreach(RHFHostageRecord member in ledger.Hostages)
                if(member?.GroupRansomId==id)
                {
                    member.GroupOriginMapId=siteId;
                    member.GroupPaymentAttempted=true;
                }
            try { DropPodUtility.DropThingsNear(landing,destination,cargo); }
            catch(Exception e)
            {
                Log.Error("[NJR][GroupRansom] #"+id+
                    " native delivery uncertain; retry BLOCKED to prevent duplicates: "+e);
                Messages.Message("NJR: возможна частичная доставка группового выкупа. " +
                    "Автоповтор отключён во избежание удвоения серебра. См. Player.log.",
                    MessageTypeDefOf.RejectInput,true);
                foreach(Thing t in cargo)
                    if(t!=null && !t.Destroyed && !t.Spawned && t.ParentHolder==null)
                        t.Destroy(DestroyMode.Vanish);
                return false;
            }
            for(int i=ledger.Hostages.Count-1;i>=0;i--)
                if(ledger.Hostages[i]?.GroupRansomId==id)
                {ledger.Hostages[i].Paid=true;ledger.Hostages.RemoveAt(i);
                 ledger.InvalidateHostageIndex();}
            try
            {
                Find.LetterStack.ReceiveLetter("NJR: групповой выкуп",
                    members+" пленников освобождены. "+silverCount+
                    " серебра отправлены транспортными капсулами.",
                    LetterDefOf.PositiveEvent,new LookTargets(landing,destination));
            }
            catch(Exception e)
            {Log.Warning("[NJR][GroupRansom] Delivery succeeded; letter failed: "+e);}
            return true;
        }

        // True means the event belonged to a group. It suppresses the old
        // individual ransom/mercy path and never grants a premature payout.
        internal static bool OnPrisonerExit(RHFGameLedger ledger,
            RHFHostageRecord r,Map previousMap,bool released,Pawn pawn)
        {
            if(r==null || r.GroupRansomId<=0)return false;
            int id=r.GroupRansomId;
            if(!released || pawn==null || pawn.Dead || previousMap==null)
            {Abort(ledger,id,"handover was not a live release");return true;}
            if(r.GroupReleaseConfirmed)return true;
            r.GroupReleaseConfirmed=true;
            int secret=Math.Max(0,r.CrueltyScore-r.DisclosedCrueltyScore);
            if(secret>0)
                RHFPoliticalReputation.Change(r.OriginalFaction,
                    -Math.Min(95,secret*(6+r.Importance*3)));
            // Never try to re-send for one recipient before the LAST member
            // actually crossed a map edge. Completed delivery survives save/load.
            if(Ready(ledger,id,out RHFHostageRecord first))
            {
                if(!TryDeliver(ledger,id,previousMap))
                {
                    bool uncertain=false;
                    foreach(RHFHostageRecord member in ledger.Hostages)
                        if(member?.GroupRansomId==id && member.GroupPaymentAttempted)
                        {uncertain=true;break;}
                    Messages.Message(uncertain ?
                        "NJR: групповая передача завершена; доставка не подтверждена. " +
                            "Автоповтор запрещён. Проверьте Player.log." :
                        "NJR: групповая передача завершена. Нет площадки для оплаты; " +
                            "после её расчистки повторите запрос через консоль связи.",
                        uncertain?MessageTypeDefOf.RejectInput:MessageTypeDefOf.NeutralEvent,true);
                }
            }
            return true;
        }
    }
}
