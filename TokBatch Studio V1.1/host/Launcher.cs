using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

class Launcher
{
    static string UiDir;
    static readonly object Gate = new object();
    static readonly List<string> Lines = new List<string>();
    static Process Download;
    static bool Finished;
    static bool FinishedOk;
    static string FinishedMessage = "";
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

    [STAThread]
    static void Main()
    {
        try
        {
            UiDir = FindUi();
            int port = FreePort(47321);
            var listener = new HttpListener();
            listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
            listener.Start();

            var server = new Thread(delegate()
            {
                while (listener.IsListening)
                {
                    HttpListenerContext ctx = null;
                    try { ctx = listener.GetContext(); }
                    catch { break; }
                    try { Handle(ctx); }
                    catch (Exception ex) { TryClose(ctx, ex.Message); }
                }
            });
            server.IsBackground = true;
            server.SetApartmentState(ApartmentState.STA);
            server.Start();

            string edge = FindEdge();
            string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TokBatchStudio", "edge-profile");
            Directory.CreateDirectory(profile);
            var psi = new ProcessStartInfo();
            psi.FileName = edge;
            psi.Arguments = "--user-data-dir=\"" + profile + "\" --app=\"http://127.0.0.1:" + port + "/\" --window-size=1280,720 --no-first-run --disable-extensions --disable-sync";
            psi.UseShellExecute = false;
            var edgeProc = Process.Start(psi);
            edgeProc.WaitForExit();
            listener.Stop();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "TokBatch Studio", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    static void Handle(HttpListenerContext ctx)
    {
        string path = ctx.Request.Url.AbsolutePath;
        if (path.StartsWith("/api/"))
        {
            string name = path.Substring(5);
            string body = new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEnd();
            object arg = "";
            if (body.Length > 0)
            {
                var map = Json.Deserialize<Dictionary<string, object>>(body);
                if (map != null && map.ContainsKey("a") && map["a"] != null) arg = map["a"];
            }
            if (name == "poll") { WriteJson(ctx, Poll()); return; }
            if (name == "info" && ctx.Request.HttpMethod == "GET") { WriteJson(ctx, new Dictionary<string, object>()); return; }
            object value = Dispatch(name, arg == null ? "" : arg.ToString());
            var wrap = new Dictionary<string, object>();
            wrap["value"] = value;
            WriteJson(ctx, wrap);
            return;
        }
        Serve(ctx, path);
    }

    static object Dispatch(string name, string arg)
    {
        if (name == "info" || name == "appInfo")
        {
            string tool = YtPath();
            if (tool.Length == 0) tool = "não encontrado";
            var info = new Dictionary<string, object>();
            info["ytdlp"] = tool;
            return info;
        }
        if (name == "pasteClipboard")
        {
            if (Clipboard.ContainsText()) return Clipboard.GetText();
            return "";
        }
        if (name == "pickFolder")
        {
            string picked = "";
            var d = new FolderBrowserDialog();
            d.Description = "Escolher pasta de destino";
            d.SelectedPath = arg;
            if (d.ShowDialog() == DialogResult.OK) picked = d.SelectedPath;
            return picked;
        }
        if (name == "openFolder")
        {
            try
            {
                if (arg.Trim().Length == 0) return false;
                Directory.CreateDirectory(arg);
                Process.Start("explorer.exe", arg);
                return true;
            }
            catch { return false; }
        }
        if (name == "stopDownload")
        {
            lock (Gate)
            {
                if (Download != null && !Download.HasExited) Download.Kill();
            }
            return "ok";
        }
        if (name == "startDownload") return StartDownload(arg);
        return null;
    }

    static string StartDownload(string raw)
    {
        var req = Json.Deserialize<Dictionary<string, object>>(raw);
        string url = Str(req, "url");
        string dest = Str(req, "dest");
        if (url.Length == 0 || dest.Length == 0) return "Informe o link e a pasta.";
        string tool = YtPath();
        if (tool.Length == 0) return "missing";
        lock (Gate)
        {
            if (Download != null && !Download.HasExited) return "busy";
            Finished = false;
            FinishedMessage = "";
            Lines.Clear();
        }
        var thread = new Thread(delegate() { RunDownload(tool, req, url, dest); });
        thread.IsBackground = true;
        thread.Start();
        return "ok";
    }

    static void RunDownload(string tool, Dictionary<string, object> req, string url, string dest)
    {
        try
        {
            Directory.CreateDirectory(dest);
            if (Bool(req, "update"))
            {
                Log("Atualizando yt-dlp…");
                var up = new Process();
                up.StartInfo.FileName = tool;
                up.StartInfo.Arguments = "-U";
                up.StartInfo.CreateNoWindow = true;
                up.StartInfo.UseShellExecute = false;
                up.StartInfo.RedirectStandardOutput = true;
                up.StartInfo.RedirectStandardError = true;
                up.Start();
                string text = up.StandardOutput.ReadToEnd() + up.StandardError.ReadToEnd();
                up.WaitForExit();
                if (text.Trim().Length > 0) Log(text.Trim());
            }
            var args = new StringBuilder();
            if (Bool(req, "skip")) args.Append("--no-overwrites ");
            args.Append("--newline --no-mtime --windows-filenames ");
            if (Bool(req, "json")) args.Append("--write-info-json ");
            if (Bool(req, "thumb")) args.Append("--write-thumbnail ");
            string cookies = Str(req, "cookies");
            if (cookies.Length > 0) args.Append("--cookies-from-browser ").Append(cookies).Append(" ");
            args.Append("-o \"").Append(Path.Combine(dest, "%(uploader)s", "%(id)s.%(ext)s")).Append("\" ");
            args.Append("\"").Append(url).Append("\"");
            var cmd = new Process();
            cmd.StartInfo.FileName = tool;
            cmd.StartInfo.Arguments = args.ToString();
            cmd.StartInfo.CreateNoWindow = true;
            cmd.StartInfo.UseShellExecute = false;
            cmd.StartInfo.RedirectStandardOutput = true;
            cmd.StartInfo.RedirectStandardError = true;
            cmd.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null && e.Data.Trim().Length > 0) Log(e.Data.Trim()); };
            cmd.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null && e.Data.Trim().Length > 0) Log(e.Data.Trim()); };
            cmd.Start();
            lock (Gate) Download = cmd;
            cmd.BeginOutputReadLine();
            cmd.BeginErrorReadLine();
            Log("Baixando " + url);
            cmd.WaitForExit();
            Finish(cmd.ExitCode == 0, cmd.ExitCode == 0 ? "Concluído." : "O download não foi concluído.");
        }
        catch (Exception ex)
        {
            Finish(false, ex.Message);
        }
        finally
        {
            lock (Gate) Download = null;
        }
    }

    static Dictionary<string, object> Poll()
    {
        var result = new Dictionary<string, object>();
        lock (Gate)
        {
            result["lines"] = Lines.ToArray();
            Lines.Clear();
            result["finished"] = Finished;
            result["ok"] = FinishedOk;
            result["message"] = Finished ? FinishedMessage : "";
            Finished = false;
        }
        return result;
    }

    static void Log(string line)
    {
        lock (Gate) Lines.Add(line);
    }

    static void Finish(bool ok, string message)
    {
        lock (Gate)
        {
            Finished = true;
            FinishedOk = ok;
            FinishedMessage = message;
        }
    }

    static void Serve(HttpListenerContext ctx, string path)
    {
        if (path == "/") path = "/index.html";
        string rel = path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        string full = Path.GetFullPath(Path.Combine(UiDir, rel));
        if (!full.StartsWith(Path.GetFullPath(UiDir), StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
            return;
        }
        byte[] bytes = File.ReadAllBytes(full);
        if (path == "/index.html")
        {
            string html = Encoding.UTF8.GetString(bytes);
            string dest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "TokBatch");
            html = html.Replace(@"C:\Users\Administrador\Downloads\TokBatch", dest);
            bytes = Encoding.UTF8.GetBytes(html);
        }
        string ext = Path.GetExtension(full).ToLowerInvariant();
        ctx.Response.ContentType = ext == ".css" ? "text/css" : ext == ".js" ? "text/javascript" : ext == ".png" ? "image/png" : "text/html; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.Close();
    }

    static void WriteJson(HttpListenerContext ctx, object obj)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(Json.Serialize(obj));
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.Close();
    }

    static void TryClose(HttpListenerContext ctx, string message)
    {
        try
        {
            if (ctx == null) return;
            ctx.Response.StatusCode = 500;
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }
        catch { }
    }

    static string FindUi()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        string[] candidates = new string[] {
            Path.Combine(baseDir, "src", "ui"),
            Path.GetFullPath(Path.Combine(baseDir, "..", "src", "ui")),
            Path.Combine(baseDir, "ui")
        };
        foreach (string c in candidates)
        {
            if (File.Exists(Path.Combine(c, "index.html"))) return c;
        }
        throw new DirectoryNotFoundException("Não encontrei a pasta da interface.");
    }

    static string FindEdge()
    {
        string[] candidates = new string[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles"), "Microsoft", "Edge", "Application", "msedge.exe")
        };
        foreach (string c in candidates)
        {
            if (File.Exists(c)) return c;
        }
        throw new FileNotFoundException("Microsoft Edge não foi encontrado.");
    }

    static string YtPath()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        string[] candidates = new string[] {
            Path.Combine(baseDir, "yt-dlp.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TokBatch Studio", "bin", "yt-dlp.exe")
        };
        foreach (string c in candidates)
        {
            if (File.Exists(c)) return c;
        }
        return "";
    }

    static int FreePort(int start)
    {
        for (int p = start; p < start + 20; p++)
        {
            try
            {
                var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, p);
                probe.Start();
                probe.Stop();
                return p;
            }
            catch { }
        }
        return start;
    }

    static string Str(Dictionary<string, object> map, string key)
    {
        if (map == null || !map.ContainsKey(key) || map[key] == null) return "";
        return map[key].ToString().Trim();
    }

    static bool Bool(Dictionary<string, object> map, string key)
    {
        if (map == null || !map.ContainsKey(key) || map[key] == null) return false;
        return map[key].ToString().ToLowerInvariant() == "true";
    }
}
