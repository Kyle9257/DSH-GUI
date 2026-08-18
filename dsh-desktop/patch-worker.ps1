# patch-worker.ps1 — 修复 DSH 目录选择器 worker 的 IPC 断开 bug（防 npm 重装后回归）
# 背景：@deepseek-ai/dsh-host-directory-picker-native 的 worker.cjs 在每次发送消息
# （含 "showing" 通知）后都 process.disconnect()，导致用户取消文件夹对话框后
# 的 "done" 消息无法送达 → 前端 armed 状态卡死 → 「添加工作区」再次点击无反应。
# 修复：仅终态消息（done/error）发送后才允许断开通道。幂等，可重复执行。
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File patch-worker.ps1 [-WorkerPath <路径>]

[CmdletBinding()]
param([string]$WorkerPath = '')

$ErrorActionPreference = 'Stop'

if (-not $WorkerPath) {
    $local = Join-Path $env:LOCALAPPDATA 'npm-cache\_npx'
    if (Test-Path $local) {
        $found = Get-ChildItem -Path $local -Recurse -Filter 'worker.cjs' -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match 'dsh-host-directory-picker-native\\lib\\worker\.cjs$' } |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($found) { $WorkerPath = $found.FullName }
    }
}
if (-not $WorkerPath -or -not (Test-Path -LiteralPath $WorkerPath)) {
    Write-Output '[FAIL] 找不到 worker.cjs（请用 -WorkerPath 显式指定）'
    exit 1
}

$content = [System.IO.File]::ReadAllText($WorkerPath, [System.Text.Encoding]::UTF8)
$old = 'if (process.connected) process.disconnect();'
$new = 'if ((message.kind === "done" || message.kind === "error") && process.connected) process.disconnect();'

if ($content.Contains('message.kind === "done"')) {
    Write-Output ("[PASS] 已修复，无需处理: " + $WorkerPath)
    exit 0
}
if (-not $content.Contains($old)) {
    Write-Output "[FAIL] 未找到待修复代码（文件内容与预期不符，可能版本已变化）: $WorkerPath"
    exit 2
}
$content = $content.Replace($old, $new)
[System.IO.File]::WriteAllText($WorkerPath, $content, [System.Text.UTF8Encoding]::new($false))
Write-Output ("[PASS] 已修复: " + $WorkerPath)
exit 0
