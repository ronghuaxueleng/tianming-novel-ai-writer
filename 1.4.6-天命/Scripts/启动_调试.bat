@echo off
chcp 65001 >nul 2>&1
start powershell -NoProfile -ExecutionPolicy Bypass -NoExit -File "%~dp0调试\启动_调试.ps1"

