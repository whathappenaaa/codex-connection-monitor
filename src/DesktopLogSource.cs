using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ConnectionMonitor {
// Store-packaged Codex redirects its own LocalAppData reads. An independently
// launched monitor must also inspect the explicit package cache, not an empty
// placeholder at the unredirected path.
public static class DesktopLogSource {
    public static IEnumerable<string> Roots(string local) {
        yield return Path.Combine(local,"Codex","Logs");
        string packages=Path.Combine(local,"Packages");
        string[] installed=new string[0];
        try {if(Directory.Exists(packages))installed=Directory.GetDirectories(packages,"OpenAI.Codex_*");}catch(IOException){}catch(UnauthorizedAccessException){}
        foreach(string package in installed)yield return Path.Combine(package,"LocalCache","Local","Codex","Logs");
    }
    public static string Find(IEnumerable<string> roots) {
        var files=new List<FileInfo>();
        foreach(string root in roots)try {
            if(Directory.Exists(root))files.AddRange(Directory.GetFiles(root,"*-t0-*.log",SearchOption.AllDirectories).Select(p=>new FileInfo(p)));
        }catch(IOException){}catch(UnauthorizedAccessException){}
        // Two paths can represent copies of the same desktop session log.
        // Choose the newest session log before resolving its physical copy;
        // an empty new session must not silently fall back to an older task.
        var latest=files.GroupBy(f=>f.Name,StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group=>group.Max(f=>f.LastWriteTimeUtc)).FirstOrDefault();
        if(latest==null)return null;
        var copies=latest.OrderByDescending(f=>IsPackaged(f.FullName)).ThenByDescending(f=>f.LastWriteTimeUtc).ToList();
        foreach(FileInfo file in copies)try {
            // FileInfo metadata can remain stale while the desktop writer has
            // its file open. Inspect the shared read handle instead.
            using(var stream=new FileStream(file.FullName,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
                if(stream.Length>0)return file.FullName;
        }catch(IOException){}catch(UnauthorizedAccessException){}
        return copies[0].FullName;
    }
    public static bool IsPackaged(string path) {return !String.IsNullOrEmpty(path) && path.IndexOf(Path.DirectorySeparatorChar+"Packages"+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)>=0;}
    static readonly Regex PartPattern=new Regex(@"^(.*-t0-i\d+-\d{6})-(\d+)\.log$",RegexOptions.IgnoreCase);
    public static string Session(string path){Match match=PartPattern.Match(Path.GetFileName(path));return match.Success?match.Groups[1].Value:Path.GetFileName(path);}
    public static int Part(string path){Match match=PartPattern.Match(Path.GetFileName(path));int part;return match.Success && Int32.TryParse(match.Groups[2].Value,out part)?part:0;}
    public static string[] Series(string newest){string session=Session(newest);return Directory.GetFiles(Path.GetDirectoryName(newest),"*-t0-*.log").Where(p=>Session(p)==session && Part(p)<=Part(newest)).OrderBy(Part).ToArray();}
}
public sealed class DesktopActivityReader {
    sealed class Tail {public long Offset;public string Partial="";}
    readonly Dictionary<string,Tail> tails=new Dictionary<string,Tail>(StringComparer.OrdinalIgnoreCase);
    string session="",directory="";bool initialized;
    public string ActiveThread="";
    public void Poll(string newest){
        if(String.IsNullOrEmpty(newest) || !File.Exists(newest))return;
        string nextSession=DesktopLogSource.Session(newest),nextDirectory=Path.GetDirectoryName(newest);
        if(session!=nextSession || directory!=nextDirectory){if(session!=nextSession)ActiveThread="";session=nextSession;directory=nextDirectory;tails.Clear();initialized=false;}
        string[] series=DesktopLogSource.Series(newest);
        if(!initialized){
            // On startup search backwards only as far as the last activity event
            // in this exact process/session, never a different older task session.
            foreach(string file in series.Reverse()){string active=Read(file);if(active!=""){ActiveThread=active;break;}}
            initialized=true;return;
        }
        int newestRead=tails.Count==0?-1:tails.Keys.Max(p=>DesktopLogSource.Part(p));
        foreach(string file in series.Where(p=>tails.ContainsKey(p) || DesktopLogSource.Part(p)>newestRead)){
            string active=Read(file);if(active!="")ActiveThread=active;
        }
    }
    string Read(string file){
        Tail tail;if(!tails.TryGetValue(file,out tail)){tail=new Tail();tails[file]=tail;}
        string active="";
        using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
            if(tail.Offset>stream.Length){tail.Offset=0;tail.Partial="";}
            stream.Position=tail.Offset;long end=stream.Length;
            while(stream.Position<end){
                byte[] bytes=new byte[(int)Math.Min(128*1024,end-stream.Position)];int count=stream.Read(bytes,0,bytes.Length);if(count==0)break;tail.Offset+=count;
                string text=tail.Partial+Encoding.UTF8.GetString(bytes,0,count);int last=text.LastIndexOf('\n');
                if(last<0){tail.Partial=text.Length>1024*1024?"":text;continue;}tail.Partial=text.Substring(last+1);
                foreach(string line in text.Substring(0,last).Split('\n')){string id=LogReader.ActiveFromLine(line);if(id!="")active=id;}
            }
        }return active;
    }
}
}
