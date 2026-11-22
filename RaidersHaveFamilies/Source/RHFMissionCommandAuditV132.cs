// RHF v0.13.2 — explicit on-map command ownership audit.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI.Group;
namespace RaidersHaveFamilies
{
    public static class RHFMissionCommandAuditV132
    {
        [DebugAction("NJR - ТЕСТЫ | ДИАГНОСТИКА", "33 — Кто командует рейдом и зачем", 
            actionType=DebugActionType.Action,allowedGameStates=AllowedGameStates.PlayingOnMap)]
        public static void Show()
        {
            Map map=Find.CurrentMap;
            if(map==null)return;
            var ops=RHFMissionIntegrityV130.Operations(map);
            var sb=new StringBuilder("NJR: фактическая цепочка командования\n");
            if(ops==null || ops.Count==0)
                sb.AppendLine("Нет активных операций RHF. Если рейдеры всё ещё на карте, это неуправляемый остаток рейда.");
            else for(int i=0;i<ops.Count && i<4;i++)
            {
                RHFOperation op=ops[i];
                if(op==null)continue;
                sb.AppendLine("Операция #"+(i+1)+" тип="+(RHFOperationType)op.Type+
                    " фаза="+op.Phase+" доктрина="+(RHFDoctrine)op.Doctrine+
                    " активна="+(!op.Finished)+" цель="+
                    (op.Vip?.LabelShortCap??(op.Targets!=null && op.Targets.Count>0 ?
                        op.Targets[0]?.LabelShortCap:"нет"))+
                    " rescue="+op.Squad?.Count+" diversion="+op.Diversion?.Count+
                    " rescued="+op.Freed?.Count+" combined="+op.CombinedStrike+
                    " landed="+op.Deployed+" parties="+op.SuccessfulParties+
                    "/"+op.Parties?.Count);
                if(op.Phase==1)
                    sb.AppendLine(RHFStagedAdvanceV139.Diagnostic(op));
                if(op.Phase==3)
                    sb.AppendLine("VIP construction: budget="+op.PodBudgetPaid+
                        " supplies="+op.PodSuppliesIssued+
                        " stage="+op.PodConstructionStage+
                        " timer="+op.PodConstructionTick+
                        " launcher="+op.PodLauncherCell+
                        " pod="+op.PodCell+
                        " barricades="+op.BarricadesBuilt+
                        " pendingWall="+op.BarricadeCell+
                        " ready="+op.PodReady);
                if(op.Squad==null)continue;
                int own=0,vanilla=0,none=0;
                int shown=0;
                foreach(Pawn pawn in op.Squad)
                {
                    if(pawn==null || !pawn.Spawned || pawn.Map!=map || pawn.Dead)continue;
                    Lord l=pawn.GetLord();
                    bool mission=l?.LordJob is RHFRescueLordJobV124;
                    if(mission)own++;else if(l==null)none++;else vanilla++;
                    if(shown++>=10)continue;
                    sb.AppendLine("  "+pawn.LabelShortCap+" lord="+
                        (l?.LordJob?.GetType().Name??"нет")+" job="+
                        (pawn.jobs?.curJob?.def?.defName??"нет")+" target="+
                        (pawn.jobs?.curJob?.targetA.ToString()??"нет")+
                        " suppression/CE="+RHFCombatPlannerV125.IsCombatEmergencyJob(pawn)+
                        " pos="+pawn.Position);
                }
                sb.AppendLine("Командование: RHF="+own+" vanilla="+vanilla+" без Lord="+none+
                    "; lastProgressTick="+op.LastProgressTick);
            }
            Find.WindowStack.Add(new Dialog_MessageBox(sb.ToString()));
        }
    }
}
