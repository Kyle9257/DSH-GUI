using System.Runtime.InteropServices;
using System.Text;

namespace DshDesktop;

/// <summary>
/// 程序入口：无参数启动桌面 UI（单实例）；有参数进入 CLI 模式（逻辑见 Cli.cs，partial）。
/// </summary>
internal static partial class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SwRestore = 9;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            // CLI 模式：附加到父控制台以便输出（WinExe 默认无控制台）；失败则忽略
            try { AttachConsole(-1); } catch { }
        }
        // 控制台初始化均为尽力而为：在无有效控制台句柄的环境下绝不崩溃
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var svc = new DshService();

        if (args.Length > 0)
        {
            return RunCli(svc, args);
        }

        // 单实例：重复启动时激活已有窗口
        using var mutex = new Mutex(true, "DshDesktop.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            ActivateExisting();
            return 0;
        }

        Application.Run(new AppWindow(svc));
        return 0;
    }
}
