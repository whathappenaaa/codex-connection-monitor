using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConnectionMonitor {
public static class ConnectionTests {
    static int count;
    static void Check(bool condition,string name){if(!condition)throw new Exception("FAIL: "+name);count++;}
    static ThreadState State(string kind,long time){return new ThreadState {Id="fixture",Last=new Evidence {Id=1,Kind=kind,Time=time,Reason="TLS 握手被中断"}};}
    public static int Run(){
        count=0;long now=1000;
        var good=new ProbeResult {Reached=true,Status=200,Time=now};var denied=new ProbeResult {Reached=true,Status=403,Time=now};var failed=new ProbeResult {Reached=false,Time=now};
        foreach(string kind in new[]{"response","connected","complete"}){
            ThreadState task=State(kind,now);
            Check(ConnectionRules.Evaluate(task,true,false,good,good,now).Code=="error","system offline wins "+kind);
            Check(ConnectionRules.Evaluate(task,true,true,good,failed,now).Code=="error","service failure immediate red "+kind);
            Check(ConnectionRules.Evaluate(task,true,true,good,denied,now).Code=="ok","403 is reachable "+kind);
            Check(ConnectionRules.Evaluate(task,true,true,failed,denied,now).Code=="ok","public-only failure not Codex disconnect "+kind);
        }
        foreach(string kind in new[]{"response","connected","complete"})Check(ConnectionRules.Evaluate(State(kind,now-600),true,true,good,good,now).Code=="ok","thinking or idle does not add a state "+kind);
        var taskError=State("error",now-10000);
        Check(ConnectionRules.Evaluate(taskError,true,true,good,good,now).Code=="error","HTTP success never clears unresolved task failure");
        taskError.Apply(new Evidence {Id=2,Kind="connected",Time=now});
        Check(ConnectionRules.Evaluate(taskError,true,true,good,good,now).Code=="ok","new task success clears failure");
        Check(ConnectionRules.Evaluate(null,true,true,good,good,now).Code=="unknown","unidentified task not claimed healthy");
        Check(ConnectionRules.Evaluate(State("response",now),false,true,good,good,now).Code=="unknown","unreadable logs remain unconfirmed");
        Check(ConnectionRules.Evaluate(null,false,false,null,null,now).Code=="error","offline still visible without logs");
        Check(ConnectionRules.Evaluate(null,false,true,good,failed,now).Code=="error","probe failure still visible without logs");
        Check(ConnectionRules.Evaluate(State("response",now+11),true,true,good,good,now).Code=="unknown","future task time stays unknown");
        var stale=new ProbeResult {Reached=false,Time=now-MonitorTiming.ProbeFreshSeconds-1};
        Check(ConnectionRules.Evaluate(State("response",now),true,true,stale,stale,now).Code=="ok","old probe failure ignored");
        Check(ConnectionRules.Evaluate(State("response",now-600),true,true,stale,stale,now).Code=="unknown","all expired data not green");
        Check(ConnectionRules.Evaluate(State("response",now-20),true,true,stale,stale,now).Code=="unknown","expired failure does not reveal an older green state");
        Check(ConnectionRules.Evaluate(State("response",now-1),true,true,null,null,now,now).Code=="unknown","network recovery needs evidence after the network change");
        Check(ConnectionRules.Evaluate(State("response",now-1),true,true,good,good,now,now).Code=="ok","fresh post-change endpoint confirms recovery without task failure");
        Check(!ConnectionRules.Fresh(new ProbeResult {Time=now+1},now),"future probe ignored");
        Check(MonitorForm.AcceptProbe(2,2,false) && !MonitorForm.AcceptProbe(1,2,false) && !MonitorForm.AcceptProbe(2,2,true),"late network-generation results are discarded");
        var source=new LogReader();source.States["fixture"]=State("response",now);var snapshot=source.Snapshot();source.States["fixture"].Apply(new Evidence {Id=2,Time=now+1,Kind="error"});
        Check(snapshot.States["fixture"].Last.Kind=="response","UI snapshot detached from background log mutation");
        using(var db=new LogDatabase(":memory:")){
            string fixture="WITH RECURSIVE seq(n) AS (VALUES(1) UNION ALL SELECT n+1 FROM seq WHERE n<6000), logs(id,ts,thread_id,target,level,feedback_log_body) AS (SELECT n,1,'fixture','codex_core::session::turn','TRACE','unrelated diagnostic' FROM seq UNION ALL SELECT 6001,2,'fixture','codex_core::responses_retry','WARN','stream disconnected before completion') ";
            var rows=db.Query(fixture+LogReader.EventsQuery(0,6001));
            Check(rows.Count==1 && rows[0][0]=="6001" && rows[0][3]=="error","latest failure bypasses thousands of unrelated log rows");
        }
        ProbeTiming().GetAwaiter().GetResult();return count;
    }
    static async Task ProbeTiming(){
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();TcpClient peer=null;
        string url="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port+"/";
        try {
            Task<TcpClient> accept=listener.AcceptTcpClientAsync();var clock=Stopwatch.StartNew();Task<ProbeResult> request=Probes.Run(url,350,CancellationToken.None);
            peer=await accept;ProbeResult result=await request;
            Check(!result.Reached && result.Error=="探测超时" && clock.ElapsedMilliseconds<1800,"non-responsive socket bounded by independent deadline");
        }finally{if(peer!=null)peer.Close();listener.Stop();}
        listener=new TcpListener(IPAddress.Loopback,0);listener.Start();peer=null;url="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port+"/";
        try {
            Task<TcpClient> accept=listener.AcceptTcpClientAsync();Task<ProbeResult> request=Probes.Run(url,2000,CancellationToken.None);peer=await accept;
            byte[] headers=Encoding.ASCII.GetBytes("HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");await peer.GetStream().WriteAsync(headers,0,headers.Length);
            ProbeResult result=await request;Check(result.Reached && result.Status==403,"real HTTP 403 transport succeeds");
        }finally{if(peer!=null)peer.Close();listener.Stop();}
        listener=new TcpListener(IPAddress.Loopback,0);listener.Start();peer=null;url="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port+"/";
        try {using(var cancellation=new CancellationTokenSource()){
            Task<TcpClient> accept=listener.AcceptTcpClientAsync();Task<ProbeResult> request=Probes.Run(url,5000,cancellation.Token);peer=await accept;
            var clock=Stopwatch.StartNew();cancellation.Cancel();ProbeResult result=await request;
            Check(!result.Reached && clock.ElapsedMilliseconds<1500,"network-change cancellation does not wait for probe deadline");
        }}finally{if(peer!=null)peer.Close();listener.Stop();}
    }
}
}
