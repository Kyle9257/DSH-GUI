# update-shortcut.ps1 — 构建后自动更新桌面快捷方式（幂等，可重复执行）
# 产物：桌面「DeepSeek Harness.lnk」→ dsh-desktop\dist\DeepSeek Harness.exe（原生桌面端）
# 由 DshDesktop.csproj 的 AfterBuild 目标在 Release 构建完成后自动调用；
# 也可手动执行：powershell -NoProfile -ExecutionPolicy Bypass -File update-shortcut.ps1

$ErrorActionPreference = 'Stop'

$desktop = [Environment]::GetFolderPath('Desktop')
$lnkPath = Join-Path $desktop 'DeepSeek Harness.lnk'
$exePath = Join-Path (Split-Path $PSScriptRoot -Parent) 'dsh-desktop\dist\DeepSeek Harness.exe'

if (-not (Test-Path -LiteralPath $exePath)) {
    Write-Error "找不到 dist exe: $exePath（请先完成构建与热替换）"
    exit 1
}

$ws = New-Object -ComObject WScript.Shell
$sc = $ws.CreateShortcut($lnkPath)
$sc.TargetPath = $exePath
$sc.Arguments = ''
$sc.WorkingDirectory = Split-Path $exePath -Parent
$sc.IconLocation = "$exePath,0"
$sc.Description = 'DeepSeek Harness 桌面端：双击打开原生窗口（内嵌 DSH agent 界面）'
$sc.Save()

Write-Output "已更新桌面快捷方式: $lnkPath"
Write-Output "目标: $exePath"
exit 0
