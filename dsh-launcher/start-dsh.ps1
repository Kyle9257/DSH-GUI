# start-dsh.ps1 — 启动 DSH Web GUI（幂等：已在运行则直接成功）
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File start-dsh.ps1 [-Port 3080] [-NoWait]
# 退出码：0 成功/已在运行；2 端口被外来进程占用；3 等待就绪超时；4 node/dsh 未找到

[CmdletBinding()]
param(
    [int]$Port = 0,
    [switch]$NoWait
)

. (Join-Path $PSScriptRoot 'lib.ps1')
$Port = Get-DshPort $Port

$rc = Start-DshProcess $Port
if ($rc -ne $script:EX_OK) { exit $rc }

if ($NoWait) {
    Write-Output "DSH 启动进程已拉起 (port=$Port)"
    exit $script:EX_OK
}

if (Wait-DshReady $Port 30) {
    $s = Get-DshStatus $Port
    Write-Output ("DSH 已就绪: " + $s.url + " (pid=" + $s.pid + ")")
    exit $script:EX_OK
} else {
    Write-Output "DSH 启动超时(30s)：请查看 logs\dsh.err.log"
    exit $script:EX_START_TIMEOUT
}
