// A rank describes why a pawn matters to its faction, without changing pawn generation.
// New factions and pawn kinds participate through shared metadata and localized text.
using System;
using RimWorld;
using Verse;

namespace RaidersHaveFamilies
{
    public static class RHFRankResolver
    {
        // 0 ordinary, 1 established, 2 professional, 3 local notable,
        // 4 senior officer/religious official, 5 city/regional authority,
        // 6 national dignitary, 7 high command, 8 faction ruler.
        public static int RankOf(Pawn pawn)
        {
            if (pawn == null) return 0;
            if (pawn.Faction != null && pawn.Faction.leader == pawn) return 8;
            if (pawn.kindDef != null && pawn.kindDef.factionLeader) return 8;
            // Royalty: Imperial nobles already have a real title hierarchy.
            // Use their actual title's seniority rather than inventing a rank
            // from a generic pawn-kind label (and NEVER award a title here).
            if (ModsConfig.RoyaltyActive && pawn.royalty != null && pawn.Faction != null)
            {
                RoyalTitleDef title = pawn.royalty.GetCurrentTitle(pawn.Faction);
                if (title != null && title.seniority > 0)
                    return Math.Max(1,Math.Min(7,title.seniority));
            }
            string n = ((pawn.kindDef?.defName ?? "") + " " +
                (pawn.kindDef?.label ?? "") + " " + (pawn.story?.title ?? "")).ToLowerInvariant();
            // English and Russian labels are recognized. DefName and the actual
            // Faction.leader reference are preferred over transient job titles.
            if (Has(n,"emperor","empress","monarch","king","queen","high king","supreme leader","archotech ruler",
                "император","императрица","монарх","король","королева","верховный правитель")) return 8;
            if (Has(n,"field marshal","grand general","high commander","warlord","admiral","high priest","archbishop","archon","supreme commander",
                "фельдмаршал","главнокомандующий","адмирал","верховный жрец","архиепископ")) return 7;
            if (Has(n,"governor","senator","ambassador","minister","duke","duchess","general","regional leader","overseer",
                "губернатор","сенатор","посол","министр","герцог","герцогиня","генерал")) return 6;
            if (Has(n,"mayor","chief magistrate","councilor","councillor","baron","baroness","viscount","colonel","commander","bishop",
                "мэр","барон","баронесса","полковник","командир","епископ")) return 5;
            if (Has(n,"captain","lieutenant colonel","elder","chieftain","priest","abbot","noble","officer","war chief","marshal","prefect",
                "капитан","старейшина","вождь","жрец","офицер","префект")) return 4;
            if (Has(n,"merchant lord","trader chief","headman","shaman","tribal leader","master artisan","sergeant","lieutenant","knight","doctor","scientist",
                "староста","шаман","сержант","лейтенант","рыцарь","врач","учёный")) return 3;
            if (Has(n,"elite","veteran","specialist","heavy","royal guard","bodyguard","sniper","engineer","handler","researcher","merchant",
                "элита","ветеран","специалист","телохранитель","снайпер","инженер","исследователь")) return 2;
            if (Has(n,"guard","warrior","soldier","trader","hunter","scout","farmer","settler","villager",
                "страж","воин","солдат","торговец","охотник","разведчик","фермер","поселенец")) return 1;
            return 0;
        }
        private static bool Has(string haystack, params string[] tokens)
        {
            for (int i = 0; i < tokens.Length; i++)
            {
                // Token boundaries matter: e.g. "viking" must not be
                // mistaken for a king by substring matching "king".
                string word = tokens[i];
                int pos = 0;
                while ((pos = haystack.IndexOf(word, pos, StringComparison.Ordinal)) >= 0)
                {
                    int end = pos + word.Length;
                    bool left = pos == 0 || !char.IsLetterOrDigit(haystack[pos - 1]);
                    bool right = end == haystack.Length || !char.IsLetterOrDigit(haystack[end]);
                    if (left && right) return true;
                    pos++;
                }
            }
            return false;
        }
        // This is a real descriptive role, distinct from the numeric importance score.
        // For an actual faction leader, report the verified leadership fact. Other titles
        // are descriptions of available game metadata or explicit DEBUG appointments.
        public static string OfficeFor(Pawn pawn, int rank)
        {
            if (pawn?.Faction != null && pawn.Faction.leader == pawn)
                return "глава фракции";
            if (ModsConfig.RoyaltyActive && pawn?.royalty != null && pawn.Faction != null)
            {
                RoyalTitleDef actual = pawn.royalty.GetCurrentTitle(pawn.Faction);
                if (actual != null)
                    return "имперский титул: " +
                        (pawn.gender == Gender.Female && !string.IsNullOrEmpty(actual.labelFemale) ?
                         actual.labelFemale : actual.label);
            }
            string title = pawn?.story?.title;
            if (!string.IsNullOrEmpty(title) && RankOf(pawn) >= rank && rank >= 2)
                return title;
            return RHFOfficeCatalog.Office(pawn,rank);
        }

        public static string OfficeStory(Pawn pawn, int rank, string office, bool debug)
        {
            string role = string.IsNullOrEmpty(office) ? OfficeFor(pawn, rank) : office;
            string scope;
            switch (rank)
            {
                case 0: scope = "Не занимает известной RHF должности. Его выкуп зависит прежде всего от семьи, здоровья и отношений с фракцией."; break;
                case 1: scope = "Известен небольшой местной общине; личная потеря заметна родственникам и соседям, но вряд ли меняет управление фракцией."; break;
                case 2: scope = "Имеет ценный опыт и связи среди коллег. Потеря такого специалиста ослабляет отдельную группу или мастерскую."; break;
                case 3: scope = "Может руководить небольшим поселением, отрядом или ремесленным объединением. За его возвращение способны поручиться местные семьи и сослуживцы."; break;
                case 4: scope = "Отвечает за людей, общинные решения или гарнизон. При угрозе его жизни к родственникам подключаются местные власти и командование."; break;
                case 5: scope = "Выполняет административные или командные обязанности на уровне города и соседних поселений; влияет на распределение сил и припасов."; break;
                case 6: scope = "Региональная должность: координирует склады, маршруты и поставки для нескольких поселений, либо распоряжается местными администрациями. Изоляция такого человека срывает договоры и снабжение, поэтому фракция готова платить крупные суммы и рисковать солдатами."; break;
                case 7: scope = "Управляет крупными воинскими соединениями или политическими объединениями. Решения такого лица затрагивают целые районы и стратегические операции."; break;
                default: scope = "Лицо общегосударственного масштаба: его пленение затрагивает власть фракции и может вызвать организованное вмешательство. Этот рейтинг не меняет фактического лидера, назначенного самой игрой."; break;
            }
            string duty = RHFOfficeCatalog.Responsibility(role,rank);
            string imperial = ModsConfig.RoyaltyActive && pawn?.royalty != null && pawn.Faction != null &&
                pawn.royalty.GetCurrentTitle(pawn.Faction) != null ?
                "\nТитул подтверждён Royalty. Имперская иерархия имеет собственные требования, привилегии и наследование; RHF только переводит её в шкалу дипломатической важности." : "";
            return "Должность: " + role + ".\n" + duty + "\n" + scope + imperial +
                (debug ? "\nДолжность назначена командой разработчика для проверки сценария: это не замена действующего лидера фракции." :
                "\nНазначение оценено по доступным игровым данным и роли пешки; не является самостоятельной карьерной симуляцией.");
        }

        // Never pretend that a test pawn is the real Faction.leader.
        public static string AssignTestOffice(Pawn pawn, int rank)
        {
            string office = OfficeFor(pawn, rank);
            if (pawn?.story != null && rank >= 2)
                pawn.story.title = office;
            return office;
        }

        public static float RansomMultiplier(int rank)
        {
            switch(Math.Max(0,Math.Min(8,rank)))
            {
                case 1:return 1.35f; case 2:return 1.85f; case 3:return 2.7f;
                case 4:return 4f; case 5:return 6.5f; case 6:return 10f;
                case 7:return 16f; case 8:return 25f;
                default:return 1f;
            }
        }
        public static string RankLabel(int rank)
        {
            switch(rank)
            {
                case 1:return "местно известная персона";
                case 2:return "ценный специалист";
                case 3:return "местная влиятельная фигура";
                case 4:return "высокопоставленная фигура";
                case 5:return "руководящий деятель";
                case 6:return "региональный руководитель";
                case 7:return "член высшего руководства";
                case 8:return "глава фракции";
                default:return "обычный член фракции";
            }
        }
    }
}
