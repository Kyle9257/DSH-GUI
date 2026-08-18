using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DshDesktop;

/// <summary>退出码约定（与 ps1 启动器一致）</summary>
public static class ExitCodes
{
    public const int Ok = 0;            // 成功 / 已在运行（幂等）
    public const int PortBusy = 2;      // 端口被非 DSH 进程占用
    public const int StartTimeout = 3;  // 启动后等待就绪超时
    public const int NotFound = 4;      // node / dsh 未找到
    public const int Refused = 5;       // 停止被拒绝（进程身份无法确认为 DSH）
    public const int StopFailed = 6;    // 停止失败 / 停止后端口未释放
}

/// <summary>DSH 运行状态快照</summary>
public sealed record DshState(
    int Port, bool Running, bool HttpOk, int? Pid, string Url,
    bool Foreign, bool StartedByUs, string? Identity);

/// <summary>
/// DSH 启动/停止/状态/日志服务（exe 自包含，不依赖 ps1 脚本）。
/// 语义与 dsh-launcher/lib.ps1 一致：幂等启动、三重身份安全停止（本应用启动 / 命令行白名单 / 拒绝）。
/// </summary>
public sealed class DshService
{
    public const int DefaultPort = 3080;
    private const long MaxLogBytes = 5L * 1024 * 1024;

    public string DataRoot { get; }
    public string LogDir { get; }
    public string StateFile { get; }
    private string OutLog => Path.Combine(LogDir, "dsh.out.log");
    private string ErrLog => Path.Combine(LogDir, "dsh.err.log");

    private readonly object _lock = new();
    private int _cmdCachePid = -1;
    private string? _cmdCache;

    public DshService(string? dataRoot = null)
    {
        DataRoot = dataRoot
            ?? Environment.GetEnvironmentVariable("DSH_DESKTOP_DATA")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DshDesktop");
        LogDir = Path.Combine(DataRoot, "logs");
        StateFile = Path.Combine(DataRoot, "state", "launcher-state.json");
    }

    // ---------- 端口解析 ----------

    public static int ResolvePort(int port)
    {
        if (port > 0) return port;
        var env = Environment.GetEnvironmentVariable("DSH_LAUNCHER_PORT");
        if (!string.IsNullOrEmpty(env) && int.TryParse(env, out var p) && p > 0) return p;
        return DefaultPort;
    }

    // ---------- 路径解析 ----------

    public string? ResolveNode()
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var raw in pathVar.Split(Path.PathSeparator))
        {
            var dir = raw.Trim().Trim('"');
            if (dir.Length == 0) continue;
            var cand = Path.Combine(dir, "node.exe");
            if (File.Exists(cand)) return cand;
        }
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var known = new[]
        {
            Path.Combine(pf, "nodejs", "node.exe"),
            Path.Combine(pfx86, "nodejs", "node.exe"),
            Path.Combine(local, "Programs", "nodejs", "node.exe"),
        };
        foreach (var c in known) if (File.Exists(c)) return c;
        return null;
    }

    public string? ResolveDshBin()
    {
        var envBin = Environment.GetEnvironmentVariable("DSH_BIN");
        if (!string.IsNullOrEmpty(envBin) && File.Exists(envBin)) return envBin;

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var known = Path.Combine(local, @"npm-cache\_npx\1e7f6d9597241db0\node_modules\@deepseek-ai\dsh\lib\bin.js");
        if (File.Exists(known)) return known;

        // 由 PATH 上的 dsh shim 反推包内 bin.js（shim 位于 ...\_npx\<hash>\node_modules\.bin\）
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var raw in pathVar.Split(Path.PathSeparator))
        {
            var dir = raw.Trim().Trim('"');
            if (dir.Length == 0) continue;
            foreach (var shimName in new[] { "dsh.cmd", "dsh.ps1", "dsh" })
            {
                var shim = Path.Combine(dir, shimName);
                if (File.Exists(shim))
                {
                    var binDir = Path.GetDirectoryName(dir);           // node_modules
                    var pkg = Path.Combine(binDir ?? "", "@deepseek-ai", "dsh", "lib", "bin.js");
                    if (File.Exists(pkg)) return pkg;
                }
            }
        }

        // 在 npx 缓存中搜索最新的 @deepseek-ai/dsh/lib/bin.js
        var npxRoot = Path.Combine(local, "npm-cache", "_npx");
        if (Directory.Exists(npxRoot))
        {
            string? best = null;
            DateTime bestTime = DateTime.MinValue;
            foreach (var dir in Directory.EnumerateDirectories(npxRoot))
            {
                var f = Path.Combine(dir, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");
                if (File.Exists(f))
                {
                    var t = File.GetLastWriteTime(f);
                    if (t > bestTime) { bestTime = t; best = f; }
                }
            }
            if (best != null) return best;
        }
        return null;
    }

    // ---------- 探测 ----------

    public bool IsPortListening(int port)
    {
        try
        {
            using var c = new TcpClient();
            var ar = c.BeginConnect("127.0.0.1", port, null, null);
            bool ok = ar.AsyncWaitHandle.WaitOne(500, false);
            return ok && c.Connected;
        }
        catch { return false; }
    }

    public bool IsHttpOk(int port)
    {
        try
        {
            var req = (HttpWebRequest)WebRequest.Create($"http://127.0.0.1:{port}/");
            req.Method = "GET";
            req.Timeout = 3000;
            req.ReadWriteTimeout = 3000;
            using var resp = (HttpWebResponse)req.GetResponse();
            return (int)resp.StatusCode == 200;
        }
        catch { return false; }
    }

    public int? GetPortPid(int port)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("netstat", "-ano")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (p == null) return null;
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            string marker = $":{port} ";
            foreach (var rawLine in output.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (line.IndexOf("LISTENING", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length > 0 && int.TryParse(tokens[^1], out var pid)) return pid;
            }
        }
        catch { }
        return null;
    }

    public string? GetProcessCommandLine(int pid)
    {
        lock (_lock)
        {
            if (pid == _cmdCachePid) return _cmdCache;
            var r = ReadCommandLine(pid);
            _cmdCachePid = pid;
            _cmdCache = r;
            return r;
        }
    }

    private static string? ReadCommandLine(int pid)
    {
        try
        {
            // 通过 powershell EncodedCommand 读取（避免引号转义问题）；受限环境返回 null（fail-safe）
            string script = $"(Get-CimInstance Win32_Process -Filter 'ProcessId={pid}').CommandLine";
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            using var p = Process.Start(new ProcessStartInfo("powershell.exe",
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (p == null) return null;
            string outp = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(5000);
            return string.IsNullOrEmpty(outp) ? null : outp;
        }
        catch { return null; }
    }

    public static bool IsDshCommandLine(string? cmdline)
    {
        if (string.IsNullOrEmpty(cmdline)) return false;
        return cmdline.Contains("web")
            && (cmdline.Contains("bin.js") || Regex.IsMatch(cmdline, @"\bdsh\b"));
    }

    // ---------- 状态持久化（供停止时身份确认，格式与 ps1 启动器兼容） ----------

    private sealed class StateDoc
    {
        public Dictionary<string, StartedEntry>? Started { get; set; }
    }

    private sealed class StartedEntry
    {
        public int Pid { get; set; }
        public string? StartedAt { get; set; }
        public string? Node { get; set; }
        public string? Bin { get; set; }
    }

    private StateDoc ReadState()
    {
        try
        {
            if (File.Exists(StateFile))
            {
                var doc = JsonSerializer.Deserialize<StateDoc>(File.ReadAllText(StateFile, Encoding.UTF8));
                if (doc != null) return doc;
            }
        }
        catch { }
        return new StateDoc { Started = new Dictionary<string, StartedEntry>() };
    }

    private void WriteState(StateDoc doc)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
            File.WriteAllText(StateFile, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        }
        catch { }
    }

    private int? ReadStartedPid(int port)
    {
        try
        {
            var doc = ReadState();
            if (doc.Started != null && doc.Started.TryGetValue(port.ToString(), out var e)) return e.Pid;
        }
        catch { }
        return null;
    }

    private void WriteStartedEntry(int port, int pid, string node, string bin)
    {
        var doc = ReadState();
        doc.Started ??= new Dictionary<string, StartedEntry>();
        doc.Started[port.ToString()] = new StartedEntry
        {
            Pid = pid,
            StartedAt = DateTime.UtcNow.ToString("o"),
            Node = node,
            Bin = bin,
        };
        WriteState(doc);
    }

    private void RemoveStartedEntry(int port)
    {
        var doc = ReadState();
        if (doc.Started != null)
        {
            doc.Started.Remove(port.ToString());
            WriteState(doc);
        }
    }

    private static bool IsProcessAlive(int pid)
    {
        try { return !Process.GetProcessById(pid).HasExited; }
        catch { return false; }
    }

    // ---------- 日志 ----------

    private void RollLog(string file)
    {
        try
        {
            if (File.Exists(file) && new FileInfo(file).Length > MaxLogBytes)
                File.Move(file, file + ".1", true);
        }
        catch { }
    }

    public void Log(string message, string level = "info")
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            var logFile = Path.Combine(LogDir, "dsh.log");
            RollLog(logFile);
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level.ToUpperInvariant()}] {message}";
            File.AppendAllText(logFile, line + Environment.NewLine, Encoding.UTF8);
        }
        catch { }
    }

    // ---------- 状态 ----------

    public DshState GetState(int port)
    {
        bool running = IsPortListening(port);
        bool httpOk = false;
        int? pid = null;
        bool foreign = false;
        bool startedByUs = false;
        string? identity = null;
        if (running)
        {
            httpOk = IsHttpOk(port);
            pid = GetPortPid(port);
            var statePid = ReadStartedPid(port);
            if (statePid.HasValue && IsProcessAlive(statePid.Value))
            {
                startedByUs = true;
                pid ??= statePid;
            }
            if (!startedByUs)
            {
                if (pid.HasValue)
                {
                    var cmd = GetProcessCommandLine(pid.Value);
                    if (cmd != null)
                    {
                        identity = cmd;
                        foreign = !IsDshCommandLine(cmd);
                    }
                    else
                    {
                        identity = "(无法读取命令行)";
                        foreign = true;
                    }
                }
                else
                {
                    identity = "(无法获取 PID)";
                    foreign = true;
                }
            }
        }
        return new DshState(port, running, httpOk, pid, $"http://127.0.0.1:{port}", foreign, startedByUs, identity);
    }

    // ---------- 启动 / 停止 ----------

    public int Start(int port)
    {
        var s = GetState(port);
        if (s.Running)
        {
            if (s.Foreign)
            {
                Log($"端口 {port} 被非 DSH 进程(pid={s.Pid})占用，拒绝启动", "warn");
                return ExitCodes.PortBusy;
            }
            Log($"已在运行 (pid={s.Pid}, port={port})，跳过启动", "info");
            return ExitCodes.Ok;
        }

        var node = ResolveNode();
        var bin = ResolveDshBin();
        if (node == null || bin == null)
        {
            Log($"未找到 node({node}) 或 dsh 入口({bin})", "error");
            return ExitCodes.NotFound;
        }

        Directory.CreateDirectory(LogDir);
        RollLog(OutLog);
        RollLog(ErrLog);

        // 直接启动 node（匿名管道承接输出并异步写入日志文件）。
        // 实测：cmd /c 重定向方案会使 dsh web 启动后静默退出、端口永不绑定；
        // 管道方案稳定（dsh web 启动完成后不再依赖本进程存活，进程退出后实例继续运行）。
        var psi = new ProcessStartInfo(node)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(bin);
        psi.ArgumentList.Add("web");
        psi.ArgumentList.Add("--port");
        psi.ArgumentList.Add(port.ToString());

        Process proc;
        try { proc = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start 返回 null"); }
        catch (Exception ex)
        {
            Log($"启动进程失败: {ex.Message}", "error");
            return ExitCodes.StopFailed;
        }

        proc.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data)) AppendLogLine(OutLog, e.Data);
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data)) AppendLogLine(ErrLog, e.Data);
        };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        lock (_lock) { _cmdCachePid = -1; _cmdCache = null; }
        WriteStartedEntry(port, proc.Id, node, bin);
        Log($"已启动 dsh web (pid={proc.Id}, port={port}, node={node})", "info");
        return ExitCodes.Ok;
    }

    private void AppendLogLine(string file, string data)
    {
        try
        {
            File.AppendAllText(file, data + Environment.NewLine, Encoding.UTF8);
        }
        catch { }
    }

    public bool WaitReady(int port, int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (IsHttpOk(port)) return true;
            Thread.Sleep(500);
        }
        return IsHttpOk(port);
    }

    public int Stop(int port, bool force)
    {
        var s = GetState(port);
        if (!s.Running)
        {
            // 顺带清理过期 state 条目（实例已自行退出等场景）
            if (ReadStartedPid(port).HasValue) RemoveStartedEntry(port);
            Log($"端口 {port} 无监听进程，无需停止", "info");
            return ExitCodes.Ok;
        }
        if (s.Foreign && !force)
        {
            Log($"拒绝停止：端口 {port} 的进程(pid={s.Pid})身份无法确认为 DSH (identity={s.Identity})；确需停止请用 -Force", "warn");
            return ExitCodes.Refused;
        }

        int? target = s.Pid ?? ReadStartedPid(port);
        if (target == null)
        {
            Log($"停止失败：无法确定端口 {port} 对应的进程 PID", "error");
            return ExitCodes.StopFailed;
        }
        try
        {
            Process.GetProcessById(target.Value).Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            Log($"停止失败 pid={target}: {ex.Message}", "error");
            return ExitCodes.StopFailed;
        }

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (!IsPortListening(port)) break;
            Thread.Sleep(300);
        }
        if (IsPortListening(port))
        {
            Log($"停止后端口 {port} 仍被占用 (pid={GetPortPid(port)})", "warn");
            return ExitCodes.StopFailed;
        }

        lock (_lock) { _cmdCachePid = -1; _cmdCache = null; }
        RemoveStartedEntry(port);
        Log($"已停止 pid={target} (port={port})", "info");
        return ExitCodes.Ok;
    }
}
