# status.ps1 — 输出 DSH 运行状态（JSON）
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File status.ps1 [-Port 3080]
# 字段：port / running / httpOk / pid / url / foreign / startedByUs / identity

[CmdletBinding()]
param([int]$Port = 0)

. (Join-Path $PSScriptRoot 'lib.ps1')
$s = Get-DshStatus $Port
$s | ConvertTo-Json -Depth 5
exit $script:EX_OK
