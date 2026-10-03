using System;
using System.IO;
using System.Linq;

namespace ConnectionMonitor {
public static class DesktopLogTests {
    static int count;
    static void Check(bool condition,string name){if(!condition)throw new Exception("FAIL: "+name);count++;}
    public static int Run(){
        count=0;
        string temporary=Path.Combine(Path.GetTempPath(),"codex-monitor-logs-"+Guid.NewGuid().ToString("N"));
        string normal=Path.Combine(temporary,"Codex","Logs");
        string packaged=Path.Combine(temporary,"Packages","OpenAI.Codex_example","LocalCache","Local","Codex","Logs");
        try {
            Directory.CreateDirectory(normal);Directory.CreateDirectory(packaged);
            string[] roots=DesktopLogSource.Roots(temporary).ToArray();
            Check(roots.Contains(normal) && roots.Contains(packaged),"discover ordinary and Store log roots");
            Check(DesktopLogSource.Find(roots)==null,"no logs stays unknown");
            string name="codex-desktop-00000000-0000-0000-0000-000000000001-1-t0-i1-000000-0.log";
            string plain=Path.Combine(normal,name),redirected=Path.Combine(packaged,name);
            string line="2030-01-01T00:00:00.000Z info [electron-message-handler] thread_stream_view_activity_changed active=true conversationId=00000000-0000-0000-0000-000000000002\n";
            File.WriteAllText(plain,line);
            Check(DesktopLogSource.Find(roots)==plain,"standalone desktop log still works");
            File.WriteAllText(redirected,line);File.WriteAllText(plain,"");
            File.SetLastWriteTimeUtc(plain,DateTime.UtcNow.AddMinutes(1));
            Check(DesktopLogSource.Find(roots)==redirected,"newer empty placeholder cannot hide packaged contents");
            Check(LogReader.ActiveFromLine(File.ReadAllText(DesktopLogSource.Find(roots)))=="00000000-0000-0000-0000-000000000002","task identity comes from real packaged activity");
            Check(DesktopLogSource.IsPackaged(redirected) && !DesktopLogSource.IsPackaged(plain),"diagnostics identify physical log source");
            File.Delete(plain);
            Check(DesktopLogSource.Find(roots)==redirected,"package-only install works");
            File.WriteAllText(plain,line);
            Check(DesktopLogSource.Find(roots)==redirected,"explicit package path also wins inside Codex virtualization");
            string newer=Path.Combine(normal,"codex-desktop-00000000-0000-0000-0000-000000000003-2-t0-i1-000000-0.log");
            File.WriteAllText(newer,"");File.SetLastWriteTimeUtc(newer,DateTime.UtcNow.AddMinutes(2));
            Check(DesktopLogSource.Find(roots)==newer,"empty new session must not borrow an old task");
            var activity=new DesktopActivityReader();activity.Poll(redirected);
            Check(activity.ActiveThread=="00000000-0000-0000-0000-000000000002","initial activity read");
            string rotated=Path.Combine(packaged,name.Replace("-0.log","-1.log"));File.WriteAllText(rotated,"ordinary log line\n");activity.Poll(rotated);
            Check(activity.ActiveThread=="00000000-0000-0000-0000-000000000002","live rotation keeps the current task");
            var restarted=new DesktopActivityReader();restarted.Poll(rotated);
            Check(restarted.ActiveThread==activity.ActiveThread,"cold start finds activity in previous part of same session");
            string changed=line.Replace("000000000002","000000000004");File.AppendAllText(rotated,changed.TrimEnd('\n'));activity.Poll(rotated);
            Check(activity.ActiveThread=="00000000-0000-0000-0000-000000000002","partial line is held until complete");
            File.AppendAllText(rotated,"\n");activity.Poll(rotated);
            Check(activity.ActiveThread=="00000000-0000-0000-0000-000000000004","new task updates from rotated file");
            activity.Poll(newer);Check(activity.ActiveThread=="","new desktop session cannot inherit old task");
            return count;
        } finally {
            string parent=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(Path.GetFullPath(temporary).StartsWith(parent,StringComparison.OrdinalIgnoreCase) && Directory.Exists(temporary))Directory.Delete(temporary,true);
        }
    }
}
}
