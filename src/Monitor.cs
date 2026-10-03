using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace ConnectionMonitor {
public class Evidence {
    public long Id, Time;
    public string Thread, Kind, Reason;
}
public class ThreadState {
    public string Id;
    public Evidence Last, Failure;
    public long LastResponse;
    public void Apply(Evidence e) {
        if (Last != null && (e.Time < Last.Time || (e.Time == Last.Time && e.Id <= Last.Id))) return;
        Last = e;
        if (e.Kind == "error") Failure = e;
        if (e.Kind == "response" || e.Kind == "complete") LastResponse = e.Time;
    }
}
public class DisplayState {
    public string Code, Title, Detail;
    public DisplayState(string code, string title, string detail) { Code=code; Title=title; Detail=detail; }
}
public static class Rules {
    public static DisplayState Evaluate(ThreadState s, long now, bool sourceOk) {
        if (!sourceOk) return new DisplayState("unknown", "监测数据不可用", "暂时读不到 Codex 日志，不能判断对话连接。 ");
        if (s == null || s.Last == null) return new DisplayState("unknown", "等待任务活动", "尚未捕捉到当前任务的连接记录。 ");
        Evidence e=s.Last; long age=now-e.Time;
        if (age < -10) return new DisplayState("unknown", "时间记录异常", "日志时间与电脑时钟不一致，暂不判断连接。 ");
        if (e.Kind=="error") {
            if (age>120) return new DisplayState("warning", "上次连接失败", "尚未看到恢复记录；当前是否仍断开未知。 ");
            return new DisplayState("error", "连接中断 / 重试中", e.Reason);
        }
        if (e.Kind=="complete") return new DisplayState("idle", "本轮已结束", "本轮正常结束；下一次发送时再确认连接。 ");
        if (age>60) return new DisplayState("unknown", "等待新的连接证据", "可能正在思考、执行工具或空闲，不能据此判为断网。 ");
        if (e.Kind=="response") return new DisplayState("ok", "最近收到模型回应", "当前任务有新的模型输出，连接曾成功传输。 ");
        return new DisplayState("ok", "最近建立了连接", "Codex 已连上回应通道，等待后续输出。 ");
    }
    public static string Age(long timestamp) {
        if(timestamp<=0) return "尚无记录";
        long s=Math.Max(0,Now()-timestamp);
        if(s<60) return s+" 秒前";
        if(s<3600) return (s/60)+" 分钟前";
        return (s/3600)+" 小时前";
    }
    public static long Now() { return (long)(DateTime.UtcNow-new DateTime(1970,1,1)).TotalSeconds; }
}

// Only fixed event labels are returned from SQLite. No prompts, cookies, or headers leave the reader.
public sealed class LogDatabase : IDisposable {
    IntPtr db;
    [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_open_v2(byte[] path,out IntPtr db,int flags,IntPtr vfs);
    [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_close(IntPtr db);
    [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_busy_timeout(IntPtr db,int milliseconds);
    [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_prepare_v2(IntPtr db,byte[] sql,int length,out IntPtr stmt,IntPtr tail);
    [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_step(IntPtr stmt);
    [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_finalize(IntPtr stmt);
    [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] static extern IntPtr sqlite3_column_text(IntPtr stmt,int column);
    [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_column_bytes(IntPtr stmt,int column);
    public LogDatabase(string path) {
        int result=sqlite3_open_v2(Encoding.UTF8.GetBytes(path+"\0"),out db,1,IntPtr.Zero);
        if(result!=0) { Dispose(); throw new IOException("无法只读打开日志数据库"); }
        sqlite3_busy_timeout(db,150);
    }
    public List<string[]> Query(string sql) {
        IntPtr stmt; byte[] bytes=Encoding.UTF8.GetBytes(sql+"\0");
        if(sqlite3_prepare_v2(db,bytes,bytes.Length,out stmt,IntPtr.Zero)!=0) throw new IOException("日志格式不兼容");
        try {
            List<string[]> result=new List<string[]>(); int rc;
            while((rc=sqlite3_step(stmt))==100) {
                string[] row=new string[6];
                for(int i=0;i<6;i++) { IntPtr p=sqlite3_column_text(stmt,i); int n=sqlite3_column_bytes(stmt,i); byte[] b=new byte[n]; if(n>0) Marshal.Copy(p,b,0,n); row[i]=Encoding.UTF8.GetString(b); }
                result.Add(row);
            }
            if(rc!=101) throw new IOException("日志暂时被占用");
            return result;
        } finally { sqlite3_finalize(stmt); }
    }
    public void Dispose() { if(db!=IntPtr.Zero) { sqlite3_close(db); db=IntPtr.Zero; } }
    public const string Classify=@"CASE
WHEN target='codex_core::responses_retry' AND (instr(feedback_log_body,'stream disconnected')>0 OR instr(feedback_log_body,'stream connection failed')>0 OR instr(feedback_log_body,'waiting to retry')>0) THEN 'error'
WHEN target='codex_api::endpoint::responses_websocket' AND instr(feedback_log_body,'failed to connect to websocket:')>0 THEN 'error'
WHEN target='codex_api::endpoint::responses_websocket' AND instr(feedback_log_body,'successfully connected to websocket:')>0 THEN 'connected'
WHEN target='codex_core::stream_events_utils' AND level='DEBUG' AND instr(feedback_log_body,'Output item item_type=')>0 THEN 'response'
WHEN target='codex_core::session::turn' AND instr(feedback_log_body,'post sampling token usage')>0 AND instr(feedback_log_body,' needs_follow_up=false')>0 THEN 'complete'
ELSE '' END";
    public const string Reason=@"CASE
WHEN instr(feedback_log_body,'tls handshake eof')>0 THEN 'TLS 握手被中断'
WHEN instr(feedback_log_body,'10054')>0 THEN '连接被对端或链路设备重置 (10054)'
WHEN instr(feedback_log_body,'close_notify')>0 THEN 'TLS 连接意外关闭'
WHEN instr(feedback_log_body,'timed out')>0 THEN '连接超时'
WHEN instr(feedback_log_body,'error sending request')>0 THEN '请求发送失败'
ELSE '回应流中断，Codex 正在尝试恢复' END";
}
public class LogReader {
    public Dictionary<string,ThreadState> States=new Dictionary<string,ThreadState>();
    public List<string> History=new List<string>();
    public bool Ok;
    public string Error="正在读取日志", ActiveThread="", DatabasePath="";
    public TaskCatalog Catalog=new TaskCatalog();
    public long Checked;
    long cursor=-1;
    DateTime discovery=DateTime.MinValue;
    string desktopFile="";
    public bool DesktopLogAvailable {get{return File.Exists(desktopFile);}}
    public bool DesktopLogPackaged {get{return DesktopLogSource.IsPackaged(desktopFile);}}
    readonly DesktopActivityReader activity=new DesktopActivityReader();
    static readonly Regex ActivePattern=new Regex(@"thread_stream_view_activity_changed active=true conversationId=([a-fA-F0-9-]{36})\b");
    public static string ActiveFromLine(string line) { Match m=ActivePattern.Match(line); return m.Success?m.Groups[1].Value:""; }
    public LogReader Snapshot(){
        var copy=new LogReader {Ok=Ok,Error=Error,ActiveThread=ActiveThread,DatabasePath=DatabasePath,Checked=Checked,desktopFile=desktopFile};
        copy.States=States.ToDictionary(pair=>pair.Key,pair=>new ThreadState {Id=pair.Value.Id,Last=pair.Value.Last,Failure=pair.Value.Failure,LastResponse=pair.Value.LastResponse});
        copy.History=new List<string>(History);
        copy.Catalog.Items=new Dictionary<string,TaskInfo>(Catalog.Items);copy.Catalog.Available=Catalog.Available;
        return copy;
    }
    public static string EventsQuery(long after,long through){return "SELECT id,ts,thread_id,"+LogDatabase.Classify+","+LogDatabase.Reason+",'' FROM logs WHERE id>"+after+" AND id<="+through+" AND thread_id IS NOT NULL AND target IN ('codex_core::responses_retry','codex_api::endpoint::responses_websocket','codex_core::stream_events_utils','codex_core::session::turn') AND ("+LogDatabase.Classify+")<>'' ORDER BY id DESC LIMIT 4000";}
    public void Poll() {
        try {
            string codex=Environment.GetEnvironmentVariable("CODEX_HOME");
            if(String.IsNullOrEmpty(codex)) codex=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex");
            Catalog.Refresh(codex);
            if((DateTime.UtcNow-discovery).TotalSeconds>=2) {
                discovery=DateTime.UtcNow;
                string found=Directory.GetFiles(codex,"logs_*.sqlite").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if(found==null) throw new IOException("没有找到 Codex 日志数据库");
                if(found!=DatabasePath) { DatabasePath=found; cursor=-1; States.Clear(); }
                string f=DesktopLogSource.Find(DesktopLogSource.Roots(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
                if(f!=null)desktopFile=f;
            }
            ReadActive();
            using(LogDatabase db=new LogDatabase(DatabasePath)) {
                long max=Int64.Parse(db.Query("SELECT COALESCE(MAX(id),0),'','','','','' FROM logs")[0][0]);
                if(cursor<0 || cursor>max) { cursor=Math.Max(0,max-10000); States.Clear(); }
                List<string[]> rows=db.Query(EventsQuery(cursor,max));rows.Reverse();
                foreach(string[] row in rows) {
                    cursor=Int64.Parse(row[0]); if(row[3]=="") continue;
                    Evidence e=new Evidence { Id=cursor,Time=Int64.Parse(row[1]),Thread=row[2],Kind=row[3],Reason=row[4] };
                    if(!States.ContainsKey(e.Thread)) States[e.Thread]=new ThreadState { Id=e.Thread };
                    States[e.Thread].Apply(e);
                    if(e.Kind=="error" && Rules.Now()-e.Time<3600) {
                        string h=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(e.Time).ToLocalTime().ToString("HH:mm:ss")+"  "+e.Reason+"  ["+e.Thread.Substring(0,8)+"]";
                        History.Insert(0,h); if(History.Count>30) History.RemoveAt(30);
                    }
                }
                cursor=max;
            }
            Ok=true; Error=""; Checked=Rules.Now();
        } catch(Exception e) { Ok=false; Error=e is IOException?e.Message:"日志读取暂不可用"; Checked=Rules.Now(); }
    }
    void ReadActive(){activity.Poll(desktopFile);ActiveThread=activity.ActiveThread;}
    public ThreadState Selected(string selected) {
        string id=selected;
        if(id=="") id=ActiveThread;
        if(id!="") return States.ContainsKey(id)?States[id]:new ThreadState { Id=id };
        return null;
    }
}

public class ProbeResult {
    public bool Reached;
    public int Status;
    public long Ms, Time;
    public string Error="", Route="系统代理设置";
    public string Text { get { return Time==0?"检测中…":Reached?"HTTP "+Status+"  ·  "+Ms+" ms":Error; } }
}
public static class Program {
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    static Mutex mutex;
    [STAThread] public static void Main(string[] args) {
        ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
        if(args.Length>=2 && args[0]=="--assets") {Brand.Export(args[1]);return;}
        if(args.Length>=2 && args[0]=="--self-test") {Tests.Run(args[1]);return;}
        if(args.Length>=2 && args[0]=="--usage-check") {var state=new UsageState();using(var client=new UsageClient())client.Refresh(state).GetAwaiter().GetResult();File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(new {status=state.Error,updated=state.Updated,windows=state.Windows}),Encoding.UTF8);Environment.ExitCode=state.Error==""?0:1;return;}
        if(args.Length>=2 && args[0]=="--diagnose") {
            LogReader r=new LogReader();r.Poll();ThreadState s=r.Selected("");
            ProbeResult[] probes=Task.WhenAll(Probes.Run("https://www.microsoft.com/favicon.ico"),Probes.Run("https://chatgpt.com/")).GetAwaiter().GetResult();
            File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(new {sourceOk=r.Ok,sourceError=r.Error,desktopLogAvailable=r.DesktopLogAvailable,desktopLogPackaged=r.DesktopLogPackaged,activeThread=r.ActiveThread,taskName=s==null?"":r.Catalog.Name(s.Id),catalogAvailable=r.Catalog.Available,trackedThreads=r.States.Count,status=Rules.Evaluate(s,Rules.Now(),r.Ok),connectionStatus=ConnectionRules.Evaluate(s,r.Ok,NetworkInterface.GetIsNetworkAvailable(),probes[0],probes[1],Rules.Now()),lastResponse=s==null?0:s.LastResponse,publicProbe=probes[0],chatgptProbe=probes[1]}),Encoding.UTF8);return;
        }
        string capture=args.Length>=2 && args[0]=="--capture"?args[1]:null;
        bool test=args.Length>=2 && (args[0]=="--ui-test" || args[0]=="--layout-test");
        if(test)Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        if(capture==null && !test) {bool created;mutex=new Mutex(true,"Local\\CodexConnectionMonitor_"+Environment.UserName,out created);if(!created){MessageBox.Show("连接灯已在运行，请双击托盘图标。\nThe monitor is running. Double-click its tray icon.","Codex Link");return;}}
        if(args.Contains("--en"))Locale.English=true;
        SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        MonitorForm form=new MonitorForm(capture,test);
        if(args.Length>=2 && args[0]=="--ui-test")form.Shown+=(s,e)=>form.RunUiTest(args[1]);
        if(args.Length>=2 && args[0]=="--layout-test")form.Shown+=(s,e)=>form.RunLayoutTest(args[1]);
        try{Application.Run(form);}catch(Exception e){if(!test)throw;string path=args[0]=="--layout-test"?Path.Combine(args[1],"result.txt"):args[1];File.WriteAllText(path,"FAIL: "+e.ToString(),Encoding.UTF8);Environment.ExitCode=1;}
        if(mutex!=null){mutex.ReleaseMutex();mutex.Dispose();}
    }
}
public static class Tests {
    static int count;
    static void Check(bool condition,string name) {if(!condition)throw new Exception("FAIL "+name);count++;}
    public static void Run(string output) {
        try {
            long now=Rules.Now();ThreadState s=new ThreadState {Id="a"};
            Check(Rules.Evaluate(null,now,true).Code=="unknown","missing evidence");
            s.Apply(new Evidence {Id=1,Time=now,Kind="error",Reason="TLS"});
            Check(Rules.Evaluate(s,now,true).Code=="error","fresh error");
            Check(Rules.Evaluate(s,now+121,true).Code=="warning","stale error never green");
            s.Apply(new Evidence {Id=2,Time=now+1,Kind="connected"});
            Check(Rules.Evaluate(s,now+2,true).Code=="ok","recovery clears failure");
            Check(s.Failure!=null,"failure retained in history");
            s.Apply(new Evidence {Id=3,Time=now+2,Kind="response"});
            Check(s.LastResponse==now+2,"response timestamp");
            s.Apply(new Evidence {Id=0,Time=now-10,Kind="error"});
            Check(s.Last.Kind=="response","out of order ignored");
            Check(Rules.Evaluate(s,now+70,true).Code=="unknown","silence not disconnection");
            Check(Rules.Evaluate(s,now+3,false).Code=="unknown","reader failure not green");
            Check(Rules.Evaluate(s,now-20,true).Code=="unknown","future clock");
            s.Apply(new Evidence {Id=4,Time=now+3,Kind="complete"});
            Check(Rules.Evaluate(s,now+900,true).Code=="idle","completed stays idle");
            ThreadState b=new ThreadState {Id="b"};b.Apply(new Evidence {Id=5,Time=now+4,Kind="error"});
            Check(Rules.Evaluate(s,now+5,true).Code=="idle","other thread failure isolated");
            string id="00000000-0000-0000-0000-000000000001";
            Check(LogReader.ActiveFromLine("thread_stream_view_activity_changed active=true conversationId="+id+" rendererWindowId=1")==id,"active task parse");
            Check(LogReader.ActiveFromLine("thread_stream_view_activity_changed active=false conversationId="+id)=="","inactive task ignored");
            using(LogDatabase db=new LogDatabase(":memory:")) {
                string fixture="WITH logs(id,ts,thread_id,target,level,feedback_log_body) AS (VALUES "+
                "(1,1,'a','codex_core::responses_retry','WARN','stream disconnected - retrying sampling request tls handshake eof'),"+
                "(2,2,'a','codex_api::endpoint::responses_websocket','INFO','successfully connected to websocket: test'),"+
                "(3,3,'a','codex_core::stream_events_utils','INFO','ToolCall: quoted stream disconnected - retrying sampling request'),"+
                "(4,4,'a','codex_core::stream_events_utils','DEBUG','Output item item_type=reasoning'),"+
                "(5,5,'a','codex_core::session::turn','TRACE','post sampling token usage model_needs_follow_up=false needs_follow_up=true'),"+
                "(6,6,'a','codex_core::session::turn','TRACE','post sampling token usage model_needs_follow_up=false needs_follow_up=false'),"+
                "(7,7,'b','codex_api::endpoint::responses_websocket','ERROR','failed to connect to websocket: os error 10054')) ";
                List<string[]> rows=db.Query(fixture+"SELECT id,ts,thread_id,"+LogDatabase.Classify+","+LogDatabase.Reason+",'' FROM logs ORDER BY id");
                Check(rows[0][3]=="error" && rows[0][4]=="TLS 握手被中断","TLS retry classification");
                Check(rows[1][3]=="connected","websocket connected classification");
                Check(rows[2][3]=="","quoted tool text cannot spoof failure");
                Check(rows[3][3]=="response","model output classified");
                Check(rows[4][3]=="","model flag alone not completed turn");
                Check(rows[5][3]=="complete","complete sampling classified");
                Check(rows[6][3]=="error" && rows[6][4].Contains("10054"),"connection reset classification");
                LogReader reader=new LogReader();reader.States["a"]=s;reader.States["b"]=b;reader.ActiveThread="a";
                Check(reader.Selected("").Id=="a","follow active task");
                Check(reader.Selected("b").Id=="b","pin task");
                reader.ActiveThread="unseen";
                Check(reader.Selected("").Last==null,"unknown active task does not borrow other evidence");
                reader.ActiveThread="";Check(reader.Selected("")==null,"no silent fallback to a different task");
                Check(TaskCatalog.ChooseName("Sidebar name","Original user prompt")=="Sidebar name","sidebar name takes priority over initial prompt");
                Check(TaskCatalog.ChooseName("","# Files mentioned by the user:\nprivate prompt")=="","do not use prompt as task title");
                Locale.English=true;Check(Locale.T("本轮已结束")=="Turn completed","English state translation");Locale.English=false;
                Check(WindowSizes.Normalize("obsolete")=="small","unknown saved size defaults to compact");
                Check(WindowSizes.Width("small")<WindowSizes.Width("medium") && WindowSizes.Width("medium")<WindowSizes.Width("large"),"preset widths ordered");
                Check(WindowSizes.FitHeight(900,540,700,300)==540,"content cannot grow small mode past its limit");
                Check(WindowSizes.FitHeight(900,790,460,300)==460,"small screen constrains window height");
                Check(WindowSizes.FitHeight(330,540,700,300)==330,"auto fit can shrink to content");
                MonitorSettings legacy=new JavaScriptSerializer().Deserialize<MonitorSettings>("{\"language\":\"en\"}");
                Check(legacy.size=="small" && legacy.autoFit && legacy.language=="en","old settings migrate without losing language");
                var codec=new JavaScriptSerializer();MonitorSettings roundTrip=codec.Deserialize<MonitorSettings>(codec.Serialize(new MonitorSettings {language="en",size="medium",autoFit=false}));
                Check(roundTrip.language=="en" && roundTrip.size=="medium" && !roundTrip.autoFit,"language and size settings persist together");
            }
            int featureCount=FeatureTests.Run();int desktopCount=DesktopLogTests.Run();int connectionCount=ConnectionTests.Run();File.WriteAllText(output,"PASS "+count+" legacy + "+featureCount+" quota + "+desktopCount+" desktop log + "+connectionCount+" connection/timing tests",Encoding.UTF8);
        } catch(Exception e) {File.WriteAllText(output,e.ToString(),Encoding.UTF8);Environment.ExitCode=1;}
    }
}
}
