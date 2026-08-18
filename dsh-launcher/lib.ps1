# lib.ps1 — DeepSeek Harness 启动器共享逻辑（唯一业务代码）
# 兼容 Windows PowerShell 5.1 与 PowerShell 7。
# 由 start-dsh.ps1 / stop-dsh.ps1 / status.ps1 / dashboard.ps1 / smoke.ps1 复用，保证单一代码路径。

Set-StrictMode -Version 2.0

$script:LAUNCHER_ROOT = Split-Path -Parent $MyInvocation.MyCommand.Path
$script:LOG_DIR   = Join-Path $script:LAUNCHER_ROOT 'logs'
$script:STATE_DIR = Join-Path $script:LAUNCHER_ROOT 'state'
$script:STATE_FILE = Join-Path $script:STATE_DIR 'launcher-state.json'
$script:MAX_LOG_BYTES = 5MB
$script:DEFAULT_PORT = 3080

# 错误码约定（所有脚本统一）
$script:EX_OK            = 0   # 成功 / 已在运行（幂等）
$script:EX_PORT_BUSY     = 2   # 端口被非 DSH 进程占用
$script:EX_START_TIMEOUT = 3   # 启动后等待就绪超时
$script:EX_NOT_FOUND     = 4   # node / dsh 未找到
$script:EX_REFUSED       = 5   # 停止被拒绝（进程身份无法确认为 DSH）
$script:EX_STOP_FAILED   = 6   # 停止失败 / 停止后端口未释放

# ---------- 路径解析 ----------

function Get-DshPort {
    # 端口优先级：显式参数 > 环境变量 DSH_LAUNCHER_PORT > 默认 3080
    param([int]$Port = 0)
    if ($Port -gt 0) { return $Port }
    $envPort = $env:DSH_LAUNCHER_PORT
    if ($envPort -and $envPort -match '^\d+$') { return [int]$envPort }
    return $script:DEFAULT_PORT
}

function Resolve-Node {
    $c = Get-Command node -ErrorAction SilentlyContinue
    if ($c -and $c.Source) { return $c.Source }
    $candidates = @(
        "$env:ProgramFiles\nodejs\node.exe",
        "${env:ProgramFiles(x86)}\nodejs\node.exe",
        "$env:LOCALAPPDATA\Programs\nodejs\node.exe"
    )
    foreach ($p in $candidates) {
        if ($p -and (Test-Path -LiteralPath $p)) { return $p }
    }
    return $null
}

function Resolve-DshBin {
    # 1) 环境变量显式覆盖
    if ($env:DSH_BIN) {
        if (Test-Path -LiteralPath $env:DSH_BIN) { return $env:DSH_BIN }
    }
    # 2) 当前已知的 npx 缓存安装位置
    $known = Join-Path $env:LOCALAPPDATA 'npm-cache\_npx\1e7f6d9597241db0\node_modules\@deepseek-ai\dsh\lib\bin.js'
    if (Test-Path -LiteralPath $known) { return $known }
    # 3) 由 PATH 上的 dsh shim 反推包内 bin.js（shim 位于 ...\_npx\<hash>\node_modules\.bin\）
    $cmd = Get-Command dsh -ErrorAction SilentlyContinue
    if ($cmd -and $cmd.Source) {
        $shimDir = Split-Path -Parent $cmd.Source
        $pkgBin = Join-Path (Split-Path -Parent $shimDir) '@deepseek-ai\dsh\lib\bin.js'
        if (Test-Path -LiteralPath $pkgBin) { return $pkgBin }
    }
    # 4) 在 npx 缓存中搜索最新的 @deepseek-ai/dsh/lib/bin.js
    $cacheRoot = Join-Path $env:LOCALAPPDATA 'npm-cache\_npx'
    if (Test-Path $cacheRoot) {
        $found = Get-ChildItem -Path $cacheRoot -Recurse -Filter 'bin.js' -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match 'node_modules\\@deepseek-ai\\dsh\\lib\\bin\.js$' } |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1
        if ($found) { return $found.FullName }
    }
    return $null
}

# ---------- 探测 ----------

function Get-PortPid {
    # 返回监听指定端口的进程 PID；无监听返回 $null。
    # 注意：部分受限环境（沙箱）下 Get-NetTCPConnection 不可用，此时返回 $null，属预期。
    param([int]$Port)
    $conn = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
    if ($conn) { return [int]$conn[0].OwningProcess }
    return $null
}

function Test-PortListening {
    # 用 TCP 连接探测端口是否有监听者（环境无关，比 Get-NetTCPConnection 更通用）
    param([int]$Port)
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $iar = $client.BeginConnect('127.0.0.1', $Port, $null, $null)
        $ok = $iar.AsyncWaitHandle.WaitOne(500, $false)
        if ($ok -and $client.Connected) { return $true }
        return $false
    } catch {
        return $false
    } finally {
        if ($client) { $client.Close() }
    }
}

function Test-DshHttp {
    # 对 http://127.0.0.1:$Port/ 发请求，判断是否返回 200
    param([int]$Port)
    try {
        $r = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/" -UseBasicParsing -TimeoutSec 3
        return ($r.StatusCode -eq 200)
    } catch {
        return $false
    }
}

function Get-ProcessCommandLine {
    # 读取进程命令行；不可读时返回 $null（沙箱/权限受限环境）
    param([int]$ProcessId)
    try {
        $c = Get-CimInstance Win32_Process -Filter "ProcessId=$ProcessId" -ErrorAction Stop
        if ($c) { return [string]$c.CommandLine }
    } catch { }
    return $null
}

function Test-DshCommandLine {
    # 白名单：命令行必须同时包含 "web" 与 "bin.js"（或 "dsh"）才视为 DSH 进程
    param([string]$CommandLine)
    if (-not $CommandLine) { return $false }
    return ($CommandLine -match 'web' -and ($CommandLine -match 'bin\.js' -or $CommandLine -match '\bdsh\b'))
}

# ---------- 状态持久化（供停止时身份确认） ----------

function Read-State {
    try {
        if (Test-Path -LiteralPath $script:STATE_FILE) {
            $o = Get-Content -LiteralPath $script:STATE_FILE -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($o) { return $o }
        }
    } catch { }
    return [pscustomobject]@{ started = @{} }
}

function Write-State {
    param($State)
    try {
        New-Item -ItemType Directory -Force -Path $script:STATE_DIR | Out-Null
        $State | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $script:STATE_FILE -Encoding UTF8
    } catch { }
}

function ConvertTo-StartedHashtable {
    # JSON 反序列化后 started 是 PSCustomObject（无法直接赋新属性）→ 统一转成 Hashtable
    param($Started)
    if ($Started -is [System.Collections.IDictionary]) { return $Started }
    $ht = @{}
    if ($Started) {
        foreach ($prop in $Started.PSObject.Properties) {
            if ($null -ne $prop.Value) { $ht[$prop.Name] = $prop.Value }
        }
    }
    return $ht
}

function Remove-StateEntry {
    param([int]$Port)
    $state = Read-State
    if ($state.started) {
        $started = ConvertTo-StartedHashtable $state.started
        if ($started.ContainsKey("$Port")) { $started.Remove("$Port") | Out-Null }
        $state.started = $started
        Write-State $state
    }
}

# ---------- 日志 ----------

function Roll-DshLog {
    param([string]$File)
    try {
        if ((Test-Path -LiteralPath $File) -and ((Get-Item -LiteralPath $File).Length -gt $script:MAX_LOG_BYTES)) {
            Move-Item -LiteralPath $File -Destination ($File + '.1') -Force -ErrorAction SilentlyContinue
        }
    } catch { }
}

function Write-DshLog {
    param([string]$Message, [string]$Level = 'info')
    try {
        New-Item -ItemType Directory -Force -Path $script:LOG_DIR | Out-Null
        $logFile = Join-Path $script:LOG_DIR 'dsh.log'
        Roll-DshLog $logFile
        $line = "{0} [{1}] {2}" -f (Get-Date).ToString('yyyy-MM-dd HH:mm:ss'), $Level.ToUpper(), $Message
        [System.IO.File]::AppendAllText($logFile, $line + [Environment]::NewLine, [System.Text.Encoding]::UTF8)
    } catch { }
}

# ---------- 状态 ----------

function Get-DshStatus {
    param([int]$Port = 0)
    $Port = Get-DshPort $Port
    $state = Read-State
    $statePid = $null
    try {
        if ($state.started -and $state.started."$Port") { $statePid = [int]$state.started."$Port".pid }
    } catch { }
    $running = Test-PortListening $Port
    $httpOk = $false
    $foreign = $false
    $startedByUs = $false
    $identity = $null
    $procId = $null
    if ($running) {
        $httpOk = Test-DshHttp $Port
        $procId = Get-PortPid $Port
        if ($statePid -and (Get-Process -Id $statePid -ErrorAction SilentlyContinue)) {
            # 本启动器记录的实例进程仍存活 → 视为我们的实例（即使当前环境拿不到监听 PID）
            $startedByUs = $true
            if (-not $procId) { $procId = $statePid }
        }
        if (-not $startedByUs) {
            if ($procId) {
                $cmdline = Get-ProcessCommandLine $procId
                if ($cmdline) {
                    $identity = $cmdline
                    $foreign = -not (Test-DshCommandLine $cmdline)
                } else {
                    $identity = '(无法读取命令行)'
                    $foreign = $true
                }
            } else {
                $identity = '(无法获取 PID)'
                $foreign = $true
            }
        }
    }
    return [pscustomobject]@{
        port        = $Port
        running     = $running
        httpOk      = $httpOk
        pid         = $procId
        url         = "http://127.0.0.1:$Port"
        foreign     = $foreign
        startedByUs = $startedByUs
        identity    = $identity
    }
}

# ---------- 启动 / 停止 ----------

function Start-DshProcess {
    # 幂等启动：已在运行（且非外来进程）→ 直接成功。
    # 返回错误码：0 成功/已在运行；EX_PORT_BUSY 端口被外来进程占用；EX_NOT_FOUND 未找到 node/dsh。
    param([int]$Port = 0)
    $Port = Get-DshPort $Port
    $s = Get-DshStatus $Port
    if ($s.running) {
        if ($s.foreign) {
            Write-DshLog "端口 $Port 被非 DSH 进程(pid=$($s.pid))占用，拒绝启动" 'warn'
            return $script:EX_PORT_BUSY
        }
        Write-DshLog "已在运行 (pid=$($s.pid), port=$Port)，跳过启动" 'info'
        return $script:EX_OK
    }
    $node = Resolve-Node
    $bin  = Resolve-DshBin
    if (-not $node -or -not $bin) {
        Write-DshLog "未找到 node($node) 或 dsh 入口($bin)" 'error'
        return $script:EX_NOT_FOUND
    }
    New-Item -ItemType Directory -Force -Path $script:LOG_DIR, $script:STATE_DIR | Out-Null
    $stdout = Join-Path $script:LOG_DIR 'dsh.out.log'
    $stderr = Join-Path $script:LOG_DIR 'dsh.err.log'
    Roll-DshLog $stdout
    Roll-DshLog $stderr
    $proc = Start-Process -FilePath $node `
        -ArgumentList @($bin, 'web', '--port', "$Port") `
        -WindowStyle Hidden `
        -RedirectStandardOutput $stdout `
        -RedirectStandardError $stderr `
        -PassThru
    $state = Read-State
    $state.started = ConvertTo-StartedHashtable $state.started
    $state.started."$Port" = @{
        pid       = $proc.Id
        startedAt = (Get-Date).ToString('o')
        node      = $node
        bin       = $bin
    }
    Write-State $state
    Write-DshLog "已启动 dsh web (pid=$($proc.Id), port=$Port, node=$node)" 'info'
    return $script:EX_OK
}

function Wait-DshReady {
    # 轮询等待 HTTP 200；默认最长 30 秒。返回 $true/$false。
    param([int]$Port = 0, [int]$TimeoutSec = 30)
    $Port = Get-DshPort $Port
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (Test-DshHttp $Port) { return $true }
        Start-Sleep -Milliseconds 500
    }
    return (Test-DshHttp $Port)
}

function Stop-Dsh {
    # 安全停止：本启动器启动的实例（state 匹配）或命令行白名单匹配的实例可停；
    # 外来/无法确认身份的实例一律拒绝（除非 -Force）。
    param([int]$Port = 0, [switch]$Force)
    $Port = Get-DshPort $Port
    $s = Get-DshStatus $Port
    if (-not $s.running) {
        Write-DshLog "端口 $Port 无监听进程，无需停止" 'info'
        return $script:EX_OK
    }
    if ($s.foreign -and -not $Force) {
        Write-DshLog "拒绝停止：端口 $Port 的进程(pid=$($s.pid))身份无法确认为 DSH (identity=$($s.identity))；确需停止请用 -Force" 'warn'
        return $script:EX_REFUSED
    }
    $target = $s.pid
    if (-not $target) {
        # 环境拿不到监听 PID 时，用 state 中记录的 PID 兜底（startedByUs 已确认过存活）
        $st = Read-State
        try {
            if ($st.started -and $st.started."$Port") { $target = [int]$st.started."$Port".pid }
        } catch { }
    }
    if (-not $target) {
        Write-DshLog "停止失败：无法确定端口 $Port 对应的进程 PID" 'error'
        return $script:EX_STOP_FAILED
    }
    try {
        Stop-Process -Id $target -Force -ErrorAction Stop
    } catch {
        Write-DshLog "停止失败 pid=$target : $($_.Exception.Message)" 'error'
        return $script:EX_STOP_FAILED
    }
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline) {
        if (-not (Get-PortPid $Port)) { break }
        Start-Sleep -Milliseconds 300
    }
    if (Get-PortPid $Port) {
        Write-DshLog "停止后端口 $Port 仍被占用 (pid=$(Get-PortPid $Port))" 'warn'
        return $script:EX_STOP_FAILED
    }
    Remove-StateEntry $Port
    Write-DshLog "已停止 pid=$target (port=$Port)" 'info'
    return $script:EX_OK
}
