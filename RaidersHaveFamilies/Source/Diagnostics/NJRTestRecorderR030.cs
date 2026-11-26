// R030 TEST ONLY. No gameplay decisions, no saved fields, no RNG consumption.
// All instrumentation is installed explicitly by the tester, with a separate Harmony ID.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RaidersHaveFamilies
{
    internal static class NJRTestRecorderR030
    {
        internal const string Category = "NJR - ЗАПИСЬ ТЕСТОВ";
        private const long SegmentLimit = 32L * 1024 * 1024;
        private const long SessionLimit = 512L * 1024 * 1024;
        private static readonly object Gate = new object();
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals };
        private static readonly Stopwatch Clock = new Stopwatch();
        private static readonly Type PathFollowerType = AccessTools.TypeByName("Verse.AI.Pawn_PathFollower");
        private static readonly FieldInfo PathPawn = PathFollowerType==null?null:AccessTools.Field(PathFollowerType,"pawn");
        private static readonly FieldInfo PathCurrent = PathFollowerType==null?null:AccessTools.Field(PathFollowerType,"curPath");
        private static readonly FieldInfo TrackerPawn = AccessTools.Field(typeof(Pawn_JobTracker), "pawn");
        private static readonly Dictionary<RHFOperation,int> Operations = new Dictionary<RHFOperation,int>();
        private static readonly Dictionary<RHFOperation,string> Routes = new Dictionary<RHFOperation,string>();
        private static readonly Dictionary<Map,Dictionary<int,string>> Objects = new Dictionary<Map,Dictionary<int,string>>();
        private static readonly Dictionary<Map,int> LastSample = new Dictionary<Map,int>();
        private static readonly Dictionary<Map,int> LastObjects = new Dictionary<Map,int>();
        private static readonly List<string> HookResults = new List<string>();
        private static readonly Queue<string> Recent = new Queue<string>();
        private static StreamWriter Writer;
        private static string Folder, LastZip, StopReason = "not started";
        private static long Sequence, Bytes, SegmentBytes, LastFlush;
        private static int Segment, MainThread, LastTick, Faults;
        private static volatile bool Recording;
        private static bool HooksAttempted;
        [ThreadStatic] private static bool Inside;
        internal static bool Active => Recording;

        private static int Tick() => Environment.CurrentManagedThreadId == MainThread ?
            (Find.TickManager?.TicksGame ?? LastTick) : Volatile.Read(ref LastTick);
        private static int MapId(Map m) => m == null ? -1 : RuntimeHelpers.GetHashCode(m);
        private static int OpId(RHFOperation op)
        {
            if(op==null)return -1;
            if(!Operations.TryGetValue(op,out int id)) Operations.Add(op,id=Operations.Count+1);
            return id;
        }
        private static object Cell(IntVec3 c) => c.IsValid ? new[]{c.x,c.z} : null;
        private static int Id(Thing t) => t?.thingIDNumber ?? -1;
        private static object ThingData(Thing t)
        {
            if(t==null)return null;
            return new { id=Id(t), def=t.def?.defName, label=t.LabelShortCap.ToString(),
                faction=t.Faction?.Name, pos=Cell(t.Position), map=MapId(t.MapHeld),
                spawned=t.Spawned, destroyed=t.Destroyed, hp=t.def?.useHitPoints==true?t.HitPoints:(int?)null,
                maxHP=t.def?.useHitPoints==true?t.MaxHitPoints:(int?)null, stack=t.stackCount,
                rotation=t.Rotation.AsInt, size=t.def==null?null:new[]{t.def.size.x,t.def.size.z},
                doorOpen=t is Building_Door door?door.Open:(bool?)null,
                launcherFuel=t.def?.defName=="PodLauncher" ? (t as ThingWithComps)?.GetComp<CompRefuelable>()?.HasFuel : null,
                launcherFull=t.def?.defName=="PodLauncher" ? (t as ThingWithComps)?.GetComp<CompRefuelable>()?.IsFull : null };
        }
        private static object Target(LocalTargetInfo t) => new { id=Id(t.Thing), cell=Cell(t.Cell) };
        private static object JobData(Job j) => j==null ? null : new { id=RuntimeHelpers.GetHashCode(j),
            def=j.def?.defName, a=Target(j.targetA), b=Target(j.targetB), c=Target(j.targetC), j.expiryInterval };
        private static object PawnData(Pawn p)
        {
            if(p==null)return null;
            var lord=p.Spawned?p.GetLord():null;
            return new { thing=ThingData(p), dead=p.Dead, downed=p.Downed, prisoner=p.IsPrisonerOfColony,
                slave=p.IsSlaveOfColony, mental=p.InMentalState, job=JobData(p.jobs?.curJob),
                duty=p.mindState?.duty?.def?.defName, lord=lord==null?-1:RuntimeHelpers.GetHashCode(lord),
                lordJob=lord?.LordJob?.GetType().Name, weapon=p.equipment?.Primary?.def?.defName,
                health=p.health?.summaryHealth?.SummaryHealthPercent,
                construction=p.skills?.GetSkill(SkillDefOf.Construction)?.Level };
        }
        private static int[] PawnIds(List<Pawn> pawns) => pawns==null?Array.Empty<int>():pawns.ConvertAll(p=>Id(p)).ToArray();
        private static object BoardData(RHFTacticalBoardS41 b)
        {
            if(b==null)return null;
            var orders=new List<object>();
            if(b.Orders!=null)foreach(var o in b.Orders)if(o!=null)orders.Add(new { pawn=Id(o.Member),
                role=((RHFRoleS41)o.Role).ToString(), objective=ThingData(o.Objective), cell=Cell(o.Cell), tick=o.ChangedTick });
            return new { id=RuntimeHelpers.GetHashCode(b), b.Decision, b.ObservedPhase, b.LastPlanTick,
                b.LastProgressTick, b.NextReplanTick, b.Replans, b.LastObstacleHP, b.LastThreatHP,
                target=Id(b.RescueTarget), breacher=Id(b.Breacher), barrier=ThingData(b.Barrier), threat=ThingData(b.Threat),
                engineer=Id(b.PodEngineerS41), work=ThingData(b.PodWorkTargetS41), b.PodWorkStageS41,
                withdrawal=Cell(b.WithdrawalCell), orders };
        }
        private static object OpData(RHFOperation op)
        {
            if(op==null)return null;
            var escorts=new List<object>();
            if(op.Escorts!=null)foreach(var e in op.Escorts)if(e!=null)escorts.Add(new { captive=Id(e.Captive), escort=Id(e.Escort) });
            var cargo=new List<object>();
            if(op.PodCargoStacksS41!=null)foreach(var t in op.PodCargoStacksS41)if(t!=null)cargo.Add(ThingData(t));
            var parties=new List<object>();
            if(op.Parties!=null)foreach(var p in op.Parties)if(p!=null)parties.Add(new { faction=p.Faction?.Name,p.Role,p.Points,p.Drop,p.Siege,p.Breach,p.Attempted,p.Succeeded,p.Members,p.SupplyBudget,p.SupplySpent });
            return new { id=OpId(op), op.Type, op.Doctrine, op.Phase, op.Deployed, op.Finished, op.DebugForced,
                op.CreatedTick, op.SpawnTick, op.ExpiryTick, op.PhaseStartTick, op.LastProgressTick, op.LastOrderTick, op.ThreatPoints,
                sponsor=op.Sponsor?.Name, vip=Id(op.Vip), targets=PawnIds(op.Targets), squad=PawnIds(op.Squad),
                freed=PawnIds(op.Freed), diversion=PawnIds(op.Diversion), escorts, parties, op.EscapedWithEscort,
                exit=Cell(op.ExitCell), breach=Cell(op.BreachCell), breacher=Id(op.BreachOwner),
                op.TryPod, op.PodSuppliesIssued, op.PodConstructionStage, op.PodConstructionTick, op.PodReady,
                op.PodBudgetPaid, op.PodSupplyCost, op.PodLaunchCompleted, op.BarricadesBuilt,
                pod=Cell(op.PodCell), launcherCell=Cell(op.PodLauncherCell), launcher=ThingData(op.PodLauncherThing),
                vehicle=ThingData(op.PodVehicleThing), steel=ThingData(op.PodSteel), components=ThingData(op.PodComponents), cargo,
                rescueBoard=BoardData(op.BoardS41), offenseBoard=BoardData(op.OffenseBoardS41) };
        }
        private static object Arg(object v)
        {
            if(v==null)return null;
            if(v is Pawn p)return new { pawn=Id(p), cell=Cell(p.Position), map=MapId(p.MapHeld) };
            if(v is Thing t)return ThingData(t);
            if(v is Job j)return JobData(j);
            if(v is RHFOperation op)return new { operation=OpId(op) };
            if(v is Map m)return new { map=MapId(m) };
            if(v is IntVec3 c)return Cell(c);
            if(v is LocalTargetInfo target)return Target(target);
            if(v is DamageInfo d)return new { def=d.Def?.defName, amount=d.Amount, instigator=Id(d.Instigator) };
            if(v is Def def)return def.defName;
            if(v is Faction faction)return faction.Name;
            if(v is Enum)return v.ToString();
            if(v is ThinkNode)return v.GetType().FullName;
            if(v is string || v is bool || v is int || v is float || v is double || v is long)return v;
            if(v is IList list){var a=new List<object>();for(int i=0;i<Math.Min(list.Count,256);i++)a.Add(Arg(list[i]));return new { count=list.Count, sample=a };}
            return v.GetType().FullName;
        }
        private static object[] Args(object[] args)
        { if(args==null)return Array.Empty<object>();return Array.ConvertAll(args,Arg); }
        private static RHFOperation FindOp(object[] args)
        { if(args!=null)foreach(var a in args)if(a is RHFOperation op)return op;return null; }

        private static void OpenSegment()
        {
            Writer=new StreamWriter(Path.Combine(Folder,"events-"+(++Segment).ToString("D4")+".jsonl"),false,Utf8,65536);
            SegmentBytes=0;
        }
        private static void CloseWriter()
        { try{Writer?.Dispose();}catch{}finally{Writer=null;} }
        private static void StopLocked(string reason)
        {
            Recording=false;StopReason=reason;CloseWriter();
            try{if(Folder!=null)File.WriteAllText(Path.Combine(Folder,"session-end.json"),
                JsonSerializer.Serialize(new { reason, events=Sequence, bytes=Bytes, faults=Faults, segments=Segment, utc=DateTime.UtcNow },JsonOptions),Utf8);}catch{}
        }
        private static void Write(string kind,object data,bool urgent=false)
        {
            lock(Gate)
            {
                if(!Recording || Writer==null)return;
                try
                {
                    string line=JsonSerializer.Serialize(new { seq=++Sequence, tick=Tick(), ms=Clock.ElapsedMilliseconds,
                        utc=DateTime.UtcNow, thread=Environment.CurrentManagedThreadId, kind, data },JsonOptions);
                    int size=Utf8.GetByteCount(line)+2;
                    if(Bytes+size>SessionLimit){StopLocked("512 MiB limit reached; capture ended explicitly");return;}
                    if(SegmentBytes+size>SegmentLimit && SegmentBytes>0){CloseWriter();OpenSegment();}
                    Writer.WriteLine(line);Bytes+=size;SegmentBytes+=size;
                    if(kind=="console" || kind=="route" || kind=="decision_result" || kind=="cell_result" || kind=="tester_marker" || kind=="recorder_fault")
                    {
                        Recent.Enqueue(line.Length>260?line.Substring(0,260)+"…":line);
                        while(Recent.Count>12)Recent.Dequeue();
                    }
                    if(urgent || Clock.ElapsedMilliseconds-LastFlush>=1000){Writer.Flush();LastFlush=Clock.ElapsedMilliseconds;}
                }
                catch(Exception e){StopLocked("writer error: "+e.GetType().Name+": "+e.Message);}
            }
        }
        private static void Observe(Action a)
        {
            if(!Recording || Inside)return;
            Inside=true;
            try{a();}catch(Exception e){int n=Interlocked.Increment(ref Faults);if(n<=20)Write("recorder_fault",new { error=e.ToString(), count=n },true);}
            finally{Inside=false;}
        }
        private static void MainObserve(Action a)
        { if(Environment.CurrentManagedThreadId==MainThread)Observe(a); }

        // Prefix on Verse.Log is independent of Unity console delivery on Waylith.
        private static void ConsolePrefix(MethodBase __originalMethod,object[] __args)
        {
            if(!Recording)return;
            Observe(()=>{string method=__originalMethod.Name;bool urgent=method.StartsWith("Error",StringComparison.Ordinal);
                Write("console",new { level=method, args=Args(__args), stack=urgent?Environment.StackTrace:null },urgent);});
        }
        private static void JobPrefix(object __instance, MethodBase __originalMethod,object[] __args)
        {
            if(!Recording)return;
            MainObserve(()=>{var tracker=__instance as Pawn_JobTracker;var p=TrackerPawn?.GetValue(tracker) as Pawn;
                if(p?.MapHeld==null)return;
                Write("job_request",new { method=__originalMethod.Name,pawn=PawnData(p),args=Args(__args) });});
        }
        private static void JobPostfix(object __instance, MethodBase __originalMethod)
        {
            if(!Recording)return;
            MainObserve(()=>{var p=TrackerPawn?.GetValue(__instance) as Pawn;if(p?.MapHeld==null)return;
                Write("job_after",new { method=__originalMethod.Name,pawn=PawnData(p) });});
        }
        private static void GeneralPrefix(object __instance,MethodBase __originalMethod,object[] __args)
        {
            if(!Recording)return;
            MainObserve(()=>{var thing=__instance as Thing; if(thing!=null && thing.MapHeld==null && __originalMethod.Name!="SpawnSetup")return;
                var lord=__instance as Lord;
                Map eventMap=thing?.MapHeld;
                if(eventMap==null && __instance!=null && (__originalMethod.Name=="SetTerrain" || __originalMethod.Name=="SetRoof"))
                    eventMap=AccessTools.Field(__instance.GetType(),"map")?.GetValue(__instance) as Map;
                Write("engine_event",new { method=__originalMethod.DeclaringType?.Name+"."+__originalMethod.Name,
                    subject=ThingData(thing),map=MapId(eventMap),lord=lord==null?-1:RuntimeHelpers.GetHashCode(lord), args=Args(__args) });});
        }
        private static void DriverPrefix(object __instance,MethodBase __originalMethod,object[] __args)
        {
            if(!Recording)return;
            MainObserve(()=>{var p=AccessTools.Field(__instance.GetType(),"pawn")?.GetValue(__instance) as Pawn;
                if(p?.MapHeld==null)return;
                Write("job_driver",new { method=__originalMethod.Name,driver=__instance.GetType().FullName,pawn=Id(p),
                    map=MapId(p.MapHeld),job=JobData(p.jobs?.curJob),args=Args(__args) });});
        }
        private static void PathEvent(object __instance,MethodBase __originalMethod,object[] __args)
        {
            if(!Recording)return;
            MainObserve(()=>{var p=PathPawn?.GetValue(__instance) as Pawn;if(p?.MapHeld==null)return;
                var path=PathCurrent?.GetValue(__instance);var nodes=new List<object>();
                if(path!=null && __originalMethod.Name=="StartPath")
                {
                    var raw=AccessTools.Property(path.GetType(),"NodesReversed")?.GetValue(path) as IEnumerable;
                    if(raw!=null)foreach(object node in raw)if(node is IntVec3 c)nodes.Add(Cell(c));
                }
                Write("path_event",new { method=__originalMethod.Name,pawn=Id(p),map=MapId(p.MapHeld),
                    position=Cell(p.Position),job=JobData(p.jobs?.curJob),args=Args(__args),nodesReversed=nodes });});
        }
        private static void CastPostfix(object __instance,MethodBase __originalMethod,object[] __args,bool __result)
        {
            if(!Recording)return;
            MainObserve(()=>{if(!__result)return;
                var caster=AccessTools.Property(__instance.GetType(),"Caster")?.GetValue(__instance) as Thing;
                if(caster?.MapHeld==null)return;
                Write("cast_started",new { method=__originalMethod.Name,caster=ThingData(caster),args=Args(__args) });});
        }
        // Static method variant: Harmony has no __instance for static methods.
        private static void DecisionPrefix(MethodBase __originalMethod,object[] __args)
        {
            if(!Recording)return;
            MainObserve(()=>Write("decision_begin",new { method=__originalMethod.DeclaringType?.Name+"."+__originalMethod.Name,
                operation=OpId(FindOp(__args)),args=Args(__args) }));
        }
        private static void DecisionPostfix(MethodBase __originalMethod,object[] __args)
        {
            if(!Recording)return;
            MainObserve(()=>Write("decision_end",new { method=__originalMethod.DeclaringType?.Name+"."+__originalMethod.Name,
                operation=OpData(FindOp(__args)),args=Args(__args) }));
        }
        private static void BoolPostfix(MethodBase __originalMethod,object[] __args,bool __result)
        {
            if(!Recording)return;
            MainObserve(()=>Write("decision_result",new { method=__originalMethod.DeclaringType?.Name+"."+__originalMethod.Name,
                result=__result,operation=OpId(FindOp(__args)),args=Args(__args) }));
        }
        private static void CellPostfix(MethodBase __originalMethod,object[] __args,IntVec3 __result)
        {
            if(!Recording)return;
            MainObserve(()=>Write("cell_result",new { method=__originalMethod.DeclaringType?.Name+"."+__originalMethod.Name,
                result=Cell(__result),operation=OpId(FindOp(__args)),args=Args(__args) }));
        }
        private static void JobChoicePostfix(MethodBase __originalMethod,object[] __args,Job __result)
        {
            if(!Recording)return;
            MainObserve(()=>Write("ai_job_choice",new { method=__originalMethod.DeclaringType?.Name+"."+__originalMethod.Name,
                operation=OpId(FindOp(__args)),args=Args(__args),result=JobData(__result) }));
        }
        private static void AssignPrefix(RHFTacticalBoardS41 __instance,object[] __args)
        {
            if(!Recording)return;
            MainObserve(()=>{if(__args.Length<5 || !(__args[0] is Pawn p))return;
                var prior=__instance.Orders?.Find(o=>o?.Member==p);
                if(prior!=null && prior.Role==(int)(RHFRoleS41)__args[1] && prior.Objective==(__args[2] as Thing) && prior.Cell==(IntVec3)__args[3])return;
                Write("role_change",new { board=RuntimeHelpers.GetHashCode(__instance),pawn=Id(p),previous=prior==null?null:((RHFRoleS41)prior.Role).ToString(),
                    role=__args[1].ToString(),target=ThingData(__args[2] as Thing),cell=Cell((IntVec3)__args[3]),plannedTick=__args[4] });});
        }
        private static void RoutePostfix(object[] __args,RHFRoutePlanV128 __result)
        {
            if(!Recording)return;
            MainObserve(()=>{var op=FindOp(__args);var r=__result;if(op==null || r==null)return;
                string signature=RuntimeHelpers.GetHashCode(r)+":"+r.Tick+":"+r.Expanded+":"+r.Valid+":"+r.Explanation;
                if(Routes.TryGetValue(op,out string old) && old==signature)return;Routes[op]=signature;
                var cells=new List<object>();if(r.Cells!=null)foreach(var c in r.Cells)cells.Add(Cell(c));
                Write("route",new { operation=OpId(op),r.Valid,r.BudgetExceeded,r.SearchPending,r.RiskFallback,r.Tick,r.Expanded,
                    from=Cell(r.From),to=Cell(r.To),captive=Id(r.Captive),barrier=ThingData(r.FirstBarrier),r.FirstBarrierIndex,
                    r.TotalCost,r.TotalStructuralHP,r.DangerCost,r.Walls,r.Doors,r.FirstTurretExposureIndex,r.ExposedTurretCells,r.Explanation,cells });});
        }
        private static void Hook(Harmony h,Type type,string name,string pre,string post,Type returnType=null)
        {
            if(type==null){HookResults.Add("MISSING type for "+name);return;}
            int count=0;
            foreach(var method in type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly))
            {
                if(method.Name!=name || method.IsAbstract || method.ContainsGenericParameters || (returnType!=null && method.ReturnType!=returnType))continue;
                count++;
                try
                {
                    HarmonyMethod p=pre==null?null:new HarmonyMethod(AccessTools.Method(typeof(NJRTestRecorderR030),pre));
                    HarmonyMethod q=post==null?null:new HarmonyMethod(AccessTools.Method(typeof(NJRTestRecorderR030),post));
                    h.Patch(method,prefix:p,postfix:q);HookResults.Add("OK "+type.Name+"."+method);
                }
                catch(Exception e){HookResults.Add("FAILED "+type.Name+"."+method+": "+e.GetBaseException().Message);}
            }
            if(count==0)HookResults.Add("MISSING "+type.FullName+"."+name);
        }
        // Verse.Log prefixes alone can miss an already-buffered/native error.
        // Snapshot the live engine queue and raw log on opening the actual log
        // window; never close it, suppress errors or clear the engine queue.
        private static readonly BindingFlags LogMembers=BindingFlags.Public|BindingFlags.NonPublic|
            BindingFlags.Instance|BindingFlags.Static;
        private static object ReadLogMember(object instance,string name)
        {
            if(instance==null)return null;
            try
            {
                Type t=instance.GetType();
                FieldInfo f=t.GetField(name,LogMembers);
                if(f!=null && !f.IsStatic)return f.GetValue(instance);
                PropertyInfo p=t.GetProperty(name,LogMembers);
                if(p!=null && p.GetIndexParameters().Length==0 && p.GetMethod?.IsStatic==false)return p.GetValue(instance);
            }
            catch(Exception){ }
            return null;
        }
        private static string LogText(object value,int limit=32000)
        {
            string t=value?.ToString();
            return t==null || t.Length<=limit?t:t.Substring(0,limit)+" [truncated]";
        }
        private static bool SnapshotLogItems(string source,object collection,List<object> output,List<string> notes)
        {
            if(!(collection is IEnumerable items) || collection is string)return false;
            var tail=new Queue<object>();int count=0;
            try
            {
                foreach(object item in items)
                {
                    if(item==null)continue;
                    if(++count>20000){notes.Add(source+": enumeration limit");break;}
                    tail.Enqueue(new { source, type=LogText(ReadLogMember(item,"type")??ReadLogMember(item,"Type"),100),
                        text=LogText(ReadLogMember(item,"text")??ReadLogMember(item,"Text")??
                            ReadLogMember(item,"message")??ReadLogMember(item,"Message")??item),
                        stack=LogText(ReadLogMember(item,"stackTrace")??ReadLogMember(item,"StackTrace")),
                        repeats=LogText(ReadLogMember(item,"repeatCount")??ReadLogMember(item,"RepeatCount"),100) });
                    if(tail.Count>250)tail.Dequeue();
                }
                output.AddRange(tail);notes.Add(source+": "+count+" entries inspected");return true;
            }
            catch(Exception e){notes.Add(source+": "+e.GetBaseException().Message);return false;}
        }
        private static void SnapshotEngineLog(string trigger)
        {
            var entries=new List<object>();var notes=new List<string>();
            string[] nested={"Messages","messages","AllMessages","allMessages","Queue","queue","messageQueue","LogMessages","logMessages","Entries","entries","GetMessages"};
            Type type=typeof(Verse.Log);
            foreach(MemberInfo m in type.GetMembers(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static))
            {
                string name=m.Name;
                if(name.IndexOf("message",StringComparison.OrdinalIgnoreCase)<0 &&
                   name.IndexOf("queue",StringComparison.OrdinalIgnoreCase)<0)continue;
                try
                {
                    object value=null;
                    if(m is FieldInfo f && f.IsStatic)value=f.GetValue(null);
                    else if(m is PropertyInfo prop && prop.GetMethod?.IsStatic==true && prop.GetIndexParameters().Length==0)value=prop.GetValue(null);
                    else if(m is MethodInfo method && method.IsStatic && !method.ContainsGenericParameters &&
                        method.Name.StartsWith("Get",StringComparison.Ordinal) && method.GetParameters().Length==0 &&
                        typeof(IEnumerable).IsAssignableFrom(method.ReturnType))value=method.Invoke(null,null);
                    else continue;
                    if(value==null || value is string)continue;
                    notes.Add(name+": "+value.GetType().FullName);
                    if(SnapshotLogItems(name,value,entries,notes))continue;
                    foreach(string child in nested)
                    {
                        object childValue=ReadLogMember(value,child);
                        if(childValue!=null && SnapshotLogItems(name+"."+child,childValue,entries,notes))break;
                    }
                }
                catch(Exception e){notes.Add(name+": "+e.GetBaseException().Message);}
                if(entries.Count>=500)break;
            }
            Write("engine_log_snapshot",new { trigger, notes, entries },true);
        }
        private static string CopyPlayerLog(string suffix)
        {
            if(Folder==null)return "no session folder";
            string root=GenFilePaths.SaveDataFolderPath;
            foreach(string source in new[]{Path.Combine(root,"Logs","Player.log"),Path.Combine(root,"Player.log")})
            {
                if(!File.Exists(source))continue;
                try
                {
                    string dest=Path.Combine(Folder,"Player-"+suffix+".log");
                    if(suffix=="console" && File.Exists(dest))return "first console log retained: "+Path.GetFileName(dest);
                    const long limit=16L*1024*1024;
                    using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
                    using(var output=new FileStream(dest,FileMode.Create,FileAccess.Write,FileShare.Read))
                    {
                        long length=input.Length,start=Math.Max(0,length-limit);
                        input.Seek(start,SeekOrigin.Begin);
                        byte[] buffer=new byte[65536];long left=length-start;
                        while(left>0)
                        {
                            int n=input.Read(buffer,0,(int)Math.Min(left,buffer.Length));
                            if(n==0)break;
                            output.Write(buffer,0,n);left-=n;
                        }
                        return Path.GetFileName(dest)+" originalBytes="+length+" startOffset="+start+" copied="+(length-start-left);
                    }
                }
                catch(Exception e){return "Player.log copy failed: "+e.GetBaseException().Message;}
            }
            return "Player.log not found in Logs/ or save-data root";
        }
        private static void WindowPrefix(MethodBase __originalMethod,object[] __args)
        {
            if(!Recording)return;
            MainObserve(()=>{
                if(__args==null || __args.Length==0 || __args[0]==null)return;
                string name=__args[0].GetType().FullName ?? "";
                if(name.IndexOf("Log",StringComparison.OrdinalIgnoreCase)<0 &&
                   name.IndexOf("Debug",StringComparison.OrdinalIgnoreCase)<0)return;
                Write("debug_window_open",new { window=name,method=__originalMethod.Name,
                    stack=Environment.StackTrace },true);
                if(name.IndexOf("EditWindow_Log",StringComparison.OrdinalIgnoreCase)>=0)
                {
                    SnapshotEngineLog("console opening");
                    Write("raw_log_snapshot",new { trigger="console opening", result=CopyPlayerLog("console") },true);
                }
            });
        }
        private static void Install()
        {
            if(HooksAttempted)return;HooksAttempted=true;
            var h=new Harmony("pavuk.njr.testrecorder.r030");
            foreach(string n in new[]{"Message","Warning","Error","ErrorOnce","WarningOnce"})Hook(h,typeof(Verse.Log),n,nameof(ConsolePrefix),null);
            Hook(h,AccessTools.TypeByName("Verse.WindowStack"),"Add",nameof(WindowPrefix),null);
            foreach(string n in new[]{"StartJob","EndCurrentJob"})Hook(h,typeof(Pawn_JobTracker),n,nameof(JobPrefix),nameof(JobPostfix));
            Hook(h,typeof(JobDriver),"EndJobWith",nameof(DriverPrefix),null);
            foreach(string n in new[]{"StartPath","PatherFailed","PatherArrived"})Hook(h,PathFollowerType,n,null,nameof(PathEvent));
            Hook(h,typeof(Verb),"TryStartCastOn",null,nameof(CastPostfix),typeof(bool));
            foreach(string n in new[]{"SpawnSetup","DeSpawn","Kill","ExitMap","SetFaction"})Hook(h,typeof(Pawn),n,nameof(GeneralPrefix),null);
            foreach(string n in new[]{"SpawnSetup","DeSpawn","Destroy"})Hook(h,typeof(Building),n,nameof(GeneralPrefix),null);
            Hook(h,typeof(Thing),"TakeDamage",nameof(GeneralPrefix),null);
            foreach(string n in new[]{"AddPawn","Notify_PawnLost","ReceiveMemo"})Hook(h,typeof(Lord),n,nameof(GeneralPrefix),null);
            Hook(h,typeof(RHFTacticalBoardS41),"Assign",nameof(AssignPrefix),null);
            Hook(h,typeof(RHFObjectiveRouteV128),"ResolveFromSources",null,nameof(RoutePostfix),typeof(RHFRoutePlanV128));
            Hook(h,typeof(RHFObjectiveRouteV128),"RejectUnworkableBarrier",nameof(DecisionPrefix),null);
            Hook(h,typeof(RHFRescueGroundEntryS41),"Choose",null,nameof(CellPostfix),typeof(IntVec3));
            Hook(h,typeof(NJRRescueExitS41),"Choose",null,nameof(CellPostfix),typeof(IntVec3));
            Hook(h,typeof(RHFRescuePlannerS41),"Infiltrate",nameof(DecisionPrefix),nameof(DecisionPostfix));
            Hook(h,typeof(RHFRescuePlannerS41),"Extract",nameof(DecisionPrefix),nameof(DecisionPostfix));
            Hook(h,typeof(RHFRescuePlannerS41),"TryAdoptReleased",null,nameof(BoolPostfix),typeof(bool));
            Hook(h,typeof(RHFRescueCommandV124),"StabilizeFreed",nameof(DecisionPrefix),nameof(DecisionPostfix));
            Hook(h,typeof(NJRRescueExitS41),"FirstBlockedExitDoor",nameof(DecisionPrefix),null);
            Hook(h,typeof(RHFPodPlannerS41),"Plan",nameof(DecisionPrefix),nameof(DecisionPostfix));
            Hook(h,typeof(RHFEnemyPodLaunchS41),"TryLaunch",null,nameof(BoolPostfix),typeof(bool));
            Hook(h,typeof(RHFOffenseDirectorS41),"Tick",nameof(DecisionPrefix),nameof(DecisionPostfix));
            Hook(h,typeof(RHFOperationController),"Deploy",null,nameof(BoolPostfix),typeof(bool));
            Hook(h,typeof(RHFOperationController),"BuildAndLaunch",nameof(DecisionPrefix),nameof(DecisionPostfix));
            Hook(h,typeof(RHFOperationController),"FindFieldConstructionSite",null,nameof(BoolPostfix),typeof(bool));
            Hook(h,typeof(RHFOperationController),"TryLaunchPhysicalVipPod",null,nameof(BoolPostfix),typeof(bool));
            Hook(h,typeof(RHFPodPlannerS41),"WorkJob",null,nameof(JobChoicePostfix),typeof(Job));
            Hook(h,typeof(NJRCombatContactS44),"Respond",null,nameof(JobChoicePostfix),typeof(Job));
            foreach(var t in new[]{typeof(RHFRescueRoleJobGiverS41),typeof(RHFOffenseRoleJobGiverS41),typeof(RHFOffenseDefenseJobGiverS41)})
                Hook(h,t,"TryGiveJob",null,nameof(JobChoicePostfix),typeof(Job));
            Hook(h,AccessTools.TypeByName("Verse.TerrainGrid"),"SetTerrain",nameof(GeneralPrefix),null);
            Hook(h,AccessTools.TypeByName("Verse.RoofGrid"),"SetRoof",nameof(GeneralPrefix),null);
        }

        private static void Layout(Map map)
        {
            // Row run-length encoding: [x start, length, terrain, roof, standable].
            // Static layout at the start / manual checkpoint, plus terrain/roof events.
            for(int z=0;z<map.Size.z;z++)
            {
                var runs=new List<object>();string lastTerrain=null,lastRoof=null;bool lastWalk=false;int start=0;
                for(int x=0;x<map.Size.x;x++)
                {
                    var c=new IntVec3(x,0,z);string terrain=map.terrainGrid.TerrainAt(c)?.defName;
                    string roof=map.roofGrid.RoofAt(c)?.defName;bool walk=c.Standable(map);
                    if(x>0 && (terrain!=lastTerrain || roof!=lastRoof || walk!=lastWalk))
                    {runs.Add(new { x=start,length=x-start,terrain=lastTerrain,roof=lastRoof,walk=lastWalk });start=x;}
                    lastTerrain=terrain;lastRoof=roof;lastWalk=walk;
                }
                runs.Add(new { x=start,length=map.Size.x-start,terrain=lastTerrain,roof=lastRoof,walk=lastWalk });
                Write("layout_row",new { map=MapId(map),z,runs });
            }
        }
        private static void SnapshotObjects(Map map,bool full)
        {
            if(!Objects.TryGetValue(map,out var previous)){previous=new Dictionary<int,string>();Objects.Add(map,previous);full=true;}
            var next=new Dictionary<int,string>();var changed=new List<object>();var removed=new List<int>();
            foreach(var t in map.listerThings.AllThings)
            {
                if(t==null || (!(t is Building) && !(t is Blueprint) && !(t is Frame) && t.def?.category!=ThingCategory.Item))continue;
                var data=ThingData(t);string value=JsonSerializer.Serialize(data,JsonOptions);int id=Id(t);next[id]=value;
                if(full || !previous.TryGetValue(id,out string old) || old!=value)changed.Add(data);
                if(changed.Count>=128){Write("objects_changed",new { map=MapId(map),full,items=changed });changed.Clear();}
            }
            foreach(int id in previous.Keys)if(!next.ContainsKey(id))removed.Add(id);
            if(changed.Count>0)Write("objects_changed",new { map=MapId(map),full,items=changed });
            if(removed.Count>0)Write("objects_removed",new { map=MapId(map),ids=removed });
            Objects[map]=next;
        }
        internal static void Sample(Map map,bool force=false)
        {
            if(!Recording || map==null)return;
            // Fast gate before allocating snapshot work on ordinary map ticks.
            int tick=Tick();
            if(!force && LastSample.TryGetValue(map,out int previousTick) && tick>=previousTick && tick-previousTick<120)return;
            MainObserve(()=>{
                int now=Tick();Volatile.Write(ref LastTick,now);
                if(!force && LastSample.TryGetValue(map,out int prior) && now>=prior && now-prior<120)return;
                long sampleStart=Stopwatch.GetTimestamp();
                bool first=!LastSample.ContainsKey(map);LastSample[map]=now;
                if(first || force){Write("map",new { map=MapId(map),width=map.Size.x,height=map.Size.z,full=force });Layout(map);}
                var pawns=new List<object>();foreach(Pawn p in map.mapPawns.AllPawnsSpawned){pawns.Add(PawnData(p));if(pawns.Count>=64){Write("pawns",new { map=MapId(map),items=pawns });pawns.Clear();}}
                if(pawns.Count>0)Write("pawns",new { map=MapId(map),items=pawns });
                var ops=RHFMissionIntegrityV130.Operations(map);var states=new List<object>();
                if(ops!=null)foreach(var op in ops)if(op!=null)states.Add(OpData(op));
                Write("operations",new { map=MapId(map),items=states });
                if(first || force || !LastObjects.TryGetValue(map,out int last) || now<last || now-last>=600)
                {LastObjects[map]=now;SnapshotObjects(map,first||force);}
                Write("sample_cost",new { map=MapId(map),elapsedMs=Stopwatch.GetElapsedTime(sampleStart).TotalMilliseconds });
                lock(Gate){if(Recording && Clock.ElapsedMilliseconds-LastFlush>=1000){try{Writer?.Flush();LastFlush=Clock.ElapsedMilliseconds;}catch(Exception e){StopLocked("flush error: "+e.Message);}}}
            });
        }
        internal static string Start()
        {
            if(Recording)return Status();
            lock(Gate)
            {
                try
                {
                    MainThread=Environment.CurrentManagedThreadId;LastTick=Find.TickManager?.TicksGame??0;
                    Folder=Path.Combine(GenFilePaths.SaveDataFolderPath,"NJR_TestDiagnostics",DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff")+"_"+Guid.NewGuid().ToString("N").Substring(0,8));
                    Directory.CreateDirectory(Folder);LastZip=null;Sequence=Bytes=SegmentBytes=LastFlush=0;Segment=Faults=0;
                    Operations.Clear();Routes.Clear();Objects.Clear();LastSample.Clear();LastObjects.Clear();Recent.Clear();Clock.Restart();OpenSegment();Recording=true;StopReason="recording";
                }
                catch(Exception e){StopLocked("start failed: "+e.Message);return Status();}
            }
            try{Install();}catch(Exception e){HookResults.Add("INSTALL FAILED: "+e);}
            Write("session_start",new { revision="R030", baseRevision="R049", tick=LastTick, hooks=HookResults.ToArray(),
                jobTrackerPawnField=TrackerPawn!=null,pathPawnField=PathPawn!=null,pathCurrentField=PathCurrent!=null, sampleTicks=120, objectTicks=600, byteLimit=SessionLimit,
                note="All Verse console calls; selected engine and NJR events; snapshots. No claim of every internal game action." },true);
            MainObserve(()=>SnapshotEngineLog("session start"));
            Sample(Find.CurrentMap,true);return Status();
        }
        internal static string Status()
        {
            lock(Gate)return (Recording?"ЗАПИСЬ ИДЁТ":"ЗАПИСЬ ОСТАНОВЛЕНА")+"\n"+StopReason+"\nСобытий: "+Sequence+"; МБ: "+(Bytes/1048576d).ToString("F2")+
                "; ошибок самого регистратора: "+Faults+"; хуков OK: "+HookResults.FindAll(x=>x.StartsWith("OK ")).Count+"; пропущено/ошибок: "+HookResults.FindAll(x=>!x.StartsWith("OK ")).Count+"\nКаталог: "+(Folder??"нет")+"\nZIP: "+(LastZip??"не создан");
        }
        internal static string RecentEvents()
        {lock(Gate)return Status()+"\n\nПоследние ключевые события (полные записи в ZIP):\n"+string.Join("\n",Recent);}
        internal static string Mark()
        {if(Recording){Write("tester_marker",new { map=MapId(Find.CurrentMap),label="manual checkpoint" },true);Sample(Find.CurrentMap,true);}return Status();}
        internal static string Export()
        {
            if(Recording)
            {
                MainObserve(()=>SnapshotEngineLog("export"));
                Write("raw_log_snapshot",new { trigger="export", result=CopyPlayerLog("export") },true);
                Write("session_stop",new { reason="tester export" },true);lock(Gate)StopLocked("tester export");
            }
            if(Folder==null)return Status();
            try{if(LastZip==null){string zip=Folder+".zip",temp=zip+".tmp";
                    if(File.Exists(temp))File.Delete(temp);
                    ZipFile.CreateFromDirectory(Folder,temp,CompressionLevel.Fastest,false);
                    File.Move(temp,zip,true);LastZip=zip;}}
            catch(Exception e){return Status()+"\nОшибка ZIP: "+e.Message+"\nМожно отправить файлы из каталога вручную.";}
            return Status();
        }
    }

    public static class NJRTestRecorderActionsR030
    {
        [DebugAction(NJRTestRecorderR030.Category,"01 — Начать запись",actionType=DebugActionType.Action,allowedGameStates=AllowedGameStates.PlayingOnMap)]
        public static void Start(){Find.WindowStack.Add(new Dialog_MessageBox(NJRTestRecorderR030.Start()));}
        [DebugAction(NJRTestRecorderR030.Category,"02 — Метка и полный снимок карты",actionType=DebugActionType.Action,allowedGameStates=AllowedGameStates.PlayingOnMap)]
        public static void Mark(){Find.WindowStack.Add(new Dialog_MessageBox(NJRTestRecorderR030.Mark()));}
        [DebugAction(NJRTestRecorderR030.Category,"03 — Статус записи",actionType=DebugActionType.Action,allowedGameStates=AllowedGameStates.PlayingOnMap)]
        public static void Status(){Find.WindowStack.Add(new Dialog_MessageBox(NJRTestRecorderR030.Status()));}
        [DebugAction(NJRTestRecorderR030.Category,"04 — Остановить и создать ZIP",actionType=DebugActionType.Action,allowedGameStates=AllowedGameStates.PlayingOnMap)]
        public static void Export(){Find.WindowStack.Add(new Dialog_MessageBox(NJRTestRecorderR030.Export()));}
        [DebugAction(NJRTestRecorderR030.Category,"05 — Последние ключевые события",actionType=DebugActionType.Action,allowedGameStates=AllowedGameStates.PlayingOnMap)]
        public static void Recent(){Find.WindowStack.Add(new Dialog_MessageBox(NJRTestRecorderR030.RecentEvents()));}
    }
}
