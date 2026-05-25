# 天命 - 调试模式启动
# 先用 dotnet run 或 Scripts/编译.bat 构建，再运行此脚本启动 --debug 模式。
# 原加密打包流程（.NET Reactor + VMProtect）已在开源版中移除。

chcp 65001 > $null
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$Host.UI.RawUI.ForegroundColor = "Green"

try {
    $Host.UI.RawUI.BufferSize = New-Object Management.Automation.Host.Size(120, 3000)
    $Host.UI.RawUI.WindowSize = New-Object Management.Automation.Host.Size(120, 60)
} catch {
    Write-Host "[警告] 无法设置窗口大小: $($_.Exception.Message)" -ForegroundColor Yellow
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent (Split-Path -Parent $scriptDir)
$exePath = Join-Path $projectRoot "Core\App\bin\Debug\net8.0-windows10.0.19041.0\win-x64\天命.exe"

Write-Host "================================================================" -ForegroundColor Green
Write-Host "            天命 - 调试模式启动                                 " -ForegroundColor Green
Write-Host "================================================================" -ForegroundColor Green
Write-Host ""

if (-not (Test-Path $exePath)) {
    Write-Host "[提示] 未找到已构建的可执行文件，尝试使用 dotnet run 启动..." -ForegroundColor Yellow
    $csproj = Join-Path $projectRoot "Core\App\天命.csproj"
    Write-Host "[启动] dotnet run --project $csproj -- --debug" -ForegroundColor Green
    & dotnet run --project $csproj -- --debug
    exit $LASTEXITCODE
}

Write-Host "[启动] 天命.exe --debug" -ForegroundColor Green
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
