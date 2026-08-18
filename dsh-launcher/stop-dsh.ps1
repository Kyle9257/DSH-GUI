# stop-dsh.ps1 — 停止 DSH Web GUI（安全停止：外来/无法确认身份的进程默认拒绝）
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File stop-dsh.ps1 [-Port 3080] [-Force]
# 退出码：0 已停止/未运行；5 拒绝停止（身份无法确认，需 -Force）；6 停止失败

[CmdletBinding()]
param(
    [int]$Port = 0,
    [switch]$Force
)

. (Join-Path $PSScriptRoot 'lib.ps1')
$Port = Get-DshPort $Port

$rc = Stop-Dsh $Port -Force:$Force
exit $rc
