using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace ConnectionMonitor {
public static class Probes {
    static readonly SemaphoreSlim workers=new SemaphoreSlim(4);
    public static Task<ProbeResult> Run(string url){return Run(url,MonitorTiming.ProbeTimeoutMs,CancellationToken.None);}
    public static async Task<ProbeResult> Run(string url,int timeoutMs,CancellationToken cancellation){
        Stopwatch timer=Stopwatch.StartNew();
        using(var abort=CancellationTokenSource.CreateLinkedTokenSource(cancellation)){
            CancellationToken token=abort.Token;
            // DNS and system proxy discovery can block before GetResponseAsync
            // yields. Keep the deadline and caller/UI independent of that work.
            Task<ProbeResult> operation=Task.Run(async()=>{
                await workers.WaitAsync(token).ConfigureAwait(false);
                try{return await Fetch(url,token).ConfigureAwait(false);}finally{workers.Release();}
            });
            if(await Task.WhenAny(operation,Task.Delay(timeoutMs,cancellation)).ConfigureAwait(false)!=operation){
                abort.Cancel();ObserveFailure(operation);
                return new ProbeResult {Time=Rules.Now(),Ms=timer.ElapsedMilliseconds,Error=cancellation.IsCancellationRequested?"探测已取消":"探测超时",Route=Route(url)};
            }
            try {ProbeResult result=await operation.ConfigureAwait(false);result.Time=Rules.Now();result.Ms=timer.ElapsedMilliseconds;return result;}
            catch(OperationCanceledException){return new ProbeResult {Time=Rules.Now(),Ms=timer.ElapsedMilliseconds,Error="探测已取消",Route=Route(url)};}
            catch {return new ProbeResult {Time=Rules.Now(),Ms=timer.ElapsedMilliseconds,Error="探测不可用",Route=Route(url)};}
        }
    }
    static string ProxySetting {get{return Environment.GetEnvironmentVariable("HTTPS_PROXY")??Environment.GetEnvironmentVariable("https_proxy");}}
    static string Route(string url){Uri endpoint,proxy;if(Uri.TryCreate(url,UriKind.Absolute,out endpoint) && endpoint.IsLoopback)return "本机测试";return !String.IsNullOrEmpty(ProxySetting) && Uri.TryCreate(ProxySetting,UriKind.Absolute,out proxy) && (proxy.Scheme=="http" || proxy.Scheme=="https")?"HTTPS_PROXY 环境设置":"系统代理设置";}
    static async Task<ProbeResult> Fetch(string url,CancellationToken cancellation){
        ProbeResult result=new ProbeResult {Route=Route(url)};
        HttpWebRequest request=null;
        try {
            cancellation.ThrowIfCancellationRequested();
            request=(HttpWebRequest)WebRequest.Create(url);request.Method="HEAD";
            request.Timeout=MonitorTiming.ProbeTimeoutMs;request.ReadWriteTimeout=MonitorTiming.ProbeTimeoutMs;
            request.AllowAutoRedirect=false;request.UserAgent="CodexConnectionMonitor/1.4.0";request.KeepAlive=false;
            using(cancellation.Register(()=>ThreadPool.QueueUserWorkItem(_=>{try{request.Abort();}catch{}}))){
                Uri proxy;
                if(request.RequestUri.IsLoopback)request.Proxy=null;
                else if(!String.IsNullOrEmpty(ProxySetting) && Uri.TryCreate(ProxySetting,UriKind.Absolute,out proxy)){
                    if(proxy.Scheme=="http" || proxy.Scheme=="https")request.Proxy=new WebProxy(proxy);
                    else result.Route="系统设置（未使用 SOCKS 环境代理）";
                }
                cancellation.ThrowIfCancellationRequested();
                using(HttpWebResponse response=(HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false)){result.Reached=true;result.Status=(int)response.StatusCode;}
            }
        }catch(WebException e){
            var response=e.Response as HttpWebResponse;
            if(response!=null){using(response){result.Reached=true;result.Status=(int)response.StatusCode;}}
            else switch(e.Status){
                case WebExceptionStatus.NameResolutionFailure:case WebExceptionStatus.ProxyNameResolutionFailure:result.Error="DNS 解析失败";break;
                case WebExceptionStatus.TrustFailure:case WebExceptionStatus.SecureChannelFailure:result.Error="TLS / 证书连接失败";break;
                case WebExceptionStatus.Timeout:case WebExceptionStatus.RequestCanceled:result.Error="探测超时";break;
                default:result.Error="连接失败（探测路径）";break;
            }
        }catch(OperationCanceledException){throw;}catch{result.Error="探测不可用";}
        return result;
    }
    static void ObserveFailure(Task task){task.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);}
}
}
