# verify-exe.ps1 — 桌面 exe 一键回归验证（用 netstat/HTTP/进程/state 权威基准，避开环境敏感的 Get-NetTCPConnection）
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File verify-exe.ps1 [-Port 3180] [-ExePath <路径>]
# 退出码：0 全部通过；1 存在 FAIL

[CmdletBinding()]
param(
    [int]$Port = 3180,
    [string]$ExePath = ''
)

$ErrorActionPreference = 'Continue'

if (-not $ExePath) { $ExePath = Join-Path $PSScriptRoot 'dist\DeepSeek Harness.exe' }
if (-not (Test-Path -LiteralPath $ExePath)) {
    Write-Output "[FAIL] 找不到 exe: $ExePath"
    exit 1
}
$env:DSH_LAUNCHER_PORT = "$Port"

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

function Test-Listening([int]$p) {
    return [bool](netstat -ano | Select-String (":$p\s.*LISTENING"))
}

function Get-ListenerPid([int]$p) {
    $m = netstat -ano | Select-String (":$p\s.*LISTENING") | Select-Object -First 1
    if (-not $m) { return $null }
    return ($m.ToString().Trim() -split '\s+')[-1]
}

function Invoke-Cli([string]$cliArgs) {
    # Start-Process(无 -Wait) + 看门狗轮询：避免 -Wait/cmd 管道与 GUI 进程派生交互导致的工具链挂起
    $argList = @($cliArgs -split ' ')
    $p = Start-Process -FilePath $ExePath -ArgumentList $argList -PassThru
    $deadline = (Get-Date).AddSeconds(90)
    while ((Get-Date) -lt $deadline -and -not $p.HasExited) { Start-Sleep -Milliseconds 300 }
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue; return -1 }
    return $p.ExitCode
}

Write-Output "===== exe 回归（端口 $Port） ====="
Add-Check '初始端口空闲' (-not (Test-Listening $Port))
$rc = Invoke-Cli '--start --wait'
Add-Check '--start --wait rc=0（真实就绪）' ($rc -eq 0) ("rc=" + $rc)
$lp = Get-ListenerPid $Port
Add-Check "端口 $Port 有监听" ($null -ne $lp) ("pid=" + $lp)
if ($lp) {
    $cl = Get-CimInstance Win32_Process -Filter "ProcessId=$lp" -ErrorAction SilentlyContinue
    Add-Check '监听进程命令行匹配 DSH 白名单' ($cl -and $cl.CommandLine -match 'web' -and $cl.CommandLine -match 'bin\.js') ($cl.CommandLine)
}
try {
    $r = Invoke-WebRequest -Uri "http://127.0.0.1:$Port" -UseBasicParsing -TimeoutSec 5
    Add-Check 'HTTP 200' ($r.StatusCode -eq 200) ("HTTP " + $r.StatusCode)
} catch {
    Add-Check 'HTTP 200' $false $_.Exception.Message
}
$rc2 = Invoke-Cli '--start'
Add-Check '幂等重复启动 rc=0' ($rc2 -eq 0) ("rc=" + $rc2)
$rc3 = Invoke-Cli '--stop'
Add-Check '--stop rc=0' ($rc3 -eq 0) ("rc=" + $rc3)
Start-Sleep -Milliseconds 1500
Add-Check '停止后端口释放' (-not (Test-Listening $Port))
$st = Get-Content 'C:\Users\18943\AppData\Local\DshDesktop\state\launcher-state.json' -Raw -ErrorAction SilentlyContinue
Add-Check 'state 无残留条目' ($st -and $st -notmatch '"' + $Port + '"') ($st.Trim())

Write-Output ''
Write-Output ("===== 结果: " + ($script:Checks - $script:Fails) + "/" + $script:Checks + " 通过 =====")
Remove-Item Env:DSH_LAUNCHER_PORT -ErrorAction SilentlyContinue
if ($script:Fails -gt 0) { exit 1 }
exit 0
