using System.Text.Json;

namespace DshDesktop;

/// <summary>一个会话的模型用量行</summary>
public sealed record SessionUsageRow(
    string? SessionId,
    string? Title,
    long Seq,
    long UncachedInput,
    long CacheRead,
    long CacheWrite,
    long Output,
    long ContextWindow,
    long PressureTokens,
    long SystemTokens,
    long ToolsTokens,
    long MessageTokens);

/// <summary>模型用量快照（当前会话 + 全会话合计）</summary>
public sealed record ModelUsageSnapshot(
    string? CurrentSessionTitle,
    string? CurrentWorkspace,
    long CurrentPressureTokens,
    long CurrentContextWindow,
    long CurrentSystemTokens,
    long CurrentToolsTokens,
    long CurrentMessageTokens,
    long TotalUncachedInput,
    long TotalCacheRead,
    long TotalCacheWrite,
    long TotalOutput,
    long SessionCount,
    double TotalCostCny);

/// <summary>
/// 模型使用信息数据面：读取 DSH 服务端落盘的会话投影缓存
/// （%USERPROFILE%\.dsh\storages\session_projcache.json，token-meter 每会话写入
/// tokenUsage / contextPressure / contextBreakdown），并查询 DeepSeek 账户余额。
/// 费用按 DeepSeek 官方定价估算（CNY/1M tokens），可用环境变量覆盖：
/// DSH_DESKTOP_PRICE_INPUT / DSH_DESKTOP_PRICE_CACHE_READ / DSH_DESKTOP_PRICE_OUTPUT。
/// </summary>
public static class ModelUsage
{
    private static readonly string DshHome = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh");
    private static readonly string ProjCachePath = Path.Combine(DshHome, "storages", "session_projcache.json");
    private static readonly string CredentialsPath = Path.Combine(DshHome, ".credentials.yaml");

    // DeepSeek 官方定价（CNY / 1M tokens，deepseek-chat 假设），可用环境变量覆盖
    public static readonly double PriceInputPerM = ReadPriceEnv("DSH_DESKTOP_PRICE_INPUT", 2.0);
    public static readonly double PriceCacheReadPerM = ReadPriceEnv("DSH_DESKTOP_PRICE_CACHE_READ", 0.5);
    public static readonly double PriceOutputPerM = ReadPriceEnv("DSH_DESKTOP_PRICE_OUTPUT", 8.0);

    private static double ReadPriceEnv(string name, double fallback)
    {
        var v = Environment.GetEnvironmentVariable(name);
        return double.TryParse(v, System.Globalization.CultureInfo.InvariantCulture, out var p) && p >= 0 ? p : fallback;
    }

    // ---------- 投影缓存读取 ----------

    public static List<SessionUsageRow> LoadSessions()
    {
        var rows = new List<SessionUsageRow>();
        try
        {
            if (!File.Exists(ProjCachePath)) return rows;
            using var doc = JsonDocument.Parse(File.ReadAllText(ProjCachePath, System.Text.Encoding.UTF8));
            if (!doc.RootElement.TryGetProperty("tables", out var tables)) return rows;
            if (!tables.TryGetProperty("sessions", out var sessions)) return rows;
            foreach (var session in sessions.EnumerateObject())
            {
                string? id = session.Name;
                string? title = null;
                long seq = 0, uncached = 0, cacheRead = 0, cacheWrite = 0, output = 0;
                long window = 0, pressure = 0, sysTokens = 0, toolTokens = 0, msgTokens = 0;
                if (session.Value.TryGetProperty("rows", out var rowsNode))
                {
                    if (rowsNode.TryGetProperty("title", out var t) && t.TryGetProperty("val", out var tv))
                        title = tv.GetString();
                    if (rowsNode.TryGetProperty("tokenUsage", out var tu) &&
                        tu.TryGetProperty("val", out var tuv) && tuv.TryGetProperty("totals", out var totals))
                    {
                        uncached = GetLong(totals, "uncachedInputTokens");
                        cacheRead = GetLong(totals, "cacheReadTokens");
                        cacheWrite = GetLong(totals, "cacheWriteTokens");
                        output = GetLong(totals, "outputTokens");
                    }
                    if (rowsNode.TryGetProperty("contextPressure", out var cp) && cp.TryGetProperty("val", out var cpv))
                    {
                        window = GetLong(cpv, "contextWindow");
                        pressure = GetLong(cpv, "pressureTokens");
                    }
                    if (rowsNode.TryGetProperty("contextBreakdown", out var cb) && cb.TryGetProperty("val", out var cbv))
                    {
                        sysTokens = GetLong(cbv, "systemTokens");
                        toolTokens = GetLong(cbv, "toolsTokens");
                        msgTokens = GetLong(cbv, "messageTokens");
                    }
                    if (rowsNode.TryGetProperty("sessionStats", out var ss) && ss.TryGetProperty("seq", out var sq))
                        seq = sq.GetInt64();
                }
                rows.Add(new SessionUsageRow(id, title, seq, uncached, cacheRead, cacheWrite, output,
                    window, pressure, sysTokens, toolTokens, msgTokens));
            }
        }
        catch
        {
            // 缓存可能正在被服务端写入：读取失败返回空（展示层显示占位）
        }
        return rows;
    }

    private static long GetLong(JsonElement obj, string name)
    {
        if (obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number)
            return v.GetInt64();
        return 0;
    }

    public static ModelUsageSnapshot Snapshot()
    {
        var sessions = LoadSessions();
        // 当前会话 = 最近写入（seq 最大）
        SessionUsageRow? current = null;
        foreach (var s in sessions) if (current == null || s.Seq > current.Seq) current = s;

        long tUncached = 0, tCacheRead = 0, tCacheWrite = 0, tOutput = 0;
        foreach (var s in sessions)
        {
            tUncached += s.UncachedInput;
            tCacheRead += s.CacheRead;
            tCacheWrite += s.CacheWrite;
            tOutput += s.Output;
        }
        double cost = (tUncached + tCacheWrite) / 1_000_000.0 * PriceInputPerM
            + tCacheRead / 1_000_000.0 * PriceCacheReadPerM
            + tOutput / 1_000_000.0 * PriceOutputPerM;
        return new ModelUsageSnapshot(
            current?.Title,
            current?.SessionId == null ? null : WorkspaceTitleOf(current.SessionId),
            current?.PressureTokens ?? 0,
            current?.ContextWindow ?? 0,
            current?.SystemTokens ?? 0,
            current?.ToolsTokens ?? 0,
            current?.MessageTokens ?? 0,
            tUncached, tCacheRead, tCacheWrite, tOutput,
            sessions.Count,
            cost);
    }

    private static string? WorkspaceTitleOf(string sessionId)
    {
        try
        {
            var ws = Path.Combine(DshHome, "storages", "workspace.json");
            if (!File.Exists(ws)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(ws, System.Text.Encoding.UTF8));
            if (!doc.RootElement.TryGetProperty("tables", out var tables) ||
                !tables.TryGetProperty("workspaces", out var workspaces)) return null;
            foreach (var w in workspaces.EnumerateObject())
            {
                if (w.Value.TryGetProperty("sessionIds", out var ids))
                {
                    foreach (var id in ids.EnumerateArray())
                        if (id.GetString() == sessionId && w.Value.TryGetProperty("title", out var title))
                            return title.GetString();
                }
            }
        }
        catch { }
        return null;
    }

    // ---------- 余额查询 ----------

    public static string? ReadApiKey()
    {
        try
        {
            if (!File.Exists(CredentialsPath)) return null;
            foreach (var line in File.ReadAllLines(CredentialsPath, System.Text.Encoding.UTF8))
            {
                var m = System.Text.RegularExpressions.Regex.Match(line, @"^\s*DEEPSEEK_API_KEY\s*:\s*[""']?([^""'#\s]+)");
                if (m.Success) return m.Groups[1].Value;
            }
        }
        catch { }
        return null;
    }

    /// <summary>查询 DeepSeek 账户余额；失败或未配置返回 null。</summary>
    public static async Task<string?> FetchBalanceAsync()
    {
        var key = ReadApiKey();
        if (string.IsNullOrEmpty(key)) return null;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
            var resp = await client.GetAsync("https://api.deepseek.com/user/balance");
            if (!resp.IsSuccessStatusCode) return null;
            var text = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(text);
            if (!doc.RootElement.TryGetProperty("balance_infos", out var infos)) return null;
            foreach (var info in infos.EnumerateArray())
            {
                if (info.TryGetProperty("total_balance", out var tb) && tb.ValueKind == JsonValueKind.String)
                {
                    var currency = info.TryGetProperty("currency", out var c) ? c.GetString() : "CNY";
                    return $"{currency} {tb.GetString()}";
                }
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    // ---------- 格式化辅助 ----------

    public static string FormatTokens(long n)
    {
        if (n >= 1_000_000) return $"{n / 1_000_000.0:0.0}M";
        if (n >= 1_000) return $"{n / 1_000.0:0.0}K";
        return n.ToString();
    }
}
