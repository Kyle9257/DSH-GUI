# ci.ps1 — 一键自查总入口（每次修改后必跑）
#   1) dotnet build 零错误
#   2) verify-exe.ps1 回归（隔离端口 3180 真实启动/停止，需 %DSH_HOME% 可写）
#   3) --stats 冒烟（JSON 合法 + 关键字段存在）
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File ci.ps1 [-SkipBuild]
# 退出码：0 全部通过；1 存在 FAIL

[CmdletBinding()]
param([switch]$SkipBuild)

$ErrorActionPreference = 'Continue'
$root = $PSScriptRoot
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

Write-Output '===== 1/3 构建 ====='
if ($SkipBuild) {
    Add-Check 'dotnet build' $true '已跳过（-SkipBuild）'
} else {
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $buildOut = dotnet build (Join-Path $root 'DshDesktop.csproj') -c Release 2>&1
    $buildCode = $LASTEXITCODE
    $errs = @($buildOut | Select-String -Pattern 'error CS|error MSB')
    Add-Check 'dotnet build 零错误' ($buildCode -eq 0 -and $errs.Count -eq 0) ("exit=" + $buildCode + " errors=" + $errs.Count)
}

Write-Output '===== 2/3 exe 回归（隔离端口 3180） ====='
powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'verify-exe.ps1')
Add-Check 'verify-exe 回归' ($LASTEXITCODE -eq 0) ("exit=" + $LASTEXITCODE)

Write-Output '===== 3/3 --stats 冒烟 ====='
$exe = Join-Path $root 'dist\DeepSeek Harness.exe'
if (Test-Path -LiteralPath $exe) {
    $outFile = Join-Path $env:TEMP 'dsh-ci-stats.json'
    $errFile = $outFile + '.err'
    $p = Start-Process -FilePath $exe -ArgumentList '--stats' -Wait -PassThru -RedirectStandardOutput $outFile -RedirectStandardError $errFile
    Add-Check '--stats 退出码 0' ($p.ExitCode -eq 0) ("exit=" + $p.ExitCode)
    $obj = $null
    try { $obj = Get-Content $outFile -Raw | ConvertFrom-Json } catch { }
    Add-Check '--stats 输出合法 JSON' ($null -ne $obj)
    if ($obj) {
        Add-Check 'stats 含 model/balance 字段' ($null -ne $obj.model -and $null -ne $obj.balance)
        Add-Check 'stats 上下文窗口 > 0' ([int64]$obj.model.contextWindow -gt 0) ("window=" + $obj.model.contextWindow)
        Add-Check 'stats token 统计 >= 0' ([int64]$obj.model.totalTokens -ge 0) ("total=" + $obj.model.totalTokens)
    }
    Remove-Item $outFile, $errFile -ErrorAction SilentlyContinue
} else {
    Add-Check '--stats 冒烟（dist exe 不存在）' $false $exe
}

Write-Output ''
Write-Output ("===== 结果: " + ($script:Checks - $script:Fails) + "/" + $script:Checks + " 通过 =====")
if ($script:Fails -gt 0) {
    Write-Output ("存在 " + $script:Fails + " 项 FAIL，请检查后再提交")
    exit 1
}
exit 0
