@echo off
setlocal

REM 切换到项目根目录（相对当前 Scripts 目录）
cd /d "%~dp0.."

REM 编译 Chat 主项目
dotnet build "Core\App\天命.csproj"

endlocal
pause
