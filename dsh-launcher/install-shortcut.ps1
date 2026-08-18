# install-shortcut.ps1 — 创建/更新桌面快捷方式（幂等，可重复执行）
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File install-shortcut.ps1
# 产物：桌面「DeepSeek Harness.lnk」→ powershell.exe -STA -WindowStyle Hidden -File dashboard.ps1

[CmdletBinding()]
param()

. (Join-Path $PSScriptRoot 'lib.ps1')

$desktop = [Environment]::GetFolderPath('Desktop')
$lnkPath = Join-Path $desktop 'DeepSeek Harness.lnk'
$psExe = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
$dashPath = Join-Path $script:LAUNCHER_ROOT 'dashboard.ps1'

if (-not (Test-Path -LiteralPath $dashPath)) {
    Write-Error "找不到 dashboard.ps1: $dashPath"
    exit 1
}

$node = Resolve-Node
$icon = if ($node -and (Test-Path -LiteralPath $node)) { "$node,0" } else { "$psExe,0" }

$ws = New-Object -ComObject WScript.Shell
$sc = $ws.CreateShortcut($lnkPath)
$sc.TargetPath = $psExe
$sc.Arguments = '-NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File "' + $dashPath + '"'
$sc.WorkingDirectory = $script:LAUNCHER_ROOT
$sc.IconLocation = $icon
$sc.Description = 'DeepSeek Harness 启动器：启动 / 打开 / 停止 DSH'
$sc.Save()

Write-Output "已创建/更新快捷方式: $lnkPath"
Write-Output "目标: $psExe $($sc.Arguments)"
exit 0
