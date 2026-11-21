// RHF v0.11: live, faction-/kin-specific conversations after a REAL comms-console job.
// Speaker knowledge is bounded by public disclosures and the captive's identity.
// Diagnostic state is always in a separate paragraph from speech.
using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace RaidersHaveFamilies
{
    public static class RHFDialogue
    {
        public static bool HasCaptives(Faction faction)
        {
            if (faction == null || Current.Game == null) return false;
            var ledger = Current.Game.GetComponent<RHFGameLedger>();
            if (ledger?.Hostages == null) return false;
            foreach (RHFHostageRecord r in ledger.Hostages)
                if (r?.OriginalFaction == faction && !r.Paid &&
                    (RHFHostageDiplomacy.HostageEligible(r.Pawn) ||
                     (RHFHostageDiplomacy.IsDeadCase(r) && !r.DeathReportMade) ||
                     (r.Recruited && (!r.RecruitmentReported ||
                      (r.IdeologyConverted && !r.IdeologyReported))))) return true;
            return false;
        }

        private static string Choose(RHFHostageRecord r, int scene, params string[] texts)
        {
            if (texts == null || texts.Length == 0) return "";
            int seed = ((r?.Pawn?.thingIDNumber ?? r?.CaptureTick ?? 0) & int.MaxValue);
            seed = (seed + scene * 19 + (r?.HighDemandRejected ?? 0) * 3 +
                    (r?.RefusalCount ?? 0) * 7) & int.MaxValue;
            return texts[seed % texts.Length];
        }

        private static string Official(RHFHostageRecord r)
        {
            if (r?.Importance >= 7) return "Канцелярия высшего руководства фракции «" + r.OriginalFaction.Name + "»";
            if (r?.Importance >= 5) return "Уполномоченный совета фракции «" + r.OriginalFaction.Name + "»";
            if (r?.Importance >= 3) return "Офицер связи фракции «" + r.OriginalFaction.Name + "»";
            return "Представитель общины из фракции «" + r?.OriginalFaction?.Name + "»";
        }

        private static Pawn KnownRelative(RHFHostageRecord r)
        {
            if (r?.Pawn?.relations == null) return null;
            foreach (Pawn relative in r.Pawn.relations.RelatedPawns)
                if (relative != null && relative != r.Pawn && !relative.Dead &&
                    relative.Faction == r.OriginalFaction) return relative;
            return null;
        }

        private static string Info(string facts)
        {
            return string.IsNullOrEmpty(facts) ? "" : "\n\n[Сведения RHF]\n" + facts;
        }

        private static string CurrentFacts(RHFHostageRecord r)
        {
            float h = r.Pawn?.health?.summaryHealth?.SummaryHealthPercent ?? 1f;
            string facts = "Состояние здоровья: " + Mathf.RoundToInt(h * 100f) +
                "%. Значимость: " + r.Importance + "/8 (" + r.OfficeName + ").";
            if (Prefs.DevMode)
                facts += "\n[Отладка переговоров] Отклонено высоких цен: " +
                    r.HighDemandRejected + "; заявлений об отказе: " + r.RefusalCount +
                    "; реакция фракции: " + ReactionName(r.NegotiationEscalation) + ".";
            return facts;
        }

        private static string ReactionName(int escalation)
        {
            return escalation == 1 ? "тревожные уступки" :
                escalation == 2 ? "карательная мобилизация" : "обычные переговоры";
        }

        public static DiaNode Captives(Pawn negotiator, Faction faction)
        {
            RHFGameLedger ledger = Current.Game?.GetComponent<RHFGameLedger>();
            int count = 0;
            var node = new DiaNode("В динамике трещит помеха. Человек на другом конце " +
                "линии сверяется со списком пропавших.\n\n" +
                "«Назовите имя. Мы не можем обсуждать судьбу каждого нашего " +
                "пропавшего по слухам. Мне нужны конкретные люди»." );
            if (ledger != null)
                foreach (RHFHostageRecord r in ledger.Hostages)
                {
                    if (r?.OriginalFaction != faction || r.Paid ||
                        !(RHFHostageDiplomacy.HostageEligible(r.Pawn) ||
                          (RHFHostageDiplomacy.IsDeadCase(r) && !r.DeathReportMade) ||
                          (r.Recruited && (!r.RecruitmentReported ||
                           (r.IdeologyConverted && !r.IdeologyReported))))) continue;
                    RHFHostageRecord selected=r;
                    node.options.Add(new DiaOption(
                        (RHFHostageDiplomacy.IsDeadCase(r) ? "† " : r.Recruited ? "★ " : "") +
                        (r.Pawn?.LabelShortCap ?? r.NameAtCapture) + " — " +
                        RHFRankResolver.RankLabel(r.Importance) +
                        (RHFHostageDiplomacy.IsDeadCase(r) ? " (числится пропавшим)" :
                            r.Recruited ? " (завербован в колонию)" : ""))
                    { linkLateBind = () => Person(negotiator, selected) });
                    if (++count >= 24) break;
                }
            if (count == 0) node.text="«По нашим сведениям, сейчас у вас нет людей, которых можно обсуждать по этому каналу».";
            node.options.Add(new DiaOption("Вернуться к обычным переговорам")
            { linkLateBind = () => FactionDialogMaker.FactionDialogFor(negotiator,faction) });
            return node;
        }

        private static DiaNode Person(Pawn negotiator, RHFHostageRecord r)
        {
            if (RHFHostageDiplomacy.IsDeadCase(r)) return DeadPerson(negotiator,r);
            if (r.Recruited) return RecruitedPerson(negotiator,r);
            string voice;
            if (r.RefusalCount >= 2)
                voice = Choose(r,1,
                    "«Вы уже не раз говорили, что он останется у вас навсегда. Я не собираюсь притворяться, будто это первый звонок. Что теперь изменилось?»",
                    "«Снова вы. В прошлый раз вы запретили даже говорить об освобождении. Не тратьте моё время на ещё одну угрозу»",
                    "«Я узнал ваш голос. Надеюсь, сегодня вы собираетесь обсуждать дело, а не повторять свой ультиматум»");
            else if (r.RefusalCount == 1)
                voice = Choose(r,2,
                    "«После вашего отказа мне пришлось объяснять семье, почему мы всё ещё ведём переговоры. Дайте хоть одну причину продолжать»",
                    "«Последние ваши слова записаны. Вы объявили, что не отдадите его. Я всё-таки пришёл к аппарату — может, вы передумали»");
            else if (r.DisclosedCrueltyScore >= 15)
                voice = Choose(r,3,
                    "«Врачи уже ознакомились с тем, что вы сообщили. Сейчас я хочу услышать, будет ли он освобождён, а не новые подробности»",
                    "«После вашего медицинского доклада у нас заседают два разных совета: один требует денег, другой — солдат. Решайте, с кем будете говорить дальше»");
            else if (r.Importance >= 6)
                voice = Choose(r,4,
                    "«От его решений зависят чужие жизни и договоры. Вы захватили не просто прохожего. Назовите ваши намерения»",
                    "«Глава службы требует ежедневного отчёта, а я могу дать ему только молчание. Скажите, в каком состоянии наш представитель»",
                    "«Мне приказано не торговаться без подтверждения, что наш человек жив. Начнём с этого»");
            else
                voice = Choose(r,5,
                    "«Его семья трижды приходила к управе. Мне нечего им сказать. Вы позволите ему вернуться домой?»",
                    "«Нам передали, что он у вас. Я не пришёл читать нравоучения — мне нужна ясность. Жив ли он?»",
                    "«Мы собрали деньги и поручителей. Не вынуждайте нас тратить следующий месяц на поиски тел»");
            string stage=Choose(r,6,
                "Щёлкает рычажок записи; собеседник берёт время, чтобы подобрать слова.",
                "В трубке слышно шуршание документов, затем кто-то отодвигает стул.",
                "Голос звучит устало; за ним слышна короткая перебранка с секретарём.");
            var node=new DiaNode(Official(r) + ". " + stage + "\n\n" + voice + Info(CurrentFacts(r)));
            node.options.Add(new DiaOption("Подтвердить, что пленник жив")
            { linkLateBind = () => ConfirmAlive(negotiator,r) });
            node.options.Add(new DiaOption("Обсудить цену и условия возвращения")
            { linkLateBind = () => Terms(negotiator,r) });
            DiaOption report=new DiaOption("Сообщить о ранениях и проведённых операциях")
            { linkLateBind = () => HealthReport(negotiator,r) };
            if (!RHFHostageDiplomacy.HasNewEvidence(r)) report.Disable("новых сведений пока нет");
            node.options.Add(report);
            node.options.Add(new DiaOption(r.RefusalCount == 0 ?
                "Официально заявить: мы не вернём пленника" :
                "Повторить заявление, что пленника не вернут")
            { linkLateBind = () => Deny(negotiator,r) });
            if (r.IdeologyConverted && !r.IdeologyReported &&
                r.OriginalIdeo!=null && r.ConvertedIdeo!=null)
                node.options.Add(new DiaOption("Сообщить об обращении пленника в другую идеологию")
                { linkLateBind=()=>ConversionReport(negotiator,r) });
            node.options.Add(new DiaOption("К списку пленников")
            { linkLateBind = () => Captives(negotiator,r.OriginalFaction) });
            return node;
        }

        private static DiaNode RecruitedPerson(Pawn negotiator,RHFHostageRecord r)
        {
            string speech=r.Importance>=6 ? Choose(r,50,
                "«Вы зовёте это вербовкой. Для нас это уход человека, которому доверяли государственные дела. Его имя уже на столе у командования»",
                "«Совет прислал меня не за обещаниями. Он хочет знать, почему наш представитель носит теперь ваши знаки»",
                "«Я не буду обвинять его заочно. Но если он служит вам, наши договоры перестают что-либо значить»") :
                Choose(r,51,
                "«Значит, он выбрал вас. Дома ещё ждут. Мне придётся объяснять им это самому»",
                "«Я думал, что мы будем обсуждать выкуп. Вместо этого вы говорите, что он больше не наш»",
                "«У нас нет права вернуть его силой по одному моему приказу. Но об этом узнают»");
            var node=new DiaNode(Official(r)+".\n\n"+speech+Info(
                "Вступление в колонию подтверждено. Значимость "+r.Importance+
                "/8. Вербовщик: "+r.RecruiterName+
                ". Месть назначена: "+(r.RecruitmentRevengeQueued?"да":"нет")+"."));
            if(!r.RecruitmentReported)
                node.options.Add(new DiaOption("Официально сообщить о вербовке бывшего пленника")
                {linkLateBind=()=>RecruitmentReport(negotiator,r)});
            if(r.IdeologyConverted && !r.IdeologyReported &&
                r.OriginalIdeo!=null && r.ConvertedIdeo!=null)
                node.options.Add(new DiaOption("Сообщить о переходе в другую идеологию")
                {linkLateBind=()=>ConversionReport(negotiator,r)});
            node.options.Add(new DiaOption("Вернуться к списку")
            {linkLateBind=()=>Captives(negotiator,r.OriginalFaction)});
            return node;
        }

        private static DiaNode RecruitmentReport(Pawn negotiator,RHFHostageRecord r)
        {
            string speech=r.Importance>=6 ? Choose(r,52,
                "«Если он действительно согласился добровольно, передайте ему: ответственность за прежние приказы не исчезла вместе с новой присягой»",
                "«Я передам совету точные слова. Не удивляйтесь, если следующим решением будет не обмен посланиями»") :
                Choose(r,53,
                "«Я сообщу его родным. Они заслужили услышать, что он жив и сделал выбор»",
                "«Не знаю, стоит ли сейчас его убеждать. Но домой он ещё сможет написать»");
            return ReturnNode(speech+Info(RHFHostageDiplomacy.ReportRecruitment(r)),negotiator,r);
        }

        private static DiaNode ConversionReport(Pawn negotiator,RHFHostageRecord r)
        {
            if(!r.IdeologyConverted || r.IdeologyReported || r.ConvertedIdeo==null ||
                r.OriginalIdeo==null) return ReturnNode("Новых сведений об обращении нет.",negotiator,r);
            string speech=r.Importance>=6 ? Choose(r,54,
                "«Вы изменили не только его убеждения. Для нас это сигнал о том, кого и зачем вы держите за решёткой. Совет будет обсуждать последствия»",
                "«Он вправе верить во что угодно. Но на политической должности даже частная вера становится государственным вопросом»") :
                Choose(r,55,
                "«И он теперь молится вместе с вами? Я не знаю, как сказать об этом семье. Они будут спрашивать, сам ли он так решил»",
                "«Для некоторых это радость. Для тех, кто знал его прежде, это похоже на потерю. Ваша колония ещё увидит обе реакции»");
            return ReturnNode(speech+Info(RHFHostageDiplomacy.ReportConversion(r)),negotiator,r);
        }

        private static DiaNode ConfirmAlive(Pawn negotiator,RHFHostageRecord r)
        {
            if (!RHFHostageDiplomacy.HostageEligible(r.Pawn))
                return ReturnNode("«Пока мы не можем даже подтвердить, что он у вас. Я свяжусь с теми, кто видел его последним».",negotiator,r);
            float health=r.Pawn.health?.summaryHealth?.SummaryHealthPercent??1f;
            string speech=health<.35f ? Choose(r,7,
                "«Так плох? Прекратите разговоры о цене и вызовите ему врача. Я пришлю свою медицинскую бригаду, если вы позволите»",
                "«Это уже не просто содержание пленного. Если он умрёт прежде, чем мы договоримся, кто будет отвечать?»") :
                r.DisclosedCrueltyScore>0 ? Choose(r,8,
                "«Рад слышать, что он жив. Это, впрочем, не отвечает на вопрос, почему ему пришлось пережить всё остальное»",
                "«Жив... Хорошо. Семье передам только это. Остальные сведения будут у комиссии»") :
                Choose(r,9,
                "«Передайте ему, что его разыскивают. Не обещайте сроков, просто передайте это»",
                "«Спасибо. Я знаю, как мало значит одно слово через чужой передатчик, но сегодня его услышат дома»",
                "«Теперь хотя бы понятно, за что мы боремся. Пожалуйста, не отказывайте ему во встрече с врачом»");
            return ReturnNode(speech+Info("Подтверждение жизни передано. Перевода серебра и изменения репутации нет."),negotiator,r);
        }

        private static DiaNode Terms(Pawn negotiator,RHFHostageRecord r)
        {
            if (r.Negotiated)
                return ReturnNode("«Договорённость действует. Сначала он пересекает границу вашей территории живым; затем вы получаете деньги».",negotiator,r);
            int basePrice=RHFHostageDiplomacy.NegotiatedPrice(r,negotiator);
            int higher=Mathf.RoundToInt(basePrice*1.35f);
            int extreme=Mathf.RoundToInt(basePrice*1.80f);
            int goodwill=RHFHostageDiplomacy.GoodwillReward(r);
            string speech;
            if (r.NegotiationEscalation==1)
                speech=Choose(r,10,
                    "«В прошлый раз мы отказали. После того, что вы сообщили, казначей разрешил увеличить сумму. Не принимайте это за доверие»",
                    "«Совет согласился открыть резервный фонд. Я выслушаю даже тяжёлое предложение, но хочу увидеть его живым»");
            else if (r.NegotiationEscalation==2)
                speech=Choose(r,11,
                    "«Вы отказались от прежних условий, а затем сообщили о новых увечьях. Штаб теперь ведёт собственную работу. Я ещё могу говорить, но деньги больше не решают всё»",
                    "«Часть совета считает, что наши прежние предложения были ошибкой. Я постараюсь удержать переговоры, хотя войска уже получили распоряжения»");
            else if (r.HighDemandRejected>0)
                speech=Choose(r,12,
                    "«Наш отказ не был приглашением требовать всё больше. Предлагайте реалистичную сумму — или назовите другой выход»",
                    "«Казначей подчеркнул вашу предыдущую цену красным. Прошу вас: не заставляйте меня снова возвращаться с тем же ответом»");
            else if (r.Importance>=6)
                speech=Choose(r,13,
                    "«Средства найдём. Но он должен вернуться с сохранённым здоровьем, иначе сам выкуп окажется бессмысленным»",
                    "«Это политический вопрос, а не обычная торговля. Я вправе предложить деньги и обмен, но решение о силе примут без меня»");
            else speech=Choose(r,14,
                "«У нас есть серебро. Не столько, сколько вы могли бы захотеть, но люди пожертвовали его добровольно»",
                "«Я обсудил это с семьёй и общиной. Назовите цену, которую действительно собираетесь принять»",
                "«Не буду делать вид, будто мы сильнее, чем есть. Мы попробуем выкупить его. Остальное зависит от вас»");
            string goodwillText=r.OriginalFaction.def.permanentEnemy ?
                "(обычная репутация останется -100; изменится отношение RHF)" :
                "(репутация до +"+goodwill+")";
            var node=new DiaNode(Official(r)+".\n\n"+speech+Info(
                "Гарантированная базовая цена: "+basePrice+" серебра. " +
                "Социальный навык переговорщика: "+RHFHostageDiplomacy.NegotiatorSocial(negotiator)+".\n"+
                "Предыдущее поведение: "+ReactionName(r.NegotiationEscalation)+
                ". Суммы и шансы уже учитывают историю отказов и состояние пленника."));
            node.options.Add(new DiaOption("Вернуть пленника за "+basePrice+" серебра")
            { linkLateBind=()=>Agreement(negotiator,r,basePrice,false) });
            node.options.Add(new DiaOption("Потребовать "+higher+" серебра; шанс "+
                RHFHostageDiplomacy.CounterOfferChancePercent(r,1.35f,negotiator)+"%")
            { linkLateBind=()=>Higher(negotiator,r,1.35f) });
            node.options.Add(new DiaOption("Потребовать "+extreme+" серебра; шанс "+
                RHFHostageDiplomacy.CounterOfferChancePercent(r,1.80f,negotiator)+"%")
            { linkLateBind=()=>Higher(negotiator,r,1.80f) });
            node.options.Add(new DiaOption("Отпустить без выкупа "+goodwillText)
            { linkLateBind=()=>Agreement(negotiator,r,0,true) });
            node.options.Add(new DiaOption("Вернуться к разговору")
            { linkLateBind=()=>Person(negotiator,r) });
            return node;
        }

        private static DiaNode Agreement(Pawn negotiator,RHFHostageRecord r,int silver,bool goodwill)
        {
            RHFHostageDiplomacy.AgreeRelease(r,silver,goodwill);
            string speech=goodwill ? Choose(r,15,
                "«Без платы? Что ж... Я скажу семье. Сегодня они впервые за долгое время будут спать спокойно»",
                "«Это неожиданно. Мы разберёмся с прежними обвинениями, но сам поступок я запомню»") :
                r.NegotiationEscalation==1 ?
                "«Мы перечислим "+silver+" серебра, как только он окажется за стенами. Надеюсь, на этом всё закончится»." :
                Choose(r,16,
                    "«Согласны. "+silver+" серебра после подтверждённого выхода. Ни монетой раньше — таков порядок»",
                    "«Я поставлю подпись под этой суммой: "+silver+". Теперь дайте ему уйти»");
            return ReturnNode(speech+Info("Договорённость принята. Выплата или изменение отношений — после фактического выхода пленника с карты."),negotiator,r);
        }

        private static DiaNode Higher(Pawn negotiator,RHFHostageRecord r,float factor)
        {
            int offered=Mathf.RoundToInt(RHFHostageDiplomacy.NegotiatedPrice(r,negotiator)*factor);
            bool accepted=RHFHostageDiplomacy.RequestHigherRansom(r,factor,negotiator);
            string speech=accepted ? Choose(r,17,
                "«В совете только что поспорили, но сумму "+offered+" утвердили. Надеюсь, вы понимаете цену этого решения»",
                "«Я получил разрешение. "+offered+" серебра. Вы больше не должны ему вредить»") :
                r.NegotiationEscalation==2 ? Choose(r,18,
                    "«Нет. Я больше не распоряжаюсь этими средствами. Мои полномочия заканчиваются там, где начинается военный приказ»",
                    "«Вы пытаетесь назначать цену после всего, что произошло? Совет отверг предложение. Следующий ответ может прийти не по радио»") :
                Choose(r,19,
                    "«Казначей отказал. Предложение слишком велико для нас. Давайте попробуем иначе»",
                    "«Этой суммы не будет. И не считайте очередной отказ признаком того, что мы забыли пленника»",
                    "«Я выслушал вас. Ответ — нет. Но связь не обрываю: у нас остаётся человек, которого нужно вернуть»");
            return ReturnNode(speech+Info("Предложено "+offered+" серебра. Ответ: "+
                (accepted?"принято, ожидается освобождение":"отказ, история переговоров обновлена")+"."),negotiator,r);
        }

        private static DiaNode HealthReport(Pawn negotiator,RHFHostageRecord r)
        {
            RHFHostageDiplomacy.AuditInteractionMutilation(r);
            int newOps=Math.Max(0,r.WceOperations-r.DisclosedOperations);
            int newMissing=Math.Max(0,RHFHostageDiplomacy.MissingParts(r.Pawn)-r.LastReportedMissing);
            string opening=r.Importance>=6 ? Choose(r,20,
                "«Говорите внимательно. Я хочу передать врачу точные сведения, а не слухи и угрозы»",
                "«Наш медицинский совет готов слушать. Начните с того, что делали после захвата»") :
                Choose(r,21,
                "«Я приготовил бумагу. Скажите, что с ним произошло — только не пытайтесь назвать умышленную жестокость лечением»",
                "«Минуту. Я включу запись. Теперь — что с его телом и кто был рядом?»");
            string clinician=string.IsNullOrEmpty(r.LastResponsibleActor) ?
                "устанавливается журналом" : r.LastResponsibleActor;
            string facts=(newOps>0 ?
                "Новых медицинских записей: "+newOps+"; последнее воздействие: "+
                r.LastConfirmedOperation+"; ответственный: "+clinician+"." :
                "Новых операций, уверенно определённых по рецептам: 0. " +
                "Событий, восстановленных через медицинскую запись: "+r.RecoveredMedicalEvents+".")+
                "\nОтсутствующих частей тела с прошлого отчёта: "+newMissing+". " +
                "Боевые ранения до плена не объявляются пыткой без доказательств.";
            var node=new DiaNode(Official(r)+".\n\n"+opening+Info(facts));
            Pawn family=KnownRelative(r);
            if(RHFHostageDiplomacy.CanAdmitMutilation(r))
            {
                node.options.Add(new DiaOption("Признать: мы сознательно искалечили пленника")
                { linkLateBind=()=>ReportAdmission(negotiator,r,false,null) });
                if(family!=null)
                {
                    Pawn kin=family;
                    node.options.Add(new DiaOption("Признаться родственнику "+kin.LabelShortCap+" лично")
                    { linkLateBind=()=>ReportAdmission(negotiator,r,true,kin) });
                }
            }
            if(RHFHostageDiplomacy.HasNewEvidence(r))
            {
                node.options.Add(new DiaOption("Передать официальный медицинский отчёт фракции")
                { linkLateBind=()=>ReportResponse(negotiator,r,false,null) });
                if(family!=null)
                {
                    Pawn kin=family;
                    node.options.Add(new DiaOption("Сообщить родственнику "+kin.LabelShortCap+" лично")
                    { linkLateBind=()=>ReportResponse(negotiator,r,true,kin) });
                }
            }
            node.options.Add(new DiaOption("Вернуться к обсуждению пленника")
            { linkLateBind=()=>Person(negotiator,r) });
            return node;
        }

        private static DiaNode ReportAdmission(Pawn negotiator,RHFHostageRecord r,bool family,Pawn kin)
        {
            string start="Вы: «Это сделали мы. Увечье нанесено по нашему приказу. " +
                "Это не последствие случайного боя и не несчастный случай».";
            string setting=family && kin!=null ?
                "После соединения с "+kin.LabelShortCap+" вы произносите признание без посредника." :
                "Оператор связи включает официальную запись признания.";
            return ReturnNode(setting+"\n\n"+start+"\n\n"+
                RHFHostageDiplomacy.AdmitMutilation(r,family),negotiator,r);
        }

        private static DiaNode ReportResponse(Pawn negotiator,RHFHostageRecord r,bool family,Pawn kin)
        {
            string setting=family && kin!=null ?
                "На линии появляется "+kin.LabelShortCap+". " +
                "Вместо канцелярских вопросов слышно дрожащий человеческий голос." :
                "Секретарь передаёт листы врачу и ставит подпись на расшифровке.";
            return ReturnNode(setting+"\n\n"+
                RHFHostageDiplomacy.ReportCondition(r,family),negotiator,r);
        }

        private static DiaNode Deny(Pawn negotiator,RHFHostageRecord r)
        {
            string speech=r.RefusalCount>0 ? Choose(r,22,
                "«Вы уже объявили это окончательным решением. Почему решили, что повторение произведёт на нас другое впечатление?»",
                "«Я помню ваши слова. Если вы их подтверждаете, разговор о выкупе будет ещё тяжелее. Но я не перестану звонить»",
                "«Совет понял вас с первого раза. Скажите лучше, почему вы снова решили угрожать нашим людям»") :
                Choose(r,23,
                    "«Постойте. Вы хотите объявить, что никакого возвращения не будет? Я обязан занести это в протокол»",
                    "«Если вы решите оставить его навсегда, не считайте, что нам останется только принять это»",
                    "«Я должен переспросить: это угроза или официальное решение вашей колонии?»");
            var node=new DiaNode(Official(r)+".\n\n"+speech+Info(
                "Каждое подтверждение повышает напряжение и может ускорить операцию, но не блокирует будущие переговоры."));
            node.options.Add(new DiaOption("Да. Официально подтвердить, что мы не вернём пленника")
            { linkLateBind=()=>ConfirmedRefusal(negotiator,r) });
            node.options.Add(new DiaOption("Нет. Я готов обсудить условия")
            { linkLateBind=()=>Terms(negotiator,r) });
            return node;
        }

        private static DiaNode ConfirmedRefusal(Pawn negotiator,RHFHostageRecord r)
        {
            RHFHostageDiplomacy.Refuse(r);
            string speech=r.RefusalCount>=3 ?
                "«Больше не повторяйте это по радио. Я отправлю расшифровку людям, которые умеют действовать без переговоров»." :
                r.RefusalCount==2 ?
                "«Второй отказ записан. Возможно, ваш следующий разговор будет уже не со мной»." :
                "«Я услышал вас. Семье я скажу правду, а командование получит стенограмму».";
            return ReturnNode(speech+Info("Отказов в деле: "+r.RefusalCount+
                ". Следующий разговор о выкупе по-прежнему возможен, но станет менее доверительным."),negotiator,r);
        }

        private static DiaNode DeadPerson(Pawn negotiator,RHFHostageRecord r)
        {
            string name=string.IsNullOrEmpty(r.NameAtCapture)?r.Pawn?.LabelShortCap??"пленный":r.NameAtCapture;
            string speech=r.Importance>=6 ? Choose(r,24,
                "«Последнее подтверждение жизни было давно. Совет требует от вас официального ответа: где наш представитель?»",
                "«Секретариат уже готовит комиссию. Если он погиб, скажите это прямо, чтобы никто не строил ложных надежд»") :
                Choose(r,25,
                    "«Мы всё ещё оставляем место за столом. Это глупо? Тогда скажите, где он»",
                    "«Мне звонили из семьи. Ещё вчера спрашивали, передавал ли он новости. Я не знал, что ответить»",
                    "«Я всё жду, что вы назовёте цену. Но теперь я опасаюсь услышать что-то другое»");
            string facts="Пленник умер. Причина: "+(r.Executed?"казнь":r.PlayerCausedDeath?"действия игрока":"не подтверждена")+
                ". Значимость "+r.Importance+"/8.";
            if (Prefs.DevMode)
                facts+="\n[Отладка] Разделка: "+r.BodyButchered+
                    "; непосредственно съеден труп: "+r.BodyConsumedDirectly+".";
            var node=new DiaNode(Official(r)+".\n\n"+speech+Info(facts));
            node.options.Add(new DiaOption("Сообщить о смерти: "+
                (r.Executed?"казнили":r.PlayerCausedDeath?"убили":"погиб"))
            { linkLateBind=()=>DeadResolution(negotiator,r,1) });
            if(!r.PlayerCausedDeath && !r.Executed)
                node.options.Add(new DiaOption("Признаться, что мы умышленно убили пленника")
                { linkLateBind=()=>DeadResolution(negotiator,r,3) });
            // Do NOT display even a greyed-out cannibalism option until the
            // game has actually recorded the physical evidence.
            if(RHFHostageDiplomacy.CanConfessCannibalism(r))
                node.options.Add(new DiaOption("Признаться в каннибализме (событие подтверждено)")
                { linkLateBind=()=>DeadResolution(negotiator,r,2) });
            node.options.Add(new DiaOption("Промолчать и закрыть дело RHF")
            { linkLateBind=()=>DeadResolution(negotiator,r,0) });
            node.options.Add(new DiaOption("Вернуться к списку")
            { linkLateBind=()=>Captives(negotiator,r.OriginalFaction) });
            return node;
        }

        private static DiaNode DeadResolution(Pawn negotiator,RHFHostageRecord r,int disposition)
        {
            string result=RHFHostageDiplomacy.ResolveDeadHostage(r,disposition);
            DiaNode node=new DiaNode(result);
            node.options.Add(new DiaOption("К оставшимся пленникам")
            { linkLateBind=()=>Captives(negotiator,r.OriginalFaction) });
            node.options.Add(new DiaOption("Завершить разговор")
            { linkLateBind=()=>FactionDialogMaker.FactionDialogFor(negotiator,r.OriginalFaction) });
            return node;
        }

        private static DiaNode ReturnNode(string text,Pawn negotiator,RHFHostageRecord r)
        {
            DiaNode node=new DiaNode(text);
            node.options.Add(new DiaOption("Продолжить разговор о пленнике")
            { linkLateBind=()=>Person(negotiator,r) });
            node.options.Add(new DiaOption("Вернуться к обычным переговорам")
            { linkLateBind=()=>FactionDialogMaker.FactionDialogFor(negotiator,r.OriginalFaction) });
            return node;
        }
    }
}
