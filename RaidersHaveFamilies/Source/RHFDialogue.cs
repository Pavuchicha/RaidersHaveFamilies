// Hostage discussions are part of a REAL Faction.TryOpenComms call:
// the negotiator first walks to the console; the standard Dialog_Negotiation then
// calls FactionDialogMaker.FactionDialogFor, to whose node RHF appends a branch.
// All text, family lookups and chance rolls occur only during actual player dialogue.
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RaidersHaveFamilies
{
    public static class RHFDialogue
    {
        private static readonly string Title = "Переговоры о пленниках";

        public static bool HasCaptives(Faction faction)
        {
            if (faction == null || Current.Game == null) return false;
            RHFGameLedger ledger = Current.Game.GetComponent<RHFGameLedger>();
            if (ledger?.Hostages == null) return false;
            foreach (RHFHostageRecord record in ledger.Hostages)
                if (record?.OriginalFaction == faction && !record.Paid &&
                    RHFHostageDiplomacy.HostageEligible(record.Pawn)) return true;
            return false;
        }

        public static DiaNode Captives(Pawn negotiator, Faction faction)
        {
            var node = new DiaNode("На другой стороне линии слышно, как советник передаёт кому-то список имён. " +
                "Представитель «" + faction.Name + "» возвращается к микрофону.\n\n" +
                "«Мы готовы разговаривать только о тех, кого вы действительно удерживаете. Назовите имя»." );
            RHFGameLedger ledger = Current.Game?.GetComponent<RHFGameLedger>();
            int count = 0;
            if (ledger != null)
                foreach (RHFHostageRecord r in ledger.Hostages)
                {
                    if (r?.OriginalFaction != faction || r.Paid ||
                        !RHFHostageDiplomacy.HostageEligible(r.Pawn)) continue;
                    RHFHostageRecord selected = r;
                    node.options.Add(new DiaOption(r.Pawn.LabelShortCap + " — " +
                        RHFRankResolver.RankLabel(r.Importance))
                    { linkLateBind = () => Person(negotiator, selected) });
                    count++;
                    if (count == 24) break;
                }
            if (count == 0) node.text = "«Вы утверждаете, что держите наших людей, но мы не можем подтвердить ни одного имени».";
            node.options.Add(new DiaOption("Вернуться к обычным переговорам")
            { linkLateBind = () => FactionDialogMaker.FactionDialogFor(negotiator, faction) });
            return node;
        }

        private static string Contact(RHFHostageRecord r)
        {
            if (r?.Pawn?.relations != null)
                foreach (Pawn relative in r.Pawn.relations.RelatedPawns)
                    if (relative != null && relative.Faction == r.OriginalFaction && !relative.Dead)
                        return "Родственник " + relative.LabelShortCap + " находится на линии вместе с представителем фракции";
            if (r != null && r.Importance >= 5)
                return "На линии официальный представитель совета фракции";
            return "На линии уполномоченный представитель фракции";
        }

        private static DiaNode Person(Pawn negotiator, RHFHostageRecord r)
        {
            string introduction = Contact(r) + ".\n\n";
            introduction += "Сводное здоровье пленника: " +
                Mathf.RoundToInt((r.Pawn.health?.summaryHealth?.SummaryHealthPercent ?? 1f)*100f) +
                "% (общая оценка состояния здоровья игрой).\n" +
                "Ранее отвергнутых повышенных требований: " + r.HighDemandRejected +
                "; заявлений об отказе: " + r.RefusalCount + ".\n\n";
            if (r.Importance >= 6) introduction +=
                "«Не думайте, что речь идёт только о деньгах. Вы удерживаете человека, " +
                "от которого зависят люди и решения. Мы требуем ясного ответа».";
            else if (r.CrueltyScore > 0) introduction +=
                "«До нас дошли сведения, что пленник пострадал у вас в заключении. " +
                "Я хочу услышать это от вас, прежде чем мы будем говорить о цене».";
            else introduction +=
                "«Скажите, " + r.Pawn.LabelShortCap + " жив? В каких условиях он содержится? " +
                "Пожалуйста, без угроз. Мы и так едва держим себя в руках».";
            DiaNode node = new DiaNode(introduction + "\n\n" +
                "Текущее состояние: " + Mathf.RoundToInt((r.Pawn.health?.summaryHealth?.SummaryHealthPercent ?? 1f)*100f) +
                "%. Значимость для фракции: " + r.Importance + "/8.");
            node.options.Add(new DiaOption("Подтвердить, что пленник жив, и сообщить о его состоянии")
            { linkLateBind = () => ConfirmAlive(negotiator, r) });
            node.options.Add(new DiaOption("Обсудить условия возвращения")
            { linkLateBind = () => Terms(negotiator, r) });
            DiaOption report = new DiaOption("Сообщить о ранениях и проведённых операциях")
            { linkLateBind = () => HealthReport(negotiator, r) };
            if (!RHFHostageDiplomacy.HasNewEvidence(r))
                report.Disable("новых сведений пока нет");
            node.options.Add(report);
            if (r.Importance >= 3)
                node.options.Add(new DiaOption("Заявить, что пленника не вернут")
                { linkLateBind = () => Deny(negotiator, r) });
            node.options.Add(new DiaOption("К списку пленников")
            { linkLateBind = () => Captives(negotiator, r.OriginalFaction) });
            return node;
        }

        private static DiaNode ConfirmAlive(Pawn negotiator, RHFHostageRecord r)
        {
            if (!RHFHostageDiplomacy.HostageEligible(r.Pawn))
                return ReturnNode("«Мы не можем подтвердить, что он по-прежнему у вас. Разговор окончен»." , negotiator, r);
            string tone;
            float health = r.Pawn.health?.summaryHealth?.SummaryHealthPercent ?? 1f;
            if (health < .35f)
                tone = "«Так плохо? Вы должны немедленно обеспечить ему врача. У нас нет времени ждать нового разговора».";
            else if (r.CrueltyScore > 0)
                tone = "«Значит, жив. Но это ещё не ответ на вопрос, что именно вы с ним делали. Мы вернёмся к этому».";
            else tone = "«Спасибо хотя бы за это. Передайте ему, что о нём помнят. Нам нужны гарантии его возвращения».";
            return ReturnNode(tone + "\n\nСведения о жизни переданы без требования выкупа и без изменения репутации.", negotiator, r);
        }

        private static DiaNode Terms(Pawn negotiator, RHFHostageRecord r)
        {
            if (r.Negotiated)
                return ReturnNode("«У нас уже есть договорённость. Отпустите пленника; только затем будет произведена оплата»." , negotiator, r);
            int basePrice = RHFHostageDiplomacy.NegotiatedPrice(r, negotiator);
            int higher = Mathf.RoundToInt(basePrice * 1.35f);
            int extreme = Mathf.RoundToInt(basePrice * 1.80f);
            int goodwill = RHFHostageDiplomacy.GoodwillReward(r);
            string goodwillText = r.OriginalFaction.def.permanentEnemy ?
                "Постоянная вражда: goodwill останется прежним; снизится внутреннее напряжение" :
                "Ожидаемое улучшение отношений: до +" + goodwill + " (предел +100)";
            DiaNode node = new DiaNode(Contact(r) + ".\n\n" +
                "«Назовите условия. Мы выслушаем их, но не путайте готовность платить с покорностью».\n\n" +
                "Суммы указаны в серебре; шанс принятия требования приведён отдельно. " +
                "Цена учитывает здоровье пленника, известные жестокости, историю переговоров " +
                "и социальный навык переговорщика (" + RHFHostageDiplomacy.NegotiatorSocial(negotiator) + ")." +
                (r.RefusalCount > 0 ? "\n«Вы уже заявляли, что не вернёте его. " +
                    "Мы помним это. Что изменилось?»" : ""));
            node.options.Add(new DiaOption("Стандартный выкуп — " + basePrice + " серебра")
            { linkLateBind = () => Agreement(negotiator, r, basePrice, false) });
            node.options.Add(new DiaOption("Повышенное требование — " + higher + " серебра (примерный шанс " +
                RHFHostageDiplomacy.CounterOfferChancePercent(r,1.35f, negotiator) + "%)")
            { linkLateBind = () => Higher(negotiator, r, 1.35f) });
            node.options.Add(new DiaOption("Ультиматум — " + extreme + " серебра (примерный шанс " +
                RHFHostageDiplomacy.CounterOfferChancePercent(r,1.80f, negotiator) + "%)")
            { linkLateBind = () => Higher(negotiator, r, 1.80f) });
            node.options.Add(new DiaOption("Освободить бесплатно — " + goodwillText)
            { linkLateBind = () => Agreement(negotiator, r, 0, true) });
            node.options.Add(new DiaOption("Вернуться к разговору")
            { linkLateBind = () => Person(negotiator, r) });
            return node;
        }

        private static DiaNode Agreement(Pawn negotiator, RHFHostageRecord r, int silver, bool goodwill)
        {
            RHFHostageDiplomacy.AgreeRelease(r, silver, goodwill);
            string answer = goodwill ?
                "«Вы отпускаете его без платы? Я передам это семье и совету. Мы не забудем, что вы дали ему уйти»." :
                "«Мы согласны на " + silver + " серебра. Но сначала он должен покинуть ваши стены живым. " +
                "Только тогда получите деньги».";
            return ReturnNode(answer, negotiator, r);
        }

        private static DiaNode Higher(Pawn negotiator, RHFHostageRecord r, float factor)
        {
            bool accepted = RHFHostageDiplomacy.RequestHigherRansom(r, factor, negotiator);
            int price = Mathf.RoundToInt(RHFHostageDiplomacy.NegotiatedPrice(r,negotiator) * factor);
            return ReturnNode(accepted ?
                "После паузы, когда на линии слышен спор нескольких голосов, представитель отвечает: " +
                "«Мы приняли решение. " + price + " серебра. Верните его живым»." :
                "«Нет. Вы превратили переговоры в вымогательство. Совет не согласен. " +
                "Не думайте, что после этого разговора ничего не изменится»." , negotiator, r);
        }

        private static DiaNode HealthReport(Pawn negotiator, RHFHostageRecord r)
        {
            RHFHostageDiplomacy.AuditInteractionMutilation(r);
            int undisclosed = Math.Max(0, r.WceOperations - r.DisclosedOperations);
            int missing = Math.Max(0, RHFHostageDiplomacy.MissingParts(r.Pawn) - r.LastReportedMissing);
            DiaNode node = new DiaNode(Contact(r) + ".\n\n«Я записываю. Говорите точно: " +
                "какие ранения были получены до плена, а какие операции провели вы сами?»\n\n" +
                (undisclosed > 0 ? "Подтверждённое хирургическое воздействие: " +
                    r.LastConfirmedOperation + ". Новых зарегистрированных операций: " + undisclosed + "." :
                    "Новых документированных умышленных операций в журнале RHF нет.") +
                (missing > 0 ? "\nНовых отсутствующих частей тела: " + missing + "." : ""));
            if (RHFHostageDiplomacy.CanAdmitMutilation(r))
                node.options.Add(new DiaOption("Признать: части тела намеренно удалены по нашему приказу")
                { linkLateBind = () => ReturnNode(RHFHostageDiplomacy.AdmitMutilation(r), negotiator, r) });
            if (RHFHostageDiplomacy.HasNewEvidence(r))
                node.options.Add(new DiaOption("Передать медицинские сведения без признания умысла")
                { linkLateBind = () => ReportResponse(negotiator, r) });
            node.options.Add(new DiaOption("Вернуться к разговору")
            { linkLateBind = () => Person(negotiator, r) });
            return node;
        }

        private static DiaNode ReportResponse(Pawn negotiator, RHFHostageRecord r)
        {
            string reply = RHFHostageDiplomacy.ReportCondition(r);
            return ReturnNode(reply, negotiator, r);
        }

        private static DiaNode Deny(Pawn negotiator, RHFHostageRecord r)
        {
            DiaNode n = new DiaNode(r.RefusalCount > 0 ?
                "«Вы уже клялись удерживать пленника навсегда. И теперь повторяете это? " +
                "Каждый новый отказ будет рассматриваться как отдельная угроза»." :
                "«Вы отказываетесь вернуть его? Вы уверены, что хотите сказать это официально? " +
                "Я передам каждое слово командованию».\n\nОтказ может вызвать попытку спасения или карательную операцию.");
            n.options.Add(new DiaOption("Подтвердить отказ")
            { linkLateBind = () => { RHFHostageDiplomacy.Refuse(r);
                return ReturnNode(r.RefusalCount > 1 ?
                    "«Мы слышали это уже не раз. Повторения не делают вашу угрозу убедительнее. " +
                    "Совет будет действовать исходя из того, что вы удерживаете нашего человека силой»." :
                    "«Мы услышали. Совет получит запись разговора. Решение принято»." , negotiator, r); } });
            n.options.Add(new DiaOption("Не отказываться; продолжить переговоры")
            { linkLateBind = () => Person(negotiator,r) });
            return n;
        }

        private static DiaNode ReturnNode(string text, Pawn negotiator, RHFHostageRecord r)
        {
            DiaNode n = new DiaNode(text);
            n.options.Add(new DiaOption("Продолжить разговор о пленнике")
            { linkLateBind = () => Person(negotiator, r) });
            n.options.Add(new DiaOption("Вернуться к обычным переговорам")
            { linkLateBind = () => FactionDialogMaker.FactionDialogFor(negotiator, r.OriginalFaction) });
            return n;
        }
    }
}
