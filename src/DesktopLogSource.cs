using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
}
}
