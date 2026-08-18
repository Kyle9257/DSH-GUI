# dashboard.ps1 — DeepSeek Harness 配套 UI（WinForms 中文仪表盘）
# 由桌面快捷方式调用（powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File dashboard.ps1）
# 功能：状态灯 + 启动 / 打开界面 / 停止 / 查看日志 / 退出；2 秒自动刷新；打开时未运行则自动启动。

[CmdletBinding()]
param([int]$Port = 0)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib.ps1')
$Port = Get-DshPort $Port

# --- DPI 感知（失败不影响运行） ---
try {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class DshDpiHelper {
    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();
}
'@ -ErrorAction Stop
    [DshDpiHelper]::SetProcessDPIAware() | Out-Null
} catch { }

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# --- 状态变量（事件处理器内统一用 $script: 前缀） ---
$script:LastState = $null
$script:Starting = $false
$script:StartWaitSec = 0

# --- 颜色 ---
$ColorBg    = [System.Drawing.Color]::FromArgb(31, 31, 36)
$ColorPanel = [System.Drawing.Color]::FromArgb(42, 42, 48)
$ColorText  = [System.Drawing.Color]::FromArgb(232, 232, 234)
$ColorDim   = [System.Drawing.Color]::FromArgb(160, 160, 168)
$ColorBtn   = [System.Drawing.Color]::FromArgb(51, 51, 58)
$ColorBorder = [System.Drawing.Color]::FromArgb(69, 69, 75)
$ColorAccent = [System.Drawing.Color]::FromArgb(86, 156, 255)

function Get-DotColor {
    param($s)
    if (-not $s.running) { return [System.Drawing.Color]::Gray }
    if ($s.foreign) { return [System.Drawing.Color]::OrangeRed }
    if (-not $s.httpOk) { return [System.Drawing.Color]::Gold }
    return [System.Drawing.Color]::LimeGreen
}

function Get-StateText {
    param($s)
    if (-not $s.running) {
        if ($script:Starting) { return "启动中…（$script:StartWaitSec 秒）" }
        return '未运行'
    }
    if ($s.foreign) { return "端口被占用（非 DSH 进程，PID $($s.pid)）" }
    if (-not $s.httpOk) { return '运行中（服务尚未就绪）' }
    return "运行中 · $($s.url)"
}

function Update-Status {
    try {
        $s = Get-DshStatus $Port
        $script:LastState = $s
        if ($script:Starting -and $s.running) {
            $script:Starting = $false
            $script:StartWaitSec = 0
        }
        $dot.ForeColor = Get-DotColor $s
        $statusLabel.Text = Get-StateText $s
        if ($s.running) {
            $detailLabel.Text = "PID $($s.pid) · 端口 $($s.port) · 本启动器启动: $(if ($s.startedByUs) {'是'} else {'否'})"
        } else {
            $detailLabel.Text = "端口 $($s.port) · 等待启动"
        }
        $btnStart.Enabled = (-not $s.running) -and (-not $script:Starting)
        $btnStop.Enabled  = $s.running
        $btnOpen.Enabled  = $s.running -and $s.httpOk
    } catch {
        $statusLabel.Text = "状态刷新失败: $($_.Exception.Message)"
    }
}

# --- 窗体 ---
$form = New-Object System.Windows.Forms.Form
$form.Text = 'DeepSeek Harness 启动器'
$form.Size = New-Object System.Drawing.Size(520, 360)
$form.MinimumSize = New-Object System.Drawing.Size(480, 320)
$form.StartPosition = 'CenterScreen'
$form.BackColor = $ColorBg
$form.ForeColor = $ColorText
$form.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 9)
$form.AutoScaleMode = 'Dpi'

$layout = New-Object System.Windows.Forms.TableLayoutPanel
$layout.Dock = 'Fill'
$layout.Padding = New-Object System.Windows.Forms.Padding(16)
$layout.ColumnCount = 1
$layout.RowCount = 7
$layout.BackColor = $ColorBg
$layout.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle('Percent', 100))) | Out-Null
$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle('Absolute', 44)))  | Out-Null  # 标题
$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle('Absolute', 36)))  | Out-Null  # 状态行
$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle('Absolute', 26)))  | Out-Null  # 详情行
$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle('Absolute', 52)))  | Out-Null  # 主按钮
$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle('Absolute', 44)))  | Out-Null  # 次按钮
$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle('Percent', 100)))  | Out-Null  # 留白
$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle('Absolute', 30)))  | Out-Null  # 底部提示
$form.Controls.Add($layout)

# 标题
$title = New-Object System.Windows.Forms.Label
$title.Text = 'DeepSeek Harness'
$title.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 18, [System.Drawing.FontStyle]::Bold)
$title.ForeColor = $ColorAccent
$title.Dock = 'Fill'
$title.TextAlign = 'MiddleLeft'
$layout.Controls.Add($title, 0, 0)

# 状态行：圆点 + 文本
$statusPanel = New-Object System.Windows.Forms.FlowLayoutPanel
$statusPanel.Dock = 'Fill'
$statusPanel.BackColor = $ColorBg
$statusPanel.FlowDirection = 'LeftToRight'
$statusPanel.AutoSize = $false
$dot = New-Object System.Windows.Forms.Label
$dot.Text = '●'
$dot.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 16)
$dot.ForeColor = [System.Drawing.Color]::Gray
$dot.AutoSize = $true
$dot.Padding = New-Object System.Windows.Forms.Padding(0, 2, 8, 0)
$statusLabel = New-Object System.Windows.Forms.Label
$statusLabel.Text = '检测中…'
$statusLabel.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 11)
$statusLabel.ForeColor = $ColorText
$statusLabel.AutoSize = $true
$statusLabel.Padding = New-Object System.Windows.Forms.Padding(0, 5, 0, 0)
$statusPanel.Controls.Add($dot)
$statusPanel.Controls.Add($statusLabel)
$layout.Controls.Add($statusPanel, 0, 1)

# 详情行
$detailLabel = New-Object System.Windows.Forms.Label
$detailLabel.Text = ''
$detailLabel.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 8.5)
$detailLabel.ForeColor = $ColorDim
$detailLabel.Dock = 'Fill'
$detailLabel.TextAlign = 'MiddleLeft'
$layout.Controls.Add($detailLabel, 0, 2)

# 主按钮行
$btnPanel = New-Object System.Windows.Forms.FlowLayoutPanel
$btnPanel.Dock = 'Fill'
$btnPanel.BackColor = $ColorBg
$btnPanel.FlowDirection = 'LeftToRight'

$btnStart = New-Object System.Windows.Forms.Button
$btnStart.Text = '启动 DSH'
$btnStart.Size = New-Object System.Drawing.Size(120, 38)
$btnStart.BackColor = $ColorAccent
$btnStart.ForeColor = [System.Drawing.Color]::White
$btnStart.FlatStyle = 'Flat'
$btnStart.FlatAppearance.BorderSize = 0

$btnOpen = New-Object System.Windows.Forms.Button
$btnOpen.Text = '打开界面'
$btnOpen.Size = New-Object System.Drawing.Size(120, 38)
$btnOpen.BackColor = $ColorBtn
$btnOpen.ForeColor = $ColorText
$btnOpen.FlatStyle = 'Flat'
$btnOpen.FlatAppearance.BorderColor = $ColorBorder

$btnStop = New-Object System.Windows.Forms.Button
$btnStop.Text = '停止 DSH'
$btnStop.Size = New-Object System.Drawing.Size(120, 38)
$btnStop.BackColor = $ColorBtn
$btnStop.ForeColor = $ColorText
$btnStop.FlatStyle = 'Flat'
$btnStop.FlatAppearance.BorderColor = $ColorBorder

$btnPanel.Controls.Add($btnStart)
$btnPanel.Controls.Add($btnOpen)
$btnPanel.Controls.Add($btnStop)
$layout.Controls.Add($btnPanel, 0, 3)

# 次按钮行
$subPanel = New-Object System.Windows.Forms.FlowLayoutPanel
$subPanel.Dock = 'Fill'
$subPanel.BackColor = $ColorBg
$subPanel.FlowDirection = 'LeftToRight'

$btnLogs = New-Object System.Windows.Forms.Button
$btnLogs.Text = '查看日志'
$btnLogs.Size = New-Object System.Drawing.Size(96, 30)
$btnLogs.BackColor = $ColorBtn
$btnLogs.ForeColor = $ColorText
$btnLogs.FlatStyle = 'Flat'
$btnLogs.FlatAppearance.BorderColor = $ColorBorder

$btnExit = New-Object System.Windows.Forms.Button
$btnExit.Text = '退出'
$btnExit.Size = New-Object System.Drawing.Size(96, 30)
$btnExit.BackColor = $ColorBtn
$btnExit.ForeColor = $ColorText
$btnExit.FlatStyle = 'Flat'
$btnExit.FlatAppearance.BorderColor = $ColorBorder

$subPanel.Controls.Add($btnLogs)
$subPanel.Controls.Add($btnExit)
$layout.Controls.Add($subPanel, 0, 4)

# 底部提示
$hint = New-Object System.Windows.Forms.Label
$hint.Text = '双击快捷方式即自动启动 DSH；关闭本窗口不会停止 DSH（点「停止」才会）。'
$hint.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 8)
$hint.ForeColor = $ColorDim
$hint.Dock = 'Fill'
$hint.TextAlign = 'MiddleLeft'
$layout.Controls.Add($hint, 0, 6)

# --- 事件 ---
$btnStart.add_Click({
    $script:Starting = $true
    $script:StartWaitSec = 0
    $rc = Start-DshProcess $Port
    if ($rc -ne $script:EX_OK) {
        $s = Get-DshStatus $Port
        if ($s.foreign) {
            $statusLabel.Text = '无法启动：端口被非 DSH 进程占用'
        } else {
            $statusLabel.Text = "启动失败（代码 $rc），详见 logs\dsh.log"
        }
        $script:Starting = $false
    }
    Update-Status
})

$btnOpen.add_Click({
    $s = $script:LastState
    if ($s -and $s.running) {
        Start-Process $s.url
    } else {
        $statusLabel.Text = 'DSH 未运行，请先点「启动 DSH」'
    }
})

$btnStop.add_Click({
    $s = $script:LastState
    if ($s -and $s.foreign) {
        $r = [System.Windows.Forms.MessageBox]::Show(
            "端口 $($s.port) 被非 DSH 进程（PID $($s.pid)）占用。`n确认强制停止该进程？",
            '确认停止',
            [System.Windows.Forms.MessageBoxButtons]::YesNo,
            [System.Windows.Forms.MessageBoxIcon]::Warning)
        if ($r -ne [System.Windows.Forms.DialogResult]::Yes) { return }
        $rc = Stop-Dsh $Port -Force
    } else {
        $rc = Stop-Dsh $Port
    }
    if ($rc -eq $script:EX_REFUSED) {
        $statusLabel.Text = '已拒绝停止：无法确认进程身份（需要 -Force）'
    } elseif ($rc -ne $script:EX_OK) {
        $statusLabel.Text = "停止失败（代码 $rc），详见 logs\dsh.log"
    }
    Update-Status
})

$btnLogs.add_Click({
    try {
        New-Item -ItemType Directory -Force -Path $script:LOG_DIR | Out-Null
        Start-Process (Join-Path $script:LOG_DIR 'dsh.log')
    } catch {
        $statusLabel.Text = "打开日志失败: $($_.Exception.Message)"
    }
})

$btnExit.add_Click({ $form.Close() })

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 2000
$timer.add_Tick({
    if ($script:Starting) {
        $script:StartWaitSec++
        if ($script:StartWaitSec -ge 45) {
            $script:Starting = $false
            $statusLabel.Text = '启动超时（45 秒），请查看 logs\dsh.err.log'
        }
    }
    Update-Status
})

$form.add_Shown({
    Update-Status
    $s = $script:LastState
    if ($s -and -not $s.running -and -not $s.foreign) {
        $script:Starting = $true
        $script:StartWaitSec = 0
        Start-DshProcess $Port | Out-Null
        Update-Status
    }
    $timer.Start()
})

$form.add_FormClosed({ $timer.Stop() })

[System.Windows.Forms.Application]::Run($form)
