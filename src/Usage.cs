using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ConnectionMonitor {
public sealed class UsageWindow {
    public string Bucket, Name, Slot;
    public double? Remaining;
    public int? Minutes;
    public long? ResetsAt;
    public string Period { get { return Minutes==10080?Locale.Pick("周额度","Weekly"):Minutes.HasValue?Minutes.Value%60==0?Minutes.Value/60+Locale.Pick(" 小时额度","h limit"):Minutes+Locale.Pick(" 分钟额度","m limit"):Locale.Pick("额度周期","Quota window"); } }
    public string Percent { get { return Remaining.HasValue?Remaining.Value.ToString("0.#",CultureInfo.InvariantCulture)+"%":Locale.Pick("暂无数据","Unavailable"); } }
    public string Reset(long now,bool full) {
        if(!ResetsAt.HasValue)return Locale.Pick("重置时间未知","Reset time unavailable");
        if(ResetsAt.Value<=now)return Locale.Pick("已到重置时间，待确认","Reset due; awaiting update");
        try {return new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(ResetsAt.Value).ToLocalTime().ToString(full?"yyyy/MM/dd HH:mm":"MM/dd HH:mm")+Locale.Pick(" 重置"," reset");}
        catch{return Locale.Pick("重置时间未知","Reset time unavailable");}
    }
}
public sealed class UsageState {
    public List<UsageWindow> Windows=new List<UsageWindow>();
    public long Updated,Attempted;
    public string Error="loading";
    string account="";
    public bool Busy;
    public void Account(string identity) {if(account!=identity){account=identity;Windows.Clear();Updated=0;}}
    public void Fail(string error) {Error=error;}
    public void Success(List<UsageWindow> windows,long now) {Windows=windows;Updated=now;Error=windows.Count==0?"empty":"";}
    public bool Due(long now,bool manual) {return !Busy && (Attempted==0 || now-Attempted>=(manual?10:60) || (!manual && now-Attempted>=10 && Windows.Any(w=>w.ResetsAt.HasValue && w.ResetsAt.Value<=now && Attempted<w.ResetsAt.Value)));}
    public bool Stale(long now) {return Updated>0 && now-Updated>120;}
    public string Status(long now) {
        string text=UsageText.Error(Error);
        if(Updated>0 && (Error!="" || Stale(now)))return Locale.Pick("旧数据 · ","Old data · ")+text+Locale.Pick(" · 更新于 "," · Updated ")+Locale.Age(Updated);
        return text!=""?text:Locale.Pick("更新于 ","Updated ")+Locale.Age(Updated);
    }
    public string Summary(long now) {
        UsageWindow w=Windows.FirstOrDefault(x=>x.Bucket=="codex")??Windows.FirstOrDefault();
        if(w==null)return Locale.Pick("额度：","Quota: ")+UsageText.Error(Error);
        return (Error!="" || Stale(now)?Locale.Pick("旧额度 ","Old quota "):"")+w.Period+" "+w.Percent;
    }
}
public static class UsageText {
    public static string Error(string code) {switch(code){
        case "":return "";
        case "loading":return Locale.Pick("正在读取额度…","Reading quota…");
        case "missing":return Locale.Pick("未找到 Codex，可在托盘菜单指定路径","Codex not found; select its path in the tray menu");
        case "login":return Locale.Pick("请先在本机 Codex 登录","Sign in to local Codex first");
        case "unsupported":return Locale.Pick("当前登录方式或 Codex 版本不支持额度读取","Quota is unavailable for this sign-in mode or Codex version");
        case "timeout":return Locale.Pick("额度更新超时","Quota update timed out");
        case "empty":return Locale.Pick("账号暂未返回额度数据","No quota data returned");
        default:return Locale.Pick("额度更新失败","Quota update failed");
    }}
}
public static class UsageParser {
    public static Dictionary<string,object> Object(object value) {return value as Dictionary<string,object>??new Dictionary<string,object>();}
    public static object Get(Dictionary<string,object> value,string key) {object result;return value.TryGetValue(key,out result)?result:null;}
    public static string Text(Dictionary<string,object> value,string key) {return Convert.ToString(Get(value,key),CultureInfo.InvariantCulture);}
    static double? Number(Dictionary<string,object> value,string key) {double n;object raw=Get(value,key);return raw!=null && !(raw is bool) && Double.TryParse(Convert.ToString(raw,CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture,out n) && !Double.IsNaN(n) && !Double.IsInfinity(n)?(double?)n:null;}
    public static List<UsageWindow> Parse(Dictionary<string,object> result) {
        var windows=new List<UsageWindow>();var buckets=Object(Get(result,"rateLimitsByLimitId"));
        if(buckets.Count==0){var legacy=Object(Get(result,"rateLimits"));if(legacy.Count>0)buckets=new Dictionary<string,object>{{String.IsNullOrEmpty(Text(legacy,"limitId"))?"codex":Text(legacy,"limitId"),legacy}};}
        foreach(var entry in buckets.OrderBy(x=>x.Key=="codex"?0:1).ThenBy(x=>x.Key)) {
            var bucket=Object(entry.Value);string name=Text(bucket,"limitName");
            foreach(string slot in new[]{"primary","secondary"}) {
                var window=Object(Get(bucket,slot));if(window.Count==0)continue;
                double? used=Number(window,"usedPercent"),minutes=Number(window,"windowDurationMins"),reset=Number(window,"resetsAt");
                windows.Add(new UsageWindow {Bucket=entry.Key,Name=String.IsNullOrEmpty(name)?entry.Key:name,Slot=slot,
                    Remaining=used.HasValue?(double?)Math.Max(0,Math.Min(100,100-used.Value)):null,
                    Minutes=minutes.HasValue && minutes>0 && minutes<=Int32.MaxValue?(int?)minutes.Value:null,
                    ResetsAt=reset.HasValue && reset>0 && reset<253402300800L?(long?)reset.Value:null});
            }
        }return windows;
    }
}
public sealed class UsageException : Exception {
    public readonly string Code;
    public UsageException(string code):base(code){Code=code;}
}
// This client exchanges only account queries with the official local Codex process.
// Raw responses, credentials, account identifiers and stderr are never logged or persisted.
public sealed class UsageClient : IDisposable {
    Process process;int sequence;bool disposed;readonly object sync=new object();
    readonly Dictionary<int,TaskCompletionSource<Dictionary<string,object>>> pending=new Dictionary<int,TaskCompletionSource<Dictionary<string,object>>>();
    readonly JavaScriptSerializer serializer=new JavaScriptSerializer {MaxJsonLength=4*1024*1024};
    public string ExecutablePath="";
    public static string Discover(string configured) {
        if(!String.IsNullOrWhiteSpace(configured))return File.Exists(configured) && String.Equals(Path.GetExtension(configured),".exe",StringComparison.OrdinalIgnoreCase)?Path.GetFullPath(configured):null;
        foreach(string folder in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator))try{string p=Path.Combine(folder.Trim('"'),"codex.exe");if(File.Exists(p))return p;}catch{}
        string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string bin=Path.Combine(local,"OpenAI","Codex","bin");
        if(Directory.Exists(bin))try{return Directory.GetFiles(bin,"codex.exe",SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();}catch{}
        string alias=Path.Combine(local,"Microsoft","WindowsApps","codex.exe");return File.Exists(alias)?alias:null;
    }
    public static ProcessStartInfo StartInfo(string executable) {
        var info=new ProcessStartInfo(executable,"app-server") {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=System.Text.Encoding.UTF8,StandardErrorEncoding=System.Text.Encoding.UTF8};
        // Reqwest does not inherit Windows' system proxy automatically. Bridge it for this child only.
        if(String.IsNullOrEmpty(Environment.GetEnvironmentVariable("HTTPS_PROXY")) && String.IsNullOrEmpty(Environment.GetEnvironmentVariable("https_proxy")) && String.IsNullOrEmpty(Environment.GetEnvironmentVariable("ALL_PROXY")) && String.IsNullOrEmpty(Environment.GetEnvironmentVariable("all_proxy")))try{
            var target=new Uri("https://chatgpt.com/");var proxy=WebRequest.DefaultWebProxy;Uri route=proxy==null?target:proxy.GetProxy(target);
            if(route!=null && route!=target && (route.Scheme=="http" || route.Scheme=="https"))info.EnvironmentVariables["HTTPS_PROXY"]=route.AbsoluteUri;
        }catch{}
        return info;
    }
    async Task EnsureStarted() {
        if(disposed)throw new UsageException("failed");
        if(process!=null && !process.HasExited)return;
        Stop();string executable=Discover(ExecutablePath);if(executable==null)throw new UsageException("missing");
        var child=new Process {StartInfo=StartInfo(executable)};process=child;
        child.OutputDataReceived+=(s,e)=>ReadLine(e.Data);child.ErrorDataReceived+=(s,e)=>{};
        child.EnableRaisingEvents=true;child.Exited+=(s,e)=>RejectPending();
        child.Start();child.BeginOutputReadLine();child.BeginErrorReadLine();
        await Call("initialize",new {clientInfo=new {name="codex_connection_monitor",title="Codex Connection Monitor",version="1.3.1"}});
        child.StandardInput.WriteLine(serializer.Serialize(new {method="initialized",@params=new {}}));child.StandardInput.Flush();
    }
    void ReadLine(string line) {
        if(String.IsNullOrEmpty(line))return;
        try {
            var response=new JavaScriptSerializer {MaxJsonLength=4*1024*1024}.Deserialize<Dictionary<string,object>>(line);int id; if(!Int32.TryParse(UsageParser.Text(response,"id"),out id))return;
            TaskCompletionSource<Dictionary<string,object>> completion;lock(sync){if(!pending.TryGetValue(id,out completion))return;pending.Remove(id);}
            var error=UsageParser.Object(UsageParser.Get(response,"error"));
            if(error.Count>0){string message=UsageParser.Text(error,"message");completion.TrySetException(new UsageException(UsageParser.Text(error,"code")=="-32601"?"unsupported":message.IndexOf("not logged",StringComparison.OrdinalIgnoreCase)>=0?"login":"failed"));}
            else completion.TrySetResult(UsageParser.Object(UsageParser.Get(response,"result")));
        }catch{}
    }
    async Task<Dictionary<string,object>> Call(string method,object parameters) {
        int id=Interlocked.Increment(ref sequence);var completion=new TaskCompletionSource<Dictionary<string,object>>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock(sync)pending[id]=completion;
        try {
            process.StandardInput.WriteLine(serializer.Serialize(new {id=id,method=method,@params=parameters}));process.StandardInput.Flush();
            if(await Task.WhenAny(completion.Task,Task.Delay(10000))!=completion.Task){Stop();throw new UsageException("timeout");}
            return await completion.Task;
        }finally{lock(sync)pending.Remove(id);}
    }
    public async Task Refresh(UsageState state) {
        try {
            await EnsureStarted();var result=await Call("account/read",new {refreshToken=false});var account=UsageParser.Object(UsageParser.Get(result,"account"));
            string type=UsageParser.Text(account,"type");
            if(account.Count==0){state.Account("");throw new UsageException("login");}
            state.Account(type+"|"+UsageParser.Text(account,"email")+"|"+UsageParser.Text(account,"accountId")+"|"+UsageParser.Text(account,"chatgptAccountId"));
            if(type!="chatgpt"){state.Account("");throw new UsageException("unsupported");}
            var limits=await Call("account/rateLimits/read",null);state.Success(UsageParser.Parse(limits),Rules.Now());
        }catch(UsageException e){state.Fail(e.Code);}catch{state.Fail("failed");}
        finally{Stop();} // A fresh child observes sign-in and system-proxy changes on the next refresh.
    }
    void RejectPending(){lock(sync){foreach(var c in pending.Values)c.TrySetException(new UsageException("failed"));pending.Clear();}}
    void Stop(){var child=process;process=null;if(child!=null){try{if(!child.HasExited)child.Kill();}catch{}try{child.WaitForExit(1000);}catch{}child.Dispose();}RejectPending();}
    public void Dispose(){disposed=true;Stop();}
}
public static class TrayRules {
    public static DisplayState Evaluate(DisplayState task,bool? networkAvailable,ProbeResult internet,ProbeResult service,long now) {
        if(networkAvailable==false)return new DisplayState("error",Locale.Pick("本机无可用网络连接","No local network connection"),"");
        if(task.Code=="error")return task;
        bool a=Failed(internet,now),b=Failed(service,now);
        if(a||b)return new DisplayState("warning",a&&b?Locale.Pick("两个入口探测异常","Both endpoint probes failed"):a?Locale.Pick("公共入口探测异常","Public endpoint probe failed"):Locale.Pick("ChatGPT 入口探测异常","ChatGPT endpoint probe failed"),"");
        return task;
    }
    static bool Failed(ProbeResult result,long now){return result!=null && result.Time>0 && now-result.Time>=0 && now-result.Time<=35 && !result.Reached;}
}
}
