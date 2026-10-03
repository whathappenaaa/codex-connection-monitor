using System.Diagnostics;

namespace ConnectionMonitor {
public static class MonitorTiming {
    public const int LogIntervalMs=500, ProbeIntervalMs=3000, ProbeTimeoutMs=2000, ProbeFreshSeconds=8;
    static readonly Stopwatch clock=Stopwatch.StartNew();
    public static long Milliseconds {get{return clock.ElapsedMilliseconds;}}
}
public static class ConnectionRules {
    static DisplayState Down(string zh,string en){return new DisplayState("error",Locale.Pick("连接中断","Disconnected"),Locale.Pick(zh,en));}
    static DisplayState Up(string zh,string en){return new DisplayState("ok",Locale.Pick("未检测到断开","No disconnect detected"),Locale.Pick(zh,en));}
    static DisplayState Unknown(string zh,string en){return new DisplayState("unknown",Locale.Pick("无法确认连接","Connection unconfirmed"),Locale.Pick(zh,en));}
    public static bool Fresh(ProbeResult probe,long now){return probe!=null && probe.Time>0 && now>=probe.Time && now-probe.Time<=MonitorTiming.ProbeFreshSeconds;}
    public static DisplayState Evaluate(ThreadState task,bool sourceOk,bool? networkAvailable,ProbeResult internet,ProbeResult service,long now,long networkChangedAt=0){
        if(networkAvailable==false)return Down("Windows 报告本机无可用网络连接。","Windows reports no available network connection.");
        Evidence last=task==null?null:task.Last;
        // An unresolved task failure cannot be cleared by an unrelated HTTP response.
        if(last!=null && last.Kind=="error" && last.Time<=now+10)
            return Down("Codex："+Locale.T(last.Reason)+"；等待该任务的恢复记录。","Codex: "+Locale.T(last.Reason)+". Waiting for this task to recover.");
        bool serviceFresh=Fresh(service,now),publicFresh=Fresh(internet,now);
        if(serviceFresh && !service.Reached)return Down("ChatGPT 入口探测未成功，正在复测；这不是对话断线的直接证据。","ChatGPT endpoint probe failed; retrying. This is not direct evidence of a broken chat stream.");
        if(publicFresh && !internet.Reached && !(serviceFresh && service.Reached))
            return Down("公共网络入口探测未成功，正在复测。","Public endpoint probe failed; retrying.");
        if(!sourceOk)return Unknown("任务日志暂不可读；网络探测仍继续。","Task logs cannot be read; network probes continue.");
        if(last==null)return Unknown("尚未识别任务或没有任务记录，可在上方选择任务。","No identified task or task evidence. Choose a task above.");
        if(last.Time>now+10)return Unknown("任务日志时间异常，暂不判断。","Task log timestamps are invalid.");
        if(last.Kind!="response" && last.Kind!="connected" && last.Kind!="complete")
            return Unknown("没有可识别的连接证据。","No recognized connection evidence.");
        if(networkChangedAt>0 && last.Time<=networkChangedAt && !(serviceFresh && service.Reached))
            return Unknown("网络已变化，正在重新确认连接。","Network changed; checking connectivity again.");
        if(service!=null && service.Time>0 && !service.Reached && !serviceFresh && last.Time<=service.Time)
            return Unknown("上次探测失败且结果已过期，等待新证据。","The failed probe is stale; waiting for new evidence.");
        if(now-last.Time<=60)return Up("当前任务没有未恢复的中断记录，最近有成功记录。","No unresolved task interruption; recent successful activity was recorded.");
        if(serviceFresh && service.Reached)return Up("未发现任务中断，ChatGPT 入口有响应；空闲或思考不会单独变色。","No task interruption recorded; ChatGPT endpoint responded. Idle or thinking alone does not change color.");
        return Unknown("正在等待新的探测结果；已有证据过期。","Waiting for a new probe; existing evidence has expired.");
    }
}
}
