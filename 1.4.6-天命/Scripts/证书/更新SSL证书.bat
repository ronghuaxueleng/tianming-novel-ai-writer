@echo off
chcp 65001 >nul
set /p DOMAIN="请输入你的服务器域名（例如 api.example.com）: "
powershell -ExecutionPolicy Bypass -File "%~dp0update-ssl-pin.ps1" -Domain "%DOMAIN%"
pause
