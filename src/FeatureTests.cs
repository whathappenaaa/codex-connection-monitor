using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;
namespace ConnectionMonitor {
public static class FeatureTests {
    static int count;
    static void Check(bool condition,string name){if(!condition)throw new Exception("FAIL: "+name);count++;}
    public static List<UsageWindow> Fixture(){return Parse("{\"rateLimitsByLimitId\":{\"codex\":{\"primary\":{\"usedPercent\":17,\"windowDurationMins\":300,\"resetsAt\":1999999999},\"secondary\":{\"usedPercent\":58,\"windowDurationMins\":10080,\"resetsAt\":2000009999}}}}");}
    static List<UsageWindow> Parse(string json){return UsageParser.Parse(new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(json));}
    public static int Run(){
        count=0;long now=1000;var good=new ProbeResult {Time=now,Reached=true,Status=200};var blocked=new ProbeResult {Time=now,Reached=true,Status=403};var bad=new ProbeResult {Time=now,Reached=false};var old=new ProbeResult {Time=now-36,Reached=false};var future=new ProbeResult {Time=now+3,Reached=false};
        foreach(string state in new[]{"ok","idle","unknown","warning","error"}){
            var task=new DisplayState(state,state,"");
            Check(TrayRules.Evaluate(task,false,good,good,now).Code=="error","offline wins over "+state);
            Check(TrayRules.Evaluate(task,true,good,blocked,now).Code==state,"HTTP 403 not transport error "+state);
            Check(TrayRules.Evaluate(task,true,bad,good,now).Code==(state=="error"?"error":"warning"),"probe failure priority "+state);
            Check(TrayRules.Evaluate(task,true,old,future,now).Code==state,"stale and future probes ignored "+state);
        }
        Check(TrayRules.Evaluate(new DisplayState("ok","ok",""),null,null,null,now).Code=="ok","unknown network does not erase model evidence");
        blocked.Status=401;Check(TrayRules.Evaluate(new DisplayState("idle","idle",""),true,blocked,good,now).Code=="idle","401 is a response");
        var windows=Fixture();Check(windows.Count==2 && windows[0].Remaining==83 && windows[1].Remaining==42,"two real periods");
        Check(windows[0].Minutes==300 && windows[1].Minutes==10080,"periods not hardcoded");
        Check(Parse("{}").Count==0,"missing bucket not full");
        Check(Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":100}}}")[0].Remaining==0,"zero remaining");
        Check(Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":0}}}")[0].Remaining==100,"full remaining");
        Check(Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":-5}}}")[0].Remaining==100,"clamp negative use");
        Check(Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":150}}}")[0].Remaining==0,"clamp above full use");
        var missing=Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":null,\"resetsAt\":null}}}")[0];
        Check(!missing.Remaining.HasValue && !missing.ResetsAt.HasValue && !missing.Minutes.HasValue,"null is unknown");
        Check(!Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":true}}}")[0].Remaining.HasValue,"malformed percent");
        Check(Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":20},\"secondary\":null}}").Count==1,"one window");
        var multi=Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":50}},\"rateLimitsByLimitId\":{\"other\":{\"primary\":{\"usedPercent\":40}},\"codex\":{\"primary\":{\"usedPercent\":10}}}}");
        Check(multi.Count==2 && multi[0].Bucket=="codex" && multi[0].Remaining==90,"prefer multi buckets, codex first");
        var stateData=new UsageState();Check(stateData.Due(now,false),"initial refresh");stateData.Account("account-a");stateData.Success(windows,now);stateData.Attempted=now;
        Check(!stateData.Due(now+9,true) && stateData.Due(now+10,true),"manual throttle");
        Check(!stateData.Due(now+59,false) && stateData.Due(now+60,false),"60 second refresh");
        stateData.Busy=true;Check(!stateData.Due(now+300,true),"no concurrent refresh");stateData.Busy=false;
        stateData.Fail("timeout");Check(stateData.Windows.Count==2 && stateData.Updated==now,"failure preserves snapshot");
        Check(!stateData.Stale(now+120) && stateData.Stale(now+121),"stale threshold");
        bool english=Locale.English;Locale.English=true;
        Check(stateData.Status(now+1).Contains("Old data") && stateData.Status(now+1).Contains("timed out"),"failure marked immediately");
        Check(missing.Reset(now,false).Contains("unavailable"),"missing reset shown");
        var resetWindow=new UsageWindow {Bucket="codex",Remaining=25,ResetsAt=now+30};stateData.Success(new List<UsageWindow>{resetWindow},now);
        Check(stateData.Due(now+30,false),"refresh on reset boundary");Check(resetWindow.Reset(now+30,false).Contains("awaiting") && resetWindow.Remaining==25,"no speculative refill");
        string expected=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(now+30).ToLocalTime().ToString("MM/dd HH:mm");Check(resetWindow.Reset(now,false).Contains(expected),"local reset timezone");
        stateData.Attempted=now+30;Check(!stateData.Due(now+40,false),"reset does not spin");
        stateData.Account("account-b");Check(stateData.Windows.Count==0 && stateData.Updated==0,"account switch clears snapshot");
        foreach(string error in new[]{"missing","login","unsupported","empty","timeout","failed"})Check(!String.IsNullOrEmpty(UsageText.Error(error)),"readable failure "+error);
        Locale.English=english;return count;
    }
}
}
