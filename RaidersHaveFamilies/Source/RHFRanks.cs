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
                case 1:return "известный местный житель";
                case 2:return "ценный профессионал или ветеран";
                case 3:return "местная знаменитость, мастер или младший командир";
                case 4:return "старший офицер, старейшина или духовное лицо";
                case 5:return "власть города, высокое дворянство или командование";
                case 6:return "региональный правитель или государственный сановник";
                case 7:return "верховное командование или высшая знать";
                case 8:return "глава фракции";
                default:return "обычный гражданин или рядовой";
            }
        }
    }
}
