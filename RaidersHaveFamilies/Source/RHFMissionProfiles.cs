// v0.12: compose mission plans from independently variable objectives/entry/exit.
// The resulting plan is stored with the operation and never re-rolled on load.
using System;
using RimWorld;
using Verse;
using UnityEngine;

namespace RaidersHaveFamilies
{
    public static class RHFMissionProfiles
    {
        public static void Configure(RHFOperation op,RHFHostageRecord cause)
        {
            if(op==null || cause==null)return;
            if(op.Type==(int)RHFOperationType.Vengeance)
            {
                op.AssaultAfterObjective=false;
                op.FortifyPrison=false;
                if(op.HuntTarget!=null)
                {
                    op.Doctrine=(int)RHFDoctrine.TargetedTraitorHunt;
                    // Political order is dual: pursue the named defector AND
                    // damage the settlement that accepted them. Distinct wings
                    // share the same total raid points, not extra free enemies.
                    op.AssaultAfterObjective=true;
                }
                else if(cause.Importance>=5 && cause.CrueltyScore>=16 && Rand.Chance(.58f))
                    op.Doctrine=(int)RHFDoctrine.SiegeAndRaze;
                else
                    op.Doctrine=(int)RHFDoctrine.PunitiveAssault;
                return;
            }
            bool advanced=op.Vip!=null && op.SourceImportance>=6 &&
                cause.OriginalFaction?.def?.techLevel>=TechLevel.Industrial;
            // Pod construction is resource-consuming and can be interrupted.
            // It does not supersede the common corridor/foot extraction plan.
            if(advanced && RHFMod.Settings?.FieldEvacPod == true &&
                Rand.Chance(.57f))op.TryPod=true;
            if(op.TryPod && advanced)
            {
                // A prepared VIP air extraction is a fortified operation,
                // not a random coin toss for whether to secure the prison.
                op.FortifyPrison=true;
                op.AssaultAfterObjective=Rand.Chance(.34f);
                op.Doctrine=(int)RHFDoctrine.FortifiedVipExtraction;
            }
            else if(cause.CrueltyScore>=15 && Rand.Chance(.45f))
            {
                op.AssaultAfterObjective=true;
                op.Doctrine=(int)(cause.Importance>=5 ?
                    RHFDoctrine.RescueAndReprisal : RHFDoctrine.RescueThenAssault);
            }
            else
                op.Doctrine=(int)(op.CombinedStrike ?
                    RHFDoctrine.InfiltrationAndExit : RHFDoctrine.PrisonBreakout);
        }

        // All closed-case raids use the same real controller and tactical roles.
        // No case is merely a letter; failed faction generation is retried by
        // RHFReprisalSystem rather than claiming a nonexistent attack happened.
        public static void ConfigureCase(RHFOperation op, RHFReprisalCase c, bool searching)
        {
            if (op == null || c == null) return;
            op.SourceImportance = c.Importance;
            if (searching)
            {
                op.Doctrine=(int)RHFDoctrine.InfiltrationAndExit;
                op.AssaultAfterObjective=false;
                return;
            }
            op.Doctrine=c.Cause==(int)RHFReprisalCause.Cannibalism ||
                (c.Cause==(int)RHFReprisalCause.ConcealedVip && c.Importance>=6) ?
                (int)RHFDoctrine.SiegeAndRaze : (int)RHFDoctrine.PunitiveAssault;
        }

        public static string Label(RHFOperation op)
        {
            if(op==null)return "не определён";
            switch((RHFDoctrine)op.Doctrine)
            {
                case RHFDoctrine.InfiltrationAndExit:return "быстрый прорыв, поиск и организованный вывод заложника под прикрытием";
                case RHFDoctrine.FortifiedVipExtraction:return op.FortifyPrison ?
                    "высадка у камеры, установка баррикад, сборка капсулы для VIP" :
                    "высадка у камеры, сборка полевой эвакуационной капсулы";
                case RHFDoctrine.RescueAndReprisal:return "освобождение и карательные действия";
                case RHFDoctrine.RescueThenAssault:return "освобождение, затем разрушение укреплений";
                case RHFDoctrine.PunitiveAssault:return "массированный карательный штурм";
                case RHFDoctrine.TargetedTraitorHunt:return "поиск и приоритетное устранение перебежчика";
                case RHFDoctrine.SiegeAndRaze:return "осада и разрушение базы";
                default:return "штурм тюрьмы и совместный вывод пленных";
            }
        }
    }
}
