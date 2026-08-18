using System.Text.Json;

namespace DshDesktop;

/// <summary>
/// CLI 模式（partial）：--status / --start / --stop / --stats / --help。
/// 供脚本/自动化使用；退出码约定见 ExitCodes。
/// </summary>
internal static partial class Program
{
    private static int RunCli(DshService svc, string[] args)
    {
        string cmd = args[0].ToLowerInvariant();
        int port = DshService.ResolvePort(ParsePortArg(args));

        switch (cmd)
        {
            case "--status":
                var st = svc.GetState(port);
                Console.WriteLine(JsonSerializer.Serialize(st, new JsonSerializerOptions { WriteIndented = true }));
                return ExitCodes.Ok;

            case "--start":
                int rc = svc.Start(port);
                if (rc != ExitCodes.Ok) return rc;
                if (HasFlag(args, "--wait"))
                {
                    if (svc.WaitReady(port, 30))
                    {
                        Console.WriteLine($"DSH 已就绪: http://127.0.0.1:{port}");
                        return ExitCodes.Ok;
                    }
                    Console.WriteLine("DSH 启动超时(30s)：请查看日志");
                    return ExitCodes.StartTimeout;
                }
                Console.WriteLine($"DSH 启动进程已拉起 (port={port})");
                return ExitCodes.Ok;

            case "--stop":
                return svc.Stop(port, force: HasFlag(args, "--force"));

            case "--stats":
                var mu = ModelUsage.Snapshot();
                string? balance = Task.Run(ModelUsage.FetchBalanceAsync).GetAwaiter().GetResult();
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    model = new
                    {
                        currentSessionTitle = mu.CurrentSessionTitle,
                        currentWorkspace = mu.CurrentWorkspace,
                        contextUsedTokens = mu.CurrentPressureTokens,
                        contextWindow = mu.CurrentContextWindow,
                        contextPercent = mu.CurrentContextWindow > 0 ? Math.Round(100.0 * mu.CurrentPressureTokens / mu.CurrentContextWindow, 1) : 0,
                        contextBreakdown = new
                        {
                            system = mu.CurrentSystemTokens,
                            tools = mu.CurrentToolsTokens,
                            messages = mu.CurrentMessageTokens,
                        },
                        totalTokens = mu.TotalUncachedInput + mu.TotalCacheRead + mu.TotalCacheWrite + mu.TotalOutput,
                        uncachedInputTokens = mu.TotalUncachedInput,
                        cacheReadTokens = mu.TotalCacheRead,
                        cacheWriteTokens = mu.TotalCacheWrite,
                        outputTokens = mu.TotalOutput,
                        sessionCount = mu.SessionCount,
                        estimatedCostCny = Math.Round(mu.TotalCostCny, 2),
                    },
                    balance,
                }, new JsonSerializerOptions { WriteIndented = true }));
                return ExitCodes.Ok;

            case "--help":
            case "-h":
                PrintHelp();
                return ExitCodes.Ok;

            default:
                Console.WriteLine($"未知参数: {cmd}");
                PrintHelp();
                return 1;
        }
    }

    private static bool HasFlag(string[] args, string flag)
        => Array.Exists(args, a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

    private static int ParsePortArg(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--port", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(args[i + 1], out var p))
                return p;
        }
        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("DeepSeek Harness 桌面端");
        Console.WriteLine("用法:");
        Console.WriteLine("  DeepSeek Harness.exe               启动桌面 UI（原生窗口内嵌 DSH GUI）");
        Console.WriteLine("  DeepSeek Harness.exe --status      输出状态 JSON [--port N]");
        Console.WriteLine("  DeepSeek Harness.exe --stats       输出模型用量 JSON（token/上下文/费用/余额）");
        Console.WriteLine("  DeepSeek Harness.exe --start       启动 DSH（幂等）[--port N] [--wait]");
        Console.WriteLine("  DeepSeek Harness.exe --stop        停止 DSH（安全停止）[--port N] [--force]");
        Console.WriteLine("  DeepSeek Harness.exe --help        显示本帮助");
        Console.WriteLine("端口默认 3080；环境变量 DSH_LAUNCHER_PORT 可覆盖默认值。");
    }

    private static void ActivateExisting()
    {
        try
        {
            var hwnd = FindWindow(null, "DeepSeek Harness");
            if (hwnd != IntPtr.Zero)
            {
                ShowWindow(hwnd, SwRestore);
                SetForegroundWindow(hwnd);
            }
        }
        catch { }
    }
}
