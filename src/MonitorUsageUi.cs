using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using System.Threading.Tasks;

namespace ConnectionMonitor {
public partial class MonitorForm {
    UsageClient usageClient=new UsageClient();UsageState usage=new UsageState();
    TableLayoutPanel usageCard,usageRows;Label usageCaption,usageStatus;
    string usageUiKey="";bool? networkAvailable;bool monitoringClosed;
    Dictionary<string,Icon> trayIcons=new Dictionary<string,Icon>();
    void BuildUsageCard(){usageCard=Card();usageCaption=Wrap(8.5f,true,Muted);usageRows=Stack(1);usageStatus=Wrap(8,false,Muted);Add(usageCard,usageCaption);Add(usageCard,usageRows);Add(usageCard,usageStatus);Add(root,usageCard);}
    void StartNetworkObserver(){if(testing)return;try{networkAvailable=NetworkInterface.GetIsNetworkAvailable();NetworkChange.NetworkAvailabilityChanged+=NetworkChanged;NetworkChange.NetworkAddressChanged+=NetworkAddressChanged;}catch{networkAvailable=null;}}
    void NetworkChanged(object sender,NetworkAvailabilityEventArgs args){QueueNetworkChange(args.IsAvailable);}
    void StopMonitoring(){monitoringClosed=true;NetworkChange.NetworkAvailabilityChanged-=NetworkChanged;NetworkChange.NetworkAddressChanged-=NetworkAddressChanged;probeCancellation.Cancel();probeCancellation.Dispose();usageClient.Dispose();foreach(Icon icon in trayIcons.Values)icon.Dispose();trayIcons.Clear();}
    void CheckNow(){lastProbe=lastLog=0;PollUsage(true);Poll();}
    async void PollUsage(bool manual){
        if(testing || monitoringClosed || !usage.Due(Rules.Now(),manual))return;
        usage.Busy=true;usage.Attempted=Rules.Now();
        UsageState next=usage.Snapshot();
        try{await Task.Run(()=>usageClient.Refresh(next));if(!monitoringClosed)usage=next;}finally{usage.Busy=false;if(!monitoringClosed && !IsDisposed)RefreshView();}
    }
    void SelectCodex(){
        if(usage.Busy){MessageBox.Show(Locale.Pick("请等本次额度刷新结束后再选择。","Wait for the current quota refresh to finish."),Text);return;}
        using(var dialog=new OpenFileDialog {Title=Locale.Pick("选择官方 Codex 的 codex.exe","Select the official Codex codex.exe"),Filter="Codex (codex.exe)|codex.exe",CheckFileExists=true}){
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            usageClient.Dispose();usageClient=new UsageClient {ExecutablePath=dialog.FileName};usage=new UsageState();SaveSettings();PollUsage(true);
        }
    }
    void RenderUsage(){
        if(usageCard==null)return;
        long now=Rules.Now();bool compact=sizeMode=="small";
        usageCaption.Text=Locale.Pick("账号共享额度","ACCOUNT QUOTA · SHARED");
        usageStatus.Text=usage.Status(now);usageStatus.Visible=!compact || usage.Error!="" || usage.Stale(now);
        usageStatus.ForeColor=usage.Error=="" && !usage.Stale(now)?Muted:StatusColor("warning");
        string key=Locale.English+"|"+sizeMode+"|"+new JavaScriptSerializer().Serialize(usage.Windows)+"|"+(usage.Error!="" || usage.Stale(now))+"|"+usage.Windows.Any(w=>w.ResetsAt.HasValue && w.ResetsAt.Value<=now);
        if(key==usageUiKey)return;usageUiKey=key;
        usageRows.SuspendLayout();
        foreach(Control control in usageRows.Controls.Cast<Control>().ToArray()){foreach(Label label in Descendants(control).OfType<Label>())wraps.Remove(label);usageRows.Controls.Remove(control);control.Dispose();}
        usageRows.RowStyles.Clear();usageRows.RowCount=0;
        IEnumerable<UsageWindow> windows=usage.Windows;
        if(compact){string bucket=windows.Any(w=>w.Bucket=="codex")?"codex":windows.Select(w=>w.Bucket).FirstOrDefault();windows=windows.Where(w=>w.Bucket==bucket);}
        foreach(UsageWindow window in windows){
            string name=window.Bucket=="codex"?"":window.Name+" · ";
            Label amount=Wrap(compact?9:10,true);amount.Margin=new Padding(0,5,0,2);amount.Text=name+window.Period+" · "+Locale.Pick("剩余 ","Left ")+window.Percent;Add(usageRows,amount);
            Label reset=Wrap(8,false,Muted);reset.Text=window.Reset(now,!compact);reset.Margin=new Padding(0,0,0,3);Add(usageRows,reset);
            if(!compact && window.Remaining.HasValue){var bar=new ProgressBar {Minimum=0,Maximum=100,Value=(int)Math.Round(window.Remaining.Value),Height=Px(7),Dock=DockStyle.Top,Margin=new Padding(0,0,0,7),Style=ProgressBarStyle.Continuous};Add(usageRows,bar);}
        }
        if(compact && usage.Windows.Select(w=>w.Bucket).Distinct().Count()>1){Label more=Wrap(8,false,Muted);more.Text=Locale.Pick("其他额度请查看记录","More quota in History");Add(usageRows,more);}
        hints.SetToolTip(usageCard,UsageDetails());hints.SetToolTip(usageCaption,UsageDetails());usageRows.ResumeLayout(true);
    }
    string UsageDetails(){
        var b=new StringBuilder();b.AppendLine(Locale.Pick("账号共享额度（不是单个任务的额度）","Account quota (shared across tasks)"));b.AppendLine(usage.Status(Rules.Now()));
        foreach(var w in usage.Windows)b.AppendLine(w.Name+" · "+w.Period+" · "+Locale.Pick("剩余 ","Left ")+w.Percent+" · "+w.Reset(Rules.Now(),true));
        b.AppendLine(Locale.Pick("每 60 秒更新；重置到期后重新查询，不自动补满。","Refreshes every 60s. Reset must be confirmed by a new response."));return b.ToString();
    }
}
public static class TrayBrand {
    [DllImport("user32.dll")]static extern bool DestroyIcon(IntPtr handle);
    public static Bitmap Draw(string code,Color color,int size){
        Bitmap bitmap=new Bitmap(size,size);using(Graphics g=Graphics.FromImage(bitmap)){
            g.SmoothingMode=SmoothingMode.AntiAlias;g.ScaleTransform(size/32f,size/32f);g.Clear(Color.Transparent);
            using(Brush b=new SolidBrush(color))g.FillEllipse(b,1,1,30,30);
            using(Pen pen=new Pen(Color.White,2.8f)){
                pen.StartCap=pen.EndCap=LineCap.Round;pen.LineJoin=LineJoin.Round;
                if(code=="error"){g.DrawLine(pen,10,10,22,22);g.DrawLine(pen,22,10,10,22);}
                else if(code=="warning"){g.DrawLine(pen,16,8,16,18);g.DrawLine(pen,16,24,16,24.2f);}
                else if(code=="idle"){g.DrawLine(pen,10,16,14,20);g.DrawLine(pen,14,20,23,11);}
                else if(code=="ok"){g.DrawLines(pen,new[]{new PointF(6,17),new PointF(11,17),new PointF(14,10),new PointF(18,23),new PointF(22,16),new PointF(26,16)});}
                else{g.DrawLine(pen,10,16,22,16);}
            }
        }return bitmap;
    }
    public static Icon Create(string code,Color color){using(Bitmap b=Draw(code,color,32)){IntPtr h=b.GetHicon();Icon icon=(Icon)Icon.FromHandle(h).Clone();DestroyIcon(h);return icon;}}
}
}
