# 天命 - 调试模式启动脚本
# PowerShell版本，更稳定可靠

# 设置控制台编码和颜色
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$Host.UI.RawUI.ForegroundColor = "Green"

# 设置窗口大小（考虑屏幕限制）
try {
    # 先设置缓冲区，再设置窗口大小
    $Host.UI.RawUI.BufferSize = New-Object Management.Automation.Host.Size(120, 3000)
    $Host.UI.RawUI.WindowSize = New-Object Management.Automation.Host.Size(120, 60)
} catch {
    # 如果设置失败，使用默认大小
    Write-Host "[警告] 无法设置窗口大小: $($_.Exception.Message)" -ForegroundColor Yellow
}

Write-Host "================================================================" -ForegroundColor Green
Write-Host "                    天命 - 调试模式                              " -ForegroundColor Green
Write-Host "================================================================" -ForegroundColor Green
Write-Host ""
Write-Host "[启动] 正在启动应用..." -ForegroundColor Yellow
Write-Host ""

# 切换到exe所在目录
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent (Split-Path -Parent $scriptPath)
$exePath = Join-Path $projectRoot "Core\App\bin\Debug\net8.0-windows10.0.19041.0\天命.exe"

if (-not (Test-Path $exePath)) {
    Write-Host "[错误] 未找到可执行文件" -ForegroundColor Red
    Write-Host "[提示] 请先编译项目" -ForegroundColor Yellow
    Write-Host "[路径] $exePath" -ForegroundColor Gray
    Write-Host ""
    Read-Host "按Enter键退出"
    exit 1
}

Write-Host "[信息] 找到程序: $exePath" -ForegroundColor Cyan
Write-Host ""

# 启动应用并捕获输出
try {
    & $exePath --debug 2>&1 | ForEach-Object { Write-Host $_ }
    $exitCode = $LASTEXITCODE
} catch {
    Write-Host "[异常] 启动失败: $($_.Exception.Message)" -ForegroundColor Red
    $exitCode = -1
}

Write-Host ""
Write-Host "================================================================" -ForegroundColor Green
Write-Host "[退出] 应用已退出 (退出代码: $exitCode)" -ForegroundColor $(if($exitCode -eq 0){"Green"}else{"Red"})
Write-Host "================================================================" -ForegroundColor Green
Write-Host ""
Write-Host "窗口将保持打开，可随时查看日志。输入 exit 退出。" -ForegroundColor Cyan

