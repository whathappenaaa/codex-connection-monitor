using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace ConnectionMonitor {
public class TaskInfo { public string Id, Name; public long Updated; public bool Archived; }
public class TaskCatalog {
    public Dictionary<string,TaskInfo> Items=new Dictionary<string,TaskInfo>();
    public bool Available;
    long lastRead;
    public static string ChooseName(string name,string title) {
        if(!String.IsNullOrWhiteSpace(name))return name.Trim();
        // Older schemas sometimes stored the display title directly; do not display an entire prompt.
        if(!String.IsNullOrWhiteSpace(title) && title.Length<=120 && !title.Contains("\n") && !title.StartsWith("#"))return title.Trim();
        return "";
    }
    public void Refresh(string home) {
        if(Rules.Now()-lastRead<5)return;lastRead=Rules.Now();
        try {
            string path=Directory.GetFiles(home,"state_*.sqlite").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if(path==null)return;
            using(LogDatabase db=new LogDatabase(path)) {
                bool hasName=db.Query("SELECT name,'','','','','' FROM pragma_table_info('threads')").Any(r=>r[0]=="name");
                string sql="SELECT id,"+(hasName?"name":"''")+",substr(title,1,160),updated_at,archived,'' FROM threads WHERE source IS NULL OR instr(source,'\"subagent\"')=0 ORDER BY updated_at DESC LIMIT 250";
                Dictionary<string,TaskInfo> next=new Dictionary<string,TaskInfo>();
                foreach(string[] row in db.Query(sql)) {long updated;Int64.TryParse(row[3],out updated);next[row[0]]=new TaskInfo {Id=row[0],Name=ChooseName(row[1],row[2]),Updated=updated,Archived=row[4]=="1"};}
                Items=next;Available=true;
            }
        } catch {Available=false;}
    }
    public string Name(string id) {TaskInfo t;return id!=null && Items.TryGetValue(id,out t) && t.Name!=""?t.Name:Locale.Pick("未命名任务","Untitled task");}
}
public static class Locale {
    public static bool English;
    public static string Pick(string zh,string en) {return English?en:zh;}
    static readonly Dictionary<string,string> Translations=new Dictionary<string,string> {
        {"监测数据不可用","Monitoring unavailable"},{"暂时读不到 Codex 日志，不能判断对话连接。","Cannot read Codex logs. Connection status is unknown."},
        {"等待任务活动","Waiting for activity"},{"尚未捕捉到当前任务的连接记录。","No connection evidence for this task yet."},
        {"时间记录异常","Clock mismatch"},{"日志时间与电脑时钟不一致，暂不判断连接。","Log timestamps do not match the system clock."},
        {"上次连接失败","Last connection failed"},{"尚未看到恢复记录；当前是否仍断开未知。","No recovery recorded yet. The current connection is unknown."},
        {"连接中断 / 重试中","Disconnected / retrying"},{"本轮已结束","Turn completed"},{"本轮正常结束；下一次发送时再确认连接。","This turn finished normally. The next request will confirm connectivity."},
        {"等待新的连接证据","Waiting for new evidence"},{"可能正在思考、执行工具或空闲，不能据此判为断网。","The task may be thinking, using tools, or idle. Silence does not prove a disconnect."},
        {"最近收到模型回应","Model response received"},{"当前任务有新的模型输出，连接曾成功传输。","Recent model output confirms data was received for this task."},
        {"最近建立了连接","Connection established"},{"Codex 已连上回应通道，等待后续输出。","Codex connected to the response channel. Waiting for output."},
        {"TLS 握手被中断","TLS handshake interrupted"},{"连接被对端或链路设备重置 (10054)","Connection reset by peer or network (10054)"},
        {"TLS 连接意外关闭","TLS connection closed unexpectedly"},{"连接超时","Connection timed out"},{"请求发送失败","Request could not be sent"},
        {"回应流中断，Codex 正在尝试恢复","Response stream interrupted; Codex is retrying"},
        {"正在读取日志","Reading logs"},{"没有找到 Codex 日志数据库","Codex log database not found"},{"无法只读打开日志数据库","Cannot open logs in read-only mode"},
        {"日志格式不兼容","Unsupported log format"},{"日志暂时被占用","Log database is busy"},{"日志读取暂不可用","Log reader unavailable"},
        {"探测超时","Probe timed out"},{"DNS 解析失败","DNS lookup failed"},{"TLS / 证书连接失败","TLS / certificate failure"},
        {"连接失败（探测路径）","Probe connection failed"},{"探测不可用","Probe unavailable"},
        {"系统代理设置","system proxy settings"},{"HTTPS_PROXY 环境设置","HTTPS_PROXY environment setting"},{"系统设置（未使用 SOCKS 环境代理）","system settings (SOCKS environment proxy not used)"}
    };
    public static string T(string text) {string result;return English && Translations.TryGetValue((text??"").Trim(),out result)?result:text;}
    public static string Age(long stamp) {
        if(stamp<=0)return Pick("尚无记录","No record yet");long s=Math.Max(0,Rules.Now()-stamp);
        return s<60?s+Pick(" 秒前","s ago"):s<3600?(s/60)+Pick(" 分钟前","m ago"):(s/3600)+Pick(" 小时前","h ago");
    }
}
public static class Brand {
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    static GraphicsPath Round(RectangleF r,float radius) {
        GraphicsPath p=new GraphicsPath();float d=radius*2;p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;
    }
    public static Bitmap Image(int size,Color? state=null) {
        Bitmap b=new Bitmap(size,size);using(Graphics g=Graphics.FromImage(b)) {
            g.SmoothingMode=SmoothingMode.AntiAlias;g.ScaleTransform(size/64f,size/64f);g.Clear(Color.Transparent);
            using(GraphicsPath p=Round(new RectangleF(2,2,60,60),16))using(LinearGradientBrush fill=new LinearGradientBrush(new Point(2,2),new Point(60,64),Color.FromArgb(24,70,76),Color.FromArgb(14,36,53)))g.FillPath(fill,p);
            using(Pen arc=new Pen(Color.FromArgb(112,237,204),4.4f)) {
                arc.StartCap=LineCap.Round;arc.EndCap=LineCap.Round;
                g.DrawArc(arc,16,13,33,33,205,245);
            }
            using(Pen pulse=new Pen(Color.White,3.5f)) {pulse.StartCap=LineCap.Round;pulse.EndCap=LineCap.Round;pulse.LineJoin=LineJoin.Round;g.DrawLines(pulse,new[]{new PointF(13,36),new PointF(24,36),new PointF(29,27),new PointF(35,43),new PointF(40,34),new PointF(51,34)});}
            if(state.HasValue) {using(Brush border=new SolidBrush(Color.White))g.FillEllipse(border,43,43,19,19);using(Brush dot=new SolidBrush(state.Value))g.FillEllipse(dot,46,46,13,13);}
        }return b;
    }
    public static Icon Icon(Color? state=null) {using(Bitmap b=Image(64,state)){IntPtr h=b.GetHicon();Icon i=(Icon)System.Drawing.Icon.FromHandle(h).Clone();DestroyIcon(h);return i;}}
    public static void Export(string dir) {
        Directory.CreateDirectory(dir);using(Bitmap b=Image(256))b.Save(Path.Combine(dir,"icon.png"),ImageFormat.Png);
        int[] sizes={16,24,32,48,64,128,256};List<byte[]> pngs=new List<byte[]>();
        foreach(int size in sizes)using(Bitmap b=Image(size))using(MemoryStream m=new MemoryStream()){b.Save(m,ImageFormat.Png);pngs.Add(m.ToArray());}
        using(BinaryWriter w=new BinaryWriter(File.Create(Path.Combine(dir,"icon.ico")))) {
            w.Write((ushort)0);w.Write((ushort)1);w.Write((ushort)sizes.Length);int offset=6+16*sizes.Length;
            for(int i=0;i<sizes.Length;i++){w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((byte)0);w.Write((byte)0);w.Write((ushort)1);w.Write((ushort)32);w.Write(pngs[i].Length);w.Write(offset);offset+=pngs[i].Length;}
            foreach(byte[] png in pngs)w.Write(png);
        }
    }
}

public class MonitorSettings {
    public string language="zh",size="small",codexExecutablePath="";
    public bool autoFit=true;
}
public static class WindowSizes {
    public static int Width(string size){return size=="large"?460:size=="medium"?390:330;}
    public static int HeightLimit(string size){return size=="large"?790:size=="medium"?670:540;}
    public static string Normalize(string size){return size=="large"||size=="medium"?size:"small";}
    public static int FitHeight(int desired,int limit,int available,int minimum){return Math.Max(1,Math.Min(available,Math.Min(limit,Math.Max(minimum,desired))));}
}
public partial class MonitorForm : Form {
    static readonly Color Ink=Color.FromArgb(27,47,54),Muted=Color.FromArgb(106,121,124),Background=Color.FromArgb(242,246,246),Line=Color.FromArgb(222,232,230);
    readonly object gate=new object();
    LogReader reader=new LogReader();ProbeResult internet=new ProbeResult(),service=new ProbeResult();
    System.Windows.Forms.Timer tick=new System.Windows.Forms.Timer();
    NotifyIcon tray;Icon trayIcon;ContextMenuStrip taskMenu;ToolTip hints=new ToolTip();
    Panel scroll;TableLayoutPanel root,statusCard,footer,header,intro,metrics,sizeBar;
    Label heading,subheading,taskCaption,taskTitle,taskMeta,statusTitle,statusDetail,responseCaption,responseValue,errorCaption,errorValue,networkHeading,publicCaption,publicValue,serviceCaption,serviceValue,updateNote,footnote,author;
    Button language,picker,refresh,detail,hide,sizeSmall,sizeMedium,sizeLarge;CheckBox pin,autoFit;PictureBox logo;
    List<Label> wraps=new List<Label>();
    bool logBusy,probeBusy,exit,screenshot,testing,wrapping,fitting,fitPending,applyingSize,ready;
    string sizeMode="small",fitSignature="";float testScale=1f;Size resizeStart;
    long lastLog,lastProbe;string selected="",lastCode="",capturePath;
    static string SettingsPath {get{return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings.json");}}
    public MonitorForm(string capture):this(capture,false){}
    public MonitorForm(string capture,bool test) {
        screenshot=!String.IsNullOrEmpty(capture);capturePath=capture;testing=test;
        MonitorSettings settings=new MonitorSettings();
        if(!test && !screenshot)try {if(File.Exists(SettingsPath))settings=new JavaScriptSerializer().Deserialize<MonitorSettings>(File.ReadAllText(SettingsPath))??settings;Locale.English=settings.language=="en";}catch{}
        sizeMode=WindowSizes.Normalize(settings.size);usageClient.ExecutablePath=settings.codexExecutablePath??"";
        AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
        Font=new Font("Microsoft YaHei UI",9f);Text="Codex 连接灯";BackColor=Background;ForeColor=Ink;DoubleBuffered=true;
        if(test){Opacity=0;ShowInTaskbar=false;}
        ClientSize=new Size(330,440);MinimumSize=new Size(346,350);MaximizeBox=false;TopMost=true;Icon=Brand.Icon();
        StartPosition=FormStartPosition.Manual;Rectangle area=Screen.PrimaryScreen.WorkingArea;
        if(Height>area.Height-24)Height=area.Height-24;
        Location=new Point(Math.Max(area.Left,area.Right-Width-22),Math.Max(area.Top,area.Bottom-Height-22));
        scroll=new Panel {Dock=DockStyle.Fill,AutoScroll=true,BackColor=Background};Controls.Add(scroll);
        root=Stack(1);root.Dock=DockStyle.Top;root.Padding=new Padding(20,18,20,16);scroll.Controls.Add(root);
        header=Grid(3);header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,48));header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        logo=new PictureBox {Size=new Size(38,38),SizeMode=PictureBoxSizeMode.Zoom,Image=Brand.Image(128),Margin=new Padding(0,1,10,0)};
        heading=Wrap(17,true);heading.Margin=new Padding(0,5,0,0);
        language=Button("EN",()=>{Locale.English=!Locale.English;ApplyLanguage();SaveLanguage();});language.MinimumSize=new Size(50,32);language.AccessibleName="Switch language / 切换语言";
        header.Controls.Add(logo,0,0);header.Controls.Add(heading,1,0);header.Controls.Add(language,2,0);Add(root,header);
        intro=Grid(2);intro.Margin=new Padding(0,8,0,10);intro.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));intro.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        subheading=Label(8.5f,false,Muted);pin=new CheckBox {AutoSize=true,Checked=true,Margin=new Padding(10,0,0,0)};pin.CheckedChanged+=(s,e)=>TopMost=pin.Checked;
        intro.Controls.Add(subheading,0,0);Add(root,intro);
        sizeBar=Grid(5);sizeBar.Margin=new Padding(0,8,0,10);
        for(int i=0;i<3;i++)sizeBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,36));
        sizeBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));sizeBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        sizeSmall=Button("小",()=>ApplySize("small",true));sizeMedium=Button("中",()=>ApplySize("medium",true));sizeLarge=Button("大",()=>ApplySize("large",true));
        foreach(Button b in new[]{sizeSmall,sizeMedium,sizeLarge}){b.Padding=new Padding(3,3,3,3);b.Dock=DockStyle.Fill;b.Margin=new Padding(0,0,3,0);}
        autoFit=new CheckBox {AutoSize=true,Checked=settings.autoFit,Margin=new Padding(8,5,0,0)};pin.Margin=new Padding(4,5,0,0);
        autoFit.CheckedChanged+=(s,e)=>{if(ready && !applyingSize){fitSignature="";SaveSettings();ScheduleFit();}};
        sizeBar.Controls.Add(sizeSmall,0,0);sizeBar.Controls.Add(sizeMedium,1,0);sizeBar.Controls.Add(sizeLarge,2,0);sizeBar.Controls.Add(autoFit,3,0);sizeBar.Controls.Add(pin,4,0);Add(root,sizeBar);
        TableLayoutPanel task=Card();TableLayoutPanel taskHead=Grid(2);taskHead.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));taskHead.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        taskCaption=Label(8.5f,false,Muted);taskCaption.Margin=new Padding(0,7,0,0);picker=Button("",ChooseTask);picker.Font=new Font(Font.FontFamily,8.5f);picker.Padding=new Padding(7,3,7,3);
        taskHead.Controls.Add(taskCaption,0,0);taskHead.Controls.Add(picker,1,0);Add(task,taskHead);
        taskTitle=Wrap(11,true);taskTitle.Margin=new Padding(0,8,0,4);Add(task,taskTitle);taskMeta=Wrap(8,false,Muted);Add(task,taskMeta);Add(root,task);
        statusCard=Card();statusTitle=Wrap(16,true);statusTitle.Margin=new Padding(0,0,0,8);Add(statusCard,statusTitle);statusDetail=Wrap(9,false,Muted);Add(statusCard,statusDetail);Add(root,statusCard);
        BuildUsageCard();
        metrics=Grid(2);metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,60));metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,40));metrics.Margin=new Padding(2,2,2,14);
        responseCaption=Label(9,false,Muted);responseValue=Label(9,true);errorCaption=Label(9,false,Muted);errorValue=Label(9,true);
        responseValue.TextAlign=ContentAlignment.TopRight;errorValue.TextAlign=ContentAlignment.TopRight;responseValue.Dock=DockStyle.Fill;errorValue.Dock=DockStyle.Fill;
        foreach(Label l in new[]{responseCaption,responseValue,errorCaption,errorValue})l.Margin=new Padding(0,4,0,4);
        metrics.Controls.Add(responseCaption,0,0);metrics.Controls.Add(responseValue,1,0);metrics.Controls.Add(errorCaption,0,1);metrics.Controls.Add(errorValue,1,1);Add(root,metrics);
        TableLayoutPanel network=Card();networkHeading=Label(10,true);networkHeading.Margin=new Padding(0,0,0,8);Add(network,networkHeading);
        TableLayoutPanel probes=Grid(2);probes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,41));probes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,59));
        publicCaption=Wrap(9,false,Muted);publicValue=Wrap(9,true);serviceCaption=Wrap(9,false,Muted);serviceValue=Wrap(9,true);
        foreach(Label l in new[]{publicCaption,publicValue,serviceCaption,serviceValue})l.Margin=new Padding(0,4,0,4);
        probes.Controls.Add(publicCaption,0,0);probes.Controls.Add(publicValue,1,0);probes.Controls.Add(serviceCaption,0,1);probes.Controls.Add(serviceValue,1,1);Add(network,probes);Add(root,network);
        updateNote=Wrap(8,false,Muted);updateNote.Margin=new Padding(0,0,0,10);Add(root,updateNote);
        TableLayoutPanel actions=Grid(3);for(int i=0;i<3;i++)actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/3));
        refresh=Button("",CheckNow);detail=Button("",ShowDetails);hide=Button("",()=>Hide());
        refresh.Dock=detail.Dock=hide.Dock=DockStyle.Fill;refresh.Margin=new Padding(0,0,5,0);detail.Margin=new Padding(5,0,5,0);hide.Margin=new Padding(5,0,0,0);
        actions.Controls.Add(refresh,0,0);actions.Controls.Add(detail,1,0);actions.Controls.Add(hide,2,0);
        footnote=Wrap(8,false,Muted);footnote.Margin=new Padding(0,12,0,8);Add(root,footnote);
        author=Label(8.5f,true,Muted);author.Margin=new Padding(0,10,0,0);footer=Stack(1);footer.Dock=DockStyle.Bottom;footer.Padding=new Padding(20,8,20,12);footer.BackColor=Background;Add(footer,actions);Add(footer,author);Controls.Add(footer);footer.SendToBack();
        root.SizeChanged+=(s,e)=>ResizeWraps();scroll.SizeChanged+=(s,e)=>ResizeWraps();
        if(!screenshot) {tray=new NotifyIcon {Visible=true};tray.DoubleClick+=(s,e)=>Restore();}
        StartNetworkObserver();
        ready=true;ApplySize(sizeMode,false);ApplyLanguage();
        Shown+=(s,e)=>{ApplySize(sizeMode,false);ResizeWraps();if(!testing){FitContent();Poll();}};
        tick.Interval=MonitorTiming.LogIntervalMs;tick.Tick+=(s,e)=>{if(!testing)Poll();RefreshView();};tick.Start();
        FormClosing+=(s,e)=>{if(!exit && !screenshot && e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();}};
        FormClosed+=(s,e)=>{tick.Stop();tick.Dispose();if(taskMenu!=null)taskMenu.Dispose();if(tray!=null){tray.Visible=false;tray.Icon=null;if(tray.ContextMenuStrip!=null)tray.ContextMenuStrip.Dispose();tray.Dispose();tray=null;}StopMonitoring();hints.Dispose();logo.Image.Dispose();Icon.Dispose();};
        Resize+=(s,e)=>{if(WindowState==FormWindowState.Minimized)Hide();};
        ResizeBegin+=(s,e)=>resizeStart=ClientSize;
        ResizeEnd+=(s,e)=>{if(ClientSize!=resizeStart){autoFit.Checked=false;SaveSettings();}};
    }
    static TableLayoutPanel Grid(int n) {return new TableLayoutPanel {ColumnCount=n,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top,Margin=Padding.Empty,Padding=Padding.Empty,GrowStyle=TableLayoutPanelGrowStyle.AddRows};}
    static TableLayoutPanel Stack(int n) {TableLayoutPanel p=Grid(n);p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));return p;}
    static void Add(TableLayoutPanel p,Control c) {int row=p.RowCount++;p.RowStyles.Add(new RowStyle(SizeType.AutoSize));p.Controls.Add(c,0,row);}
    TableLayoutPanel Card(){TableLayoutPanel c=Stack(1);c.BackColor=Color.White;c.Padding=new Padding(14);c.Margin=new Padding(0,0,0,12);return c;}
    Label Label(float size,bool bold,Color? color=null){return new Label {AutoSize=true,UseMnemonic=false,Font=new Font(Font.FontFamily,size,bold?FontStyle.Bold:FontStyle.Regular),ForeColor=color??Ink,Margin=Padding.Empty};}
    Label Wrap(float size,bool bold,Color? color=null){Label l=Label(size,bold,color);wraps.Add(l);return l;}
    Button Button(string text,Action action){Button b=new Button {Text=text,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlatStyle=FlatStyle.Flat,BackColor=Color.White,ForeColor=Ink,Padding=new Padding(8,6,8,6),Margin=Padding.Empty,UseMnemonic=false,Cursor=Cursors.Hand};b.FlatAppearance.BorderColor=Line;b.Click+=(s,e)=>action();return b;}
    void ResizeWraps(){if(wrapping)return;wrapping=true;try{foreach(Label l in wraps){if(l.Parent==null)continue;TableLayoutPanel table=l.Parent as TableLayoutPanel;int width=table==null?l.Parent.ClientSize.Width:table.GetColumnWidths().ElementAtOrDefault(table.GetColumn(l));width-=l.Margin.Horizontal+(table!=null && table.ColumnCount==1?table.Padding.Horizontal:0);if(width>60 && l.MaximumSize.Width!=width)l.MaximumSize=new Size(width,0);}}finally{wrapping=false;}}
    void SaveSettings(){if(testing || screenshot || !ready)return;try{File.WriteAllText(SettingsPath,new JavaScriptSerializer().Serialize(new MonitorSettings {language=Locale.English?"en":"zh",size=sizeMode,autoFit=autoFit.Checked,codexExecutablePath=usageClient.ExecutablePath}),Encoding.UTF8);}catch{}}
    void SaveLanguage(){SaveSettings();}
    void ApplyLanguage(){
        Text=Locale.Pick("Codex 连接灯","Codex Link Monitor");heading.Text=Locale.Pick("Codex 连接灯","Codex Link");language.Text=Locale.Pick("EN","中文");
        subheading.Text=Locale.Pick("桌面连接监测","Desktop connection monitor");pin.Text=Locale.Pick("置顶","Pin");taskCaption.Text=sizeMode=="small"?Locale.Pick("任务","Task"):Locale.Pick("监测任务","MONITORED TASK");picker.Text=Locale.Pick("选择任务 ▾","Choose task ▾");
        responseCaption.Text=sizeMode=="small"?Locale.Pick("最后回应","Last response"):Locale.Pick("最后模型回应","Last model response");errorCaption.Text=sizeMode=="small"?Locale.Pick("最近异常","Last error"):Locale.Pick("最近连接异常","Last connection error");networkHeading.Text=Locale.Pick("独立网络探测","Independent network probes");
        publicCaption.Text=sizeMode=="small"?Locale.Pick("公共网络","Internet"):Locale.Pick("公共网络入口","Public endpoint");serviceCaption.Text=sizeMode=="small"?"ChatGPT":Locale.Pick("ChatGPT 入口","ChatGPT endpoint");
        refresh.Text=sizeMode=="small"?Locale.Pick("检测","Check"):Locale.Pick("立即检测","Check now");detail.Text=sizeMode=="small"?Locale.Pick("记录","History"):Locale.Pick("查看记录","History");hide.Text=sizeMode=="small"?Locale.Pick("收起","Hide"):Locale.Pick("收起到托盘","To tray");
        sizeSmall.Text=Locale.Pick("小","S");sizeMedium.Text=Locale.Pick("中","M");sizeLarge.Text=Locale.Pick("大","L");autoFit.Text=sizeMode=="small"?Locale.Pick("自适应","Auto"):Locale.Pick("自动适配","Auto fit");
        hints.SetToolTip(sizeSmall,Locale.Pick("小号：紧凑状态面板","Small: compact status panel"));hints.SetToolTip(sizeMedium,Locale.Pick("中号：状态与说明","Medium: status and explanation"));hints.SetToolTip(sizeLarge,Locale.Pick("大号：完整信息","Large: full details"));
        hints.SetToolTip(autoFit,Locale.Pick("根据内容、语言和屏幕空间自动调整高度；拖动边框后切换为手动。","Fit height to content, language and available screen space. Dragging a border switches to manual sizing."));
        footnote.Text=Locale.Pick("绿色仅表示未检测到断开；灰色表示无法确认。","Green means no disconnect detected. Gray means unconfirmed.");author.Text=Locale.Pick("作者：B站那年松江","Author: B站那年松江");
        if(tray!=null){ContextMenuStrip old=tray.ContextMenuStrip;ContextMenuStrip menu=new ContextMenuStrip();menu.Items.Add(Locale.Pick("显示悬浮窗","Show monitor"),null,(s,e)=>Restore());menu.Items.Add(Locale.Pick("立即检测","Check now"),null,(s,e)=>CheckNow());menu.Items.Add(Locale.Pick("指定 Codex 程序…","Select Codex executable…"),null,(s,e)=>SelectCodex());menu.Items.Add(Locale.Pick("退出连接灯","Exit monitor"),null,(s,e)=>{exit=true;Close();});tray.ContextMenuStrip=menu;if(old!=null)old.Dispose();}
        RefreshView();ResizeWraps();ScheduleFit();
    }
    void Restore(){Show();WindowState=FormWindowState.Normal;Activate();}
    // Presets describe actual window width; fonts retain the user's Windows text scaling.
    float UiScale {get{return testScale;}}
    int Px(int value){return (int)Math.Round(value*UiScale);}
    void SetFontSize(Control c,float size){Font previous=c.Font;c.Font=new Font(previous.FontFamily,size*testScale,previous.Style);}
    void ApplySize(string mode,bool clicked){
        applyingSize=true;sizeMode=WindowSizes.Normalize(mode);bool compact=sizeMode=="small";int gap=compact?10:sizeMode=="medium"?14:18;
        SuspendLayout();root.SuspendLayout();footer.SuspendLayout();
        try{
            root.Padding=new Padding(Px(gap),Px(gap),Px(gap),Px(compact?4:10));footer.Padding=new Padding(Px(gap),Px(6),Px(gap),Px(9));
            foreach(TableLayoutPanel card in root.Controls.OfType<TableLayoutPanel>().Where(p=>p.BackColor==Color.White)){card.Padding=new Padding(Px(compact?9:12));card.Margin=new Padding(0,0,0,Px(compact?7:10));}
            intro.Visible=!compact;taskMeta.Visible=!compact;statusDetail.Visible=!compact;networkHeading.Visible=!compact;updateNote.Visible=sizeMode=="large";footnote.Visible=sizeMode=="large";
            logo.Size=new Size(Px(compact?28:34),Px(compact?28:34));header.ColumnStyles[0].Width=Px(compact?36:44);heading.Margin=new Padding(0,Px(3),0,0);
            SetFontSize(heading,compact?12:15);SetFontSize(statusTitle,compact?12:15);SetFontSize(taskTitle,compact?9.5f:10.5f);
            statusTitle.Margin=new Padding(0,0,0,compact?0:Px(7));taskTitle.Margin=new Padding(0,Px(5),0,compact?0:Px(3));metrics.Margin=new Padding(1,0,1,Px(compact?6:10));
            foreach(Label l in new[]{responseCaption,responseValue,errorCaption,errorValue,publicCaption,publicValue,serviceCaption,serviceValue})l.Margin=new Padding(0,Px(compact?2:4),0,Px(compact?2:4));
            sizeBar.Margin=new Padding(0,Px(6),0,Px(8));for(int i=0;i<3;i++)sizeBar.ColumnStyles[i].Width=Px(36);
            foreach(Button b in new[]{refresh,detail,hide})b.Padding=new Padding(Px(4),Px(compact?4:6),Px(4),Px(compact?4:6));
            foreach(Button b in new[]{sizeSmall,sizeMedium,sizeLarge}){bool active=b==(compact?sizeSmall:sizeMode=="medium"?sizeMedium:sizeLarge);b.BackColor=active?Ink:Color.White;b.ForeColor=active?Color.White:Ink;}
            int chromeX=Width-ClientSize.Width,chromeY=Height-ClientSize.Height;MinimumSize=new Size(Px(330)+chromeX,Px(300)+chromeY);
            Rectangle area=Screen.FromControl(this).WorkingArea;int width=Math.Min(Px(WindowSizes.Width(sizeMode)),area.Width-24-chromeX);
            ClientSize=new Size(Math.Max(Px(300),width),Math.Min(ClientSize.Height,Px(WindowSizes.HeightLimit(sizeMode))));
            if(clicked)autoFit.Checked=true;
        }finally{footer.ResumeLayout(true);root.ResumeLayout(true);ResumeLayout(true);applyingSize=false;}
        ApplyLanguage();ResizeWraps();if(clicked){FitContent();SaveSettings();}
    }
    void ScheduleFit(){
        if(testing || !ready || !IsHandleCreated || IsDisposed || !autoFit.Checked || fitPending || fitting)return;
        string signature=ClientSize.Width+"|"+sizeMode+"|"+Locale.English+"|"+taskTitle.Text+"|"+statusTitle.Text+"|"+statusDetail.Text+"|"+reader.Ok+"|"+usageUiKey;
        if(signature==fitSignature)return;fitSignature=signature;fitPending=true;
        BeginInvoke((Action)(()=>{fitPending=false;if(!IsDisposed)FitContent();}));
    }
    void FitContent(){
        if(fitting || !autoFit.Checked || WindowState!=FormWindowState.Normal)return;fitting=true;
        try{for(int pass=0;pass<3;pass++){
            ResizeWraps();PerformLayout();Rectangle area=Screen.FromControl(this).WorkingArea;int chrome=Height-ClientSize.Height;
            int desired=root.PreferredSize.Height+footer.Height+Px(8);
            int height=WindowSizes.FitHeight(desired,Px(WindowSizes.HeightLimit(sizeMode)),area.Height-chrome-24,Px(300));
            if(Math.Abs(ClientSize.Height-height)>2)ClientSize=new Size(ClientSize.Width,height);else break;
        }
        Rectangle working=Screen.FromControl(this).WorkingArea;Location=new Point(Math.Max(working.Left,Math.Min(Left,working.Right-Width)),Math.Max(working.Top,Math.Min(Top,working.Bottom-Height)));
        }finally{fitting=false;}
    }
    void ChooseTask(){
        if(taskMenu!=null)taskMenu.Dispose();taskMenu=new ContextMenuStrip {ShowCheckMargin=true,ShowImageMargin=false,MaximumSize=new Size(560,520)};if(testing)taskMenu.Opacity=0;
        ToolStripMenuItem auto=new ToolStripMenuItem(Locale.Pick("自动跟随 Codex 当前任务","Follow the current Codex task")){Checked=selected==""};auto.Click+=(s,e)=>{selected="";RefreshView();};taskMenu.Items.Add(auto);taskMenu.Items.Add(new ToolStripSeparator());
        lock(gate){
            List<string> ids=reader.Catalog.Items.Values.Where(t=>!t.Archived).OrderByDescending(t=>t.Updated).Take(30).Select(t=>t.Id).ToList();
            if(reader.ActiveThread!="" && !ids.Contains(reader.ActiveThread))ids.Insert(0,reader.ActiveThread);
            if(selected!="" && !ids.Contains(selected))ids.Insert(0,selected);
            foreach(string idValue in ids){string id=idValue;string name=reader.Catalog.Name(id);ToolStripMenuItem item=new ToolStripMenuItem(name.Replace("&","&&")){Checked=selected==id,ToolTipText=name+"\n"+id,Tag=id};item.Click+=(s,e)=>{selected=id;RefreshView();};taskMenu.Items.Add(item);}
            if(ids.Count==0)taskMenu.Items.Add(new ToolStripMenuItem(Locale.Pick("暂未读取到任务列表","Task list is not available yet")){Enabled=false});
        }
        taskMenu.Show(picker,new Point(0,picker.Height));
    }
    static Color StatusColor(string code){switch(code){case "ok":return Color.FromArgb(18,137,104);case "error":return Color.FromArgb(204,61,73);case "warning":return Color.FromArgb(167,105,27);case "idle":return Color.FromArgb(53,116,164);default:return Color.FromArgb(113,130,135);}}
    void RefreshView(){
        if(IsDisposed)return;lock(gate){
            ThreadState s=reader.Selected(selected);DisplayState d=ConnectionRules.Evaluate(s,reader.Ok,networkAvailable,internet,service,Rules.Now(),networkChangedAt);
            taskTitle.Text=s==null?Locale.Pick("尚未识别当前任务","Current task not identified"):reader.Catalog.Name(s.Id);
            string source=selected==""?Locale.Pick("自动跟随","Following Codex"):Locale.Pick("固定任务","Pinned task");
            taskMeta.Text=s==null?Locale.Pick("请选择任务；不会自动使用其他任务的记录。","Choose a task. Another task's activity will not be used."):source+" · "+ShortId(s.Id);
            hints.SetToolTip(taskTitle,s==null?"":taskTitle.Text+"\n"+s.Id);statusTitle.Text=Locale.T(d.Title);statusTitle.ForeColor=StatusColor(d.Code);statusDetail.Text=Locale.T(d.Detail);hints.SetToolTip(statusTitle,statusDetail.Text);
            responseValue.Text=Locale.Age(s==null?0:s.LastResponse);errorValue.Text=s==null||s.Failure==null?(sizeMode=="small"?Locale.Pick("无记录","None"):Locale.Pick("未捕捉到","None recorded")):Locale.Age(s.Failure.Time);
            errorValue.ForeColor=s!=null && s.Last!=null && s.Last.Kind=="error"?StatusColor("error"):Muted;
            updateNote.Text=reader.Ok?Locale.Pick("日志每 0.5 秒更新 · 网络每 3 秒探测","Logs: every 0.5s · Network probes: every 3s"):Locale.Pick("日志不可用：","Logs unavailable: ")+Locale.T(reader.Error);
            UpdateTray(d);
        }
        RenderUsage();ProbeView(publicValue,internet);ProbeView(serviceValue,service);ResizeWraps();ScheduleFit();
    }
    static string ShortId(string id){return id.Length>18?id.Substring(0,8)+"…"+id.Substring(id.Length-6):id;}
    void ProbeView(Label label,ProbeResult probe){
        label.ForeColor=probe.Time==0?Muted:StatusColor(probe.Reached?"ok":"error");
        label.Text=probe.Time==0?Locale.Pick("检测中…","Checking…"):probe.Reached?(probe.Status>=400?Locale.Pick("入口响应 · HTTP ","Responded · HTTP ")+probe.Status:Locale.Pick("可达 · ","Reachable · ")+probe.Ms+" ms"):Locale.T(probe.Error);
        if(probe.Time>0 && !ConnectionRules.Fresh(probe,Rules.Now())){label.Text=Locale.Pick("结果已过期","Result is stale");label.ForeColor=Muted;}
    }
    void UpdateTray(DisplayState d){if(tray==null)return;DisplayState combined=d;string title="Codex · "+Locale.T(combined.Title)+(combined.Code=="error" && combined.Detail!=""?"\n"+Locale.T(combined.Detail):"")+"\n"+usage.Summary(Rules.Now());tray.Text=title.Length>63?title.Substring(0,63):title;if(combined.Code==lastCode)return;lastCode=combined.Code;Icon next;if(!trayIcons.TryGetValue(lastCode,out next)){next=TrayBrand.Create(lastCode,StatusColor(lastCode));trayIcons[lastCode]=next;}tray.Icon=next;trayIcon=next;}
    string DetailsText(){
        lock(gate){ThreadState s=reader.Selected(selected);StringBuilder b=new StringBuilder();
            b.AppendLine(Locale.Pick("作者：B站那年松江","Author: B站那年松江"));b.AppendLine(Locale.Pick("任务：","Task: ")+(s==null?Locale.Pick("未选择","Not selected"):reader.Catalog.Name(s.Id)));if(s!=null)b.AppendLine("ID: "+s.Id);
            b.AppendLine(Locale.Pick("日志检查：","Log check: ")+Locale.Age(reader.Checked));b.AppendLine();
            b.AppendLine(Locale.Pick("最近异常（所有任务，最多 30 条）","Recent errors (all tasks; up to 30)"));
            if(reader.History.Count==0)b.AppendLine(Locale.Pick("暂无记录","No errors recorded"));
            foreach(string h in reader.History){string translated=h;foreach(string key in new[]{"TLS 握手被中断","连接被对端或链路设备重置 (10054)","TLS 连接意外关闭","连接超时","请求发送失败","回应流中断，Codex 正在尝试恢复"})translated=translated.Replace(key,Locale.T(key));b.AppendLine(translated);}
            DisplayState raw=Rules.Evaluate(s,Rules.Now(),reader.Ok);
            b.AppendLine();b.AppendLine(Locale.Pick("任务日志详情：","Task log detail: ")+Locale.T(raw.Title)+" · "+Locale.T(raw.Detail));
            b.AppendLine();b.AppendLine(Locale.Pick("绿色：未检测到断开。红色：任务中断、系统无网络或入口传输失败。灰色：无法确认。空闲、思考与本轮结束不再单独变色。","Green: no disconnect detected. Red: task interruption, no local network, or endpoint transport failure. Gray: unconfirmed. Idle, thinking and turn completion have no separate color."));
            b.AppendLine(Locale.Pick("窗口与托盘使用同一个判断。日志每 0.5 秒检查，网络每 3 秒探测，单次限时 2 秒，各入口独立更新。Windows 网络变化立即触发检查。","Window and tray share one decision. Logs: 0.5s; probes: 3s; deadline: 2s. Endpoints update independently. Windows network changes trigger an immediate check."));
            b.AppendLine(Locale.Pick("Codex 自身尚未写出错误时，工具不能提前知道。入口探测失败不一定表示模型流已断；401/403 只表示入口响应。只有公共入口失败、ChatGPT 入口仍响应时，不判定 Codex 断开。","The monitor cannot detect a Codex error before it is logged. Failed probes do not prove the model stream is broken; 401/403 are endpoint responses. A public-only failure with a responding ChatGPT endpoint is not a Codex disconnect."));
            b.AppendLine(Locale.Pick("探测使用：","Probe route: ")+Locale.T(service.Route));
            b.AppendLine(Locale.Pick("任务错误会保持红色，直到该任务有新的成功记录；额度状态不影响连接颜色。","Task errors stay red until this task records success; quota never changes connection color."));
            b.AppendLine();b.AppendLine(UsageDetails());b.AppendLine(Locale.Pick("关闭窗口收起到托盘；右键托盘图标退出。","Close the window to hide it to the tray; use the tray menu to exit."));return b.ToString();}
    }
    void ShowDetails(){using(Form f=new Form {Text=Locale.Pick("连接记录与说明","Connection history & help"),ClientSize=new Size(620,590),StartPosition=FormStartPosition.CenterScreen,TopMost=true,Font=Font,Icon=Icon}){TextBox box=new TextBox {Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical,Text=DetailsText(),BackColor=Color.White,Font=Font};f.Controls.Add(box);f.ShowDialog(this);}}
    public void SaveCapture(string path){ResizeWraps();PerformLayout();Update();using(Bitmap b=new Bitmap(Width,Height)){DrawToBitmap(b,new Rectangle(Point.Empty,b.Size));b.Save(path,ImageFormat.Png);}}
    public void ScaleForTest(float factor){testScale*=factor;Scale(new SizeF(factor,factor));ScaleFonts(this,factor);ResizeWraps();}
    static void ScaleFonts(Control c,float factor){var fonts=Descendants(c).Select(x=>new {Control=x,Font=x.Font}).ToArray();foreach(var f in fonts)f.Control.Font=new Font(f.Font.FontFamily,f.Font.Size*factor,f.Font.Style);}
    static IEnumerable<Control> Descendants(Control parent){yield return parent;foreach(Control c in parent.Controls)foreach(Control child in Descendants(c))yield return child;}
    public async void RunLayoutTest(string folder){
        Directory.CreateDirectory(folder);
        try {
            string id="00000000-0000-0000-0000-000000000001";reader.Ok=true;reader.ActiveThread=id;reader.Catalog.Items[id]=new TaskInfo {Id=id,Name="开源项目演示任务 · 长标题自动换行 / Open-source demo task",Updated=Rules.Now()};
            reader.States[id]=new ThreadState {Id=id};reader.States[id].Apply(new Evidence {Id=1,Time=Rules.Now(),Kind="response"});
            internet=new ProbeResult {Reached=true,Status=200,Ms=737,Time=Rules.Now()};service=new ProbeResult {Reached=true,Status=403,Time=Rules.Now()};
            usage.Success(FeatureTests.Fixture(),Rules.Now());networkAvailable=true;
            int checks=0;
            foreach(float scale in new[]{1f,1.25f,1.5f}) {
                if(scale==1.25f)ScaleForTest(1.25f);if(scale==1.5f)ScaleForTest(1.2f);
                foreach(string mode in new[]{"small","medium","large"})foreach(bool english in new[]{false,true}) {
                    Locale.English=english;ApplySize(mode,true);await Task.Delay(120);ResizeWraps();PerformLayout();FitContent();await Task.Delay(120);
                    foreach(TableLayoutPanel panel in Descendants(root).Concat(Descendants(footer)).OfType<TableLayoutPanel>().Where(p=>p.Visible)) {
                        Control[] children=panel.Controls.Cast<Control>().Where(c=>c.Visible).ToArray();
                        for(int i=0;i<children.Length;i++)for(int j=i+1;j<children.Length;j++)if(children[i].Bounds.IntersectsWith(children[j].Bounds))throw new Exception("Overlap: "+children[i].Text+" / "+children[j].Text);
                    }
                    foreach(Label label in wraps.Where(l=>l.Visible)){if(label.Height+2<label.GetPreferredSize(new Size(label.Width,0)).Height)throw new Exception("Clipped label: "+label.Text);}
                    if(ClientSize.Height>Px(WindowSizes.HeightLimit(mode))+2)throw new Exception("Preset height limit exceeded");
                    if(scroll.Bounds.IntersectsWith(footer.Bounds))throw new Exception("Footer overlaps scroll area");
                    if(footer.Height<author.Height || footer.Bottom>ClientSize.Height)throw new Exception("Author credit is clipped");
                    SaveCapture(Path.Combine(folder,mode+"-"+(english?"en":"zh")+"-"+(int)(scale*100)+".png"));checks++;
                }
            }
            File.WriteAllText(Path.Combine(folder,"result.txt"),"PASS: "+checks+" layout variants: Small/Medium/Large, Chinese/English, 100%/125%/150%; no overlapping controls, clipped wrapping labels or preset height overflow.",Encoding.UTF8);
        }catch(Exception e){File.WriteAllText(Path.Combine(folder,"result.txt"),e.ToString(),Encoding.UTF8);Environment.ExitCode=1;}finally{exit=true;Close();}
    }
    public async void RunUiTest(string output){
        try{
            await Task.Run(()=>{lock(gate)reader.Poll();});RefreshView();await Task.Delay(250);
            for(int i=0;i<3;i++){picker.PerformClick();await Task.Delay(80);if(!taskMenu.Visible)throw new Exception("task menu did not open");ToolStripMenuItem item=taskMenu.Items.OfType<ToolStripMenuItem>().FirstOrDefault(m=>m.Tag is string);if(item!=null){item.PerformClick();if(selected!=(string)item.Tag)throw new Exception("task selection mismatch");}taskMenu.Close(ToolStripDropDownCloseReason.AppClicked);await Task.Delay(80);}
            picker.PerformClick();((ToolStripMenuItem)taskMenu.Items[0]).PerformClick();taskMenu.Close();if(selected!="")throw new Exception("follow mode failed");
            sizeSmall.PerformClick();int smallWidth=ClientSize.Width;sizeMedium.PerformClick();int mediumWidth=ClientSize.Width;sizeLarge.PerformClick();int largeWidth=ClientSize.Width;
            if(!(smallWidth<mediumWidth && mediumWidth<largeWidth))throw new Exception("Three size buttons must give distinct increasing widths");
            OnResizeBegin(EventArgs.Empty);ClientSize=new Size(ClientSize.Width-10,ClientSize.Height-10);OnResizeEnd(EventArgs.Empty);if(autoFit.Checked)throw new Exception("Manual border resize did not disable auto fit");
            Size manual=ClientSize;FitContent();if(ClientSize!=manual)throw new Exception("Manual size changed unexpectedly");
            sizeSmall.PerformClick();if(!autoFit.Checked || statusDetail.Visible)throw new Exception("Small preset should enable automatic compact layout");
            FitContent();await Task.Delay(100);FitContent();Size settledSize=ClientSize;
            for(int i=0;i<4;i++){FitContent();if(ClientSize!=settledSize)throw new Exception("Auto-fit oscillates for unchanged content");}
            Locale.English=false;ApplyLanguage();language.PerformClick();if(!Locale.English || author.Text!="Author: B站那年松江" || refresh.Text!="Check" || tray.ContextMenuStrip.Items[0].Text!="Show monitor")throw new Exception("English toggle incomplete");
            if(!DetailsText().Contains("Recent errors"))throw new Exception("English history unavailable");language.PerformClick();if(Locale.English || author.Text!="作者：B站那年松江")throw new Exception("Chinese toggle failed");
            pin.Checked=false;if(TopMost)throw new Exception("unpin failed");pin.Checked=true;
            hide.PerformClick();if(Visible)throw new Exception("hide failed");Restore();Close();if(Visible || IsDisposed)throw new Exception("close-to-tray failed");Restore();
            if(tray==null || !tray.Visible || tray.Icon==null)throw new Exception("tray icon missing");
            usage.Success(FeatureTests.Fixture(),Rules.Now());hide.PerformClick();networkAvailable=false;RefreshView();if(lastCode!="error" || Visible || statusTitle.Text!="连接中断")throw new Exception("hidden tray and window did not reflect offline state");
            string fixtureId="00000000-0000-0000-0000-000000000001";reader.Ok=true;reader.ActiveThread=fixtureId;selected="";reader.States[fixtureId]=new ThreadState {Id=fixtureId,Last=new Evidence {Id=1,Time=Rules.Now(),Kind="response"}};
            networkAvailable=true;internet=new ProbeResult {Time=Rules.Now(),Reached=true};service=new ProbeResult {Time=Rules.Now(),Reached=false};RefreshView();if(lastCode!="error" || statusTitle.ForeColor!=StatusColor("error"))throw new Exception("probe failure not immediately red in both indicators");
            service.Reached=true;service.Status=403;RefreshView();if(lastCode!="ok")throw new Exception("403 incorrectly changed connection color");usage.Fail("timeout");RefreshView();if(lastCode!="ok")throw new Exception("quota failure altered connection color");
            usage.Windows[0].Remaining=0;RefreshView();if(lastCode!="ok")throw new Exception("quota depletion altered connection color");
            usage.Success(FeatureTests.Fixture(),Rules.Now());RenderUsage();if(!usageRows.Controls.OfType<Label>().Any(l=>l.Text.Contains("83%")))throw new Exception("hidden quota not updated");
            using(var releaseRead=new System.Threading.ManualResetEventSlim(false)){
                LogReader snapshot=reader.Snapshot();readLogsForTest=()=>{releaseRead.Wait(2500);return snapshot;};lastLog=0;PollLogs();
                try {await Task.Delay(50);networkAvailable=false;var watch=System.Diagnostics.Stopwatch.StartNew();RefreshView();if(lastCode!="error" || watch.ElapsedMilliseconds>1000 || !logBusy)throw new Exception("slow log I/O blocks network indicator");}
                finally{releaseRead.Set();}
                while(logBusy)await Task.Delay(10);readLogsForTest=null;
            }
            networkAvailable=true;lastProbe=0;
            var heldProbe=new TaskCompletionSource<ProbeResult>();
            probeForTest=(isPublic,token)=>isPublic?heldProbe.Task:Task.FromResult(new ProbeResult {Reached=false,Time=Rules.Now()});
            PollProbes();await Task.Delay(30);
            try{if(!probeBusy || lastCode!="error" || statusTitle.Text!="连接中断")throw new Exception("fast endpoint failure waits for the slower endpoint");}
            finally{heldProbe.SetResult(new ProbeResult {Reached=true,Status=200,Time=Rules.Now()});}
            while(probeBusy)await Task.Delay(10);probeForTest=null;
            Restore();
            File.WriteAllText(output,"PASS: sizes, auto/manual fit, named tasks, menus, bilingual UI, author, pin, tray hide/restore, close-to-tray, unified window/tray colors, hidden quota render, slow log isolation, independent endpoint updates, 403 and quota failure/depletion isolation.",Encoding.UTF8);
        }catch(Exception e){File.WriteAllText(output,e.ToString(),Encoding.UTF8);Environment.ExitCode=1;}finally{exit=true;Close();}
    }
}
}
