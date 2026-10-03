using System;
using System.Threading;
using System.Threading.Tasks;
using System.Net.NetworkInformation;

namespace ConnectionMonitor {
public partial class MonitorForm {
    readonly LogReader logSource=new LogReader();
    Func<LogReader> readLogsForTest;
    Func<bool,CancellationToken,Task<ProbeResult>> probeForTest;
    CancellationTokenSource probeCancellation=new CancellationTokenSource();
    int networkGeneration;
    long networkChangedAt;
    void Poll(){
        if(monitoringClosed)return;
        PollLogs();PollProbes();PollUsage(false);
    }
    async void PollLogs(){
        long now=MonitorTiming.Milliseconds;
        if(logBusy || (lastLog!=0 && now-lastLog<MonitorTiming.LogIntervalMs))return;
        logBusy=true;lastLog=now;
        try {
            // All filesystem/SQLite work is isolated from the UI's last snapshot.
            // An unavailable or slow log can never hold the network indicator lock.
            LogReader next=await Task.Run(()=>{if(readLogsForTest!=null)return readLogsForTest();logSource.Poll();return logSource.Snapshot();});
            if(!monitoringClosed && !IsDisposed){reader=next;RefreshView();}
        } catch {if(!monitoringClosed && !IsDisposed){reader.Ok=false;reader.Error="日志读取暂不可用";RefreshView();}}finally {logBusy=false;}
    }
    async void PollProbes(){
        long now=MonitorTiming.Milliseconds;
        if(probeBusy || networkAvailable==false || (lastProbe!=0 && now-lastProbe<MonitorTiming.ProbeIntervalMs))return;
        probeBusy=true;lastProbe=now;int generation=networkGeneration;
        CancellationToken token=probeCancellation.Token;
        try {
            // Each endpoint publishes immediately instead of waiting for its peer.
            await Task.WhenAll(ProbeEndpoint(true,generation,token),ProbeEndpoint(false,generation,token));
            if(screenshot && !monitoringClosed && !IsDisposed && generation==networkGeneration){await Task.Delay(300);if(!IsDisposed){SaveCapture(capturePath);exit=true;Close();}}
        } finally {probeBusy=false;if(!monitoringClosed && generation!=networkGeneration)PollProbes();}
    }
    async Task ProbeEndpoint(bool isPublic,int generation,CancellationToken token){
        ProbeResult result=await (probeForTest!=null?probeForTest(isPublic,token):Probes.Run(isPublic?"https://www.microsoft.com/favicon.ico":"https://chatgpt.com/",MonitorTiming.ProbeTimeoutMs,token));
        if(!AcceptProbe(generation,networkGeneration,monitoringClosed) || IsDisposed)return;
        if(isPublic)internet=result;else service=result;
        RefreshView();
    }
    internal static bool AcceptProbe(int started,int current,bool closed){return !closed && started==current;}
    void NetworkAddressChanged(object sender,EventArgs args){bool? available;try{available=NetworkInterface.GetIsNetworkAvailable();}catch{available=null;}QueueNetworkChange(available);}
    void QueueNetworkChange(bool? available){
        if(monitoringClosed || !IsHandleCreated || IsDisposed)return;
        try {BeginInvoke((Action)(()=>{
            if(monitoringClosed)return;
            networkAvailable=available;networkGeneration++;networkChangedAt=Rules.Now();
            probeCancellation.Cancel();probeCancellation.Dispose();probeCancellation=new CancellationTokenSource();
            internet=new ProbeResult();service=new ProbeResult();lastProbe=0;
            RefreshView();Poll();
        }));}catch(InvalidOperationException){}
    }
}
}
