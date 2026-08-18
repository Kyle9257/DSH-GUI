# smoke.ps1 — 自动冒烟测试（分层验收）
#   L1 静态语法    ：解析全部 .ps1，要求 0 语法错误
#   L2 单元双态    ：status 输出合法 JSON；外来进程识别与拒停；快捷方式存在性
#   L3 隔离生命周期：在隔离端口（默认 3180）真实启动 dsh web → 就绪 → 幂等 → 停止 → 无残留
#   （L3 会写入 %DSH_HOME%\profiles，需要 DSH_HOME 可写；受限沙箱可用 -SkipLive 跳过）
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File smoke.ps1 [-TestPort 3180] [-SkipLive]
# 退出码：0 全部通过；1 存在 FAIL

[CmdletBinding()]
param(
    [int]$TestPort = 3180,
    [switch]$SkipLive
)

. (Join-Path $PSScriptRoot 'lib.ps1')

$script:Fails = 0
$script:Checks = 0

function Add-Check {
    param([string]$Name, [bool]$Pass, [string]$Detail = '')
    $script:Checks++
    if (-not $Pass) { $script:Fails++ }
    $mark = if ($Pass) { 'PASS' } else { 'FAIL' }
    $suffix = if ($Detail) { ' - ' + $Detail } else { '' }
    Write-Output ("[{0}] {1}{2}" -f $mark, $Name, $suffix)
}

Write-Output '===== L1 静态语法 ====='
Get-ChildItem -Path $PSScriptRoot -Filter '*.ps1' | Sort-Object Name | ForEach-Object {
    $errs = $null
    $null = [System.Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref]$null, [ref]$errs)
    if ($errs -and $errs.Count -gt 0) {
        Add-Check ("语法: " + $_.Name) $false ($errs[0].Message)
    } else {
        Add-Check ("语法: " + $_.Name) $true
    }
}

Write-Output '===== L2 单元双态 ====='

# 2.1 status.ps1 输出合法 JSON，且当前 3080 上的 GUI 被识别为运行中
$json = & (Join-Path $PSScriptRoot 'status.ps1') 2>$null
$obj = $null
try { $obj = $json | ConvertFrom-Json } catch { }
Add-Check 'status.ps1 输出合法 JSON' ($null -ne $obj) ($json -join ' ')
if ($obj) {
    Add-Check 'status.running=true（3080 有 GUI 监听）' ($obj.running -eq $true) ("pid=" + $obj.pid)
    Add-Check 'status.httpOk=true' ($obj.httpOk -eq $true) ($obj.url)
}

# 2.2 外来进程识别 + 拒停（用进程内 TcpListener 模拟一个“非 DSH 占用者”）
$listener = $null
try {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $foreignPort = ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    $fs = Get-DshStatus $foreignPort
    Add-Check "外来进程识别 (port=$foreignPort)" ($fs.running -and $fs.foreign) ("pid=" + $fs.pid + " identity=" + $fs.identity)
    $rc = Stop-Dsh $foreignPort
    Add-Check '对外来进程拒停 (rc=5)' ($rc -eq $script:EX_REFUSED) ("rc=" + $rc)
    $listener.Stop()
    $listener = $null
} catch {
    Add-Check '外来进程识别（TcpListener 模拟）' $false ($_.Exception.Message)
    if ($listener) { try { $listener.Stop() } catch { } }
}

if (-not $SkipLive) {
    Write-Output '===== L3 隔离端口生命周期 ====='
    $oldEnv = $env:DSH_LAUNCHER_PORT
    $env:DSH_LAUNCHER_PORT = "$TestPort"
    try {
        $rc = Start-DshProcess 0
        Add-Check "启动 (port=$TestPort) rc=0" ($rc -eq 0) ("rc=" + $rc)
        $ready = Wait-DshReady 0 60
        Add-Check "就绪 HTTP 200 (port=$TestPort)" $ready
        $s1 = Get-DshStatus $TestPort
        Add-Check "状态 running+httpOk (port=$TestPort)" ($s1.running -and $s1.httpOk) ("pid=" + $s1.pid)
        # 幂等：重复启动应 rc=0，且端口仍监听中（不会重复拉起）
        $rc2 = Start-DshProcess 0
        Add-Check '幂等：重复启动 rc=0 且仍监听中' ($rc2 -eq 0 -and (Test-PortListening $TestPort)) ("rc2=" + $rc2)
        # 停止
        $rc3 = Stop-Dsh $TestPort
        Add-Check "停止 rc=0 (port=$TestPort)" ($rc3 -eq 0) ("rc=" + $rc3)
        Start-Sleep -Milliseconds 800
        Add-Check "停止后端口释放 (port=$TestPort)" (-not (Get-PortPid $TestPort))
        if ($s1.pid) {
            $alive = Get-Process -Id $s1.pid -ErrorAction SilentlyContinue
            Add-Check '停止后进程退出（无残留）' ($null -eq $alive) ("pid=" + $s1.pid)
        }
        $stale = $false
        try {
            $st = Read-State
            if ($st.started -and $st.started."$TestPort") { $stale = $true }
        } catch { }
        Add-Check 'state 无残留条目' (-not $stale)
    } finally {
        # 兜底清理：若中途异常导致实例残留，尽力停止（仅限本测试端口；身份不符则不动）
        try {
            $sx = Get-DshStatus $TestPort
            if ($sx.running -and (-not $sx.foreign -or $sx.startedByUs)) { Stop-Dsh $TestPort | Out-Null }
        } catch { }
        if ($oldEnv) { $env:DSH_LAUNCHER_PORT = $oldEnv } else { Remove-Item -Path 'Env:DSH_LAUNCHER_PORT' -ErrorAction SilentlyContinue }
    }
} else {
    Write-Output '===== L3 隔离端口生命周期 =====（-SkipLive，已跳过）'
}

Write-Output '===== L4 桌面快捷方式 ====='
$desktop = [Environment]::GetFolderPath('Desktop')
$lnkPath = Join-Path $desktop 'DeepSeek Harness.lnk'
if (Test-Path -LiteralPath $lnkPath) {
    try {
        $ws = New-Object -ComObject WScript.Shell
        $sc = $ws.CreateShortcut($lnkPath)
        $args = [string]$sc.Arguments
        Add-Check '快捷方式存在且指向 dashboard.ps1' ($args -match 'dashboard\.ps1') ($sc.TargetPath + ' ' + $args)
    } catch {
        Add-Check '快捷方式可读' $false ($_.Exception.Message)
    }
} else {
    Add-Check '桌面快捷方式存在' $false ('未找到 ' + $lnkPath + '（请先运行 install-shortcut.ps1）')
}

Write-Output ''
Write-Output ("===== 结果: " + ($script:Checks - $script:Fails) + "/" + $script:Checks + " 通过 =====")
if ($script:Fails -gt 0) {
    Write-Output ("存在 " + $script:Fails + " 项 FAIL")
    exit 1
}
exit 0
