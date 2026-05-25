# 天命 - 加密版调试模式（打包+启动）
param(
    [switch]$SkipBuild,
    [string]$Profile = "full"   # full/minimal/no-necrobit/no-string/no-flow/no-resource/naming-only
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$Host.UI.RawUI.ForegroundColor = "Green"

try {
    $Host.UI.RawUI.BufferSize = New-Object Management.Automation.Host.Size(120, 3000)
    $Host.UI.RawUI.WindowSize = New-Object Management.Automation.Host.Size(120, 60)
} catch {
    Write-Host "[警告] 无法设置窗口大小: $($_.Exception.Message)" -ForegroundColor Yellow
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildScript = Join-Path (Split-Path -Parent $scriptDir) "加密\Protect-Build.ps1"
$exePath = "E:\AI\天命\Publish\Portable\天命.exe"

Write-Host "================================================================" -ForegroundColor Green
Write-Host "            天命 - 加密版调试模式 [$Profile]                    " -ForegroundColor Green
Write-Host "================================================================" -ForegroundColor Green
Write-Host ""

if (-not $SkipBuild) {
    Write-Host "[1/2] 打包加密版 (Profile=$Profile)..." -ForegroundColor Yellow
    $proc = Get-Process -Name "天命" -ErrorAction SilentlyContinue
    if ($proc) { $proc | Stop-Process -Force; Start-Sleep -Seconds 1 }
    echo 1 | powershell -ExecutionPolicy Bypass -File $buildScript -Profile $Profile
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[错误] 打包失败" -ForegroundColor Red
        Read-Host "按Enter键退出"
        exit 1
    }
} else {
    Write-Host "[跳过] 打包（使用已有版本）" -ForegroundColor Gray
}

if (-not (Test-Path $exePath)) {
    Write-Host "[错误] 未找到: $exePath" -ForegroundColor Red
    Read-Host "按Enter键退出"
    exit 1
}

Write-Host ""
Write-Host "[2/2] 启动加密版 (--debug)..." -ForegroundColor Green
Write-Host "─────────────────────────────────────────" -ForegroundColor Green
Write-Host ""

try {
    & $exePath --debug 2>&1 | ForEach-Object { Write-Host $_ }
    $exitCode = $LASTEXITCODE
} catch {
    Write-Host "[异常] $($_.Exception.Message)" -ForegroundColor Red
    $exitCode = -1
}

Write-Host ""
Write-Host "================================================================" -ForegroundColor Green
Write-Host "[退出] 退出代码: $exitCode" -ForegroundColor $(if($exitCode -eq 0){"Green"}else{"Red"})
Write-Host "================================================================" -ForegroundColor Green
Write-Host ""
Write-Host "窗口保持打开，输入 exit 退出。" -ForegroundColor Cyan
