# ============================================
# 天命 - SSL Pin 自动更新脚本
# 用法：.\update-ssl-pin.ps1 -Domain api.example.com
# ============================================

param(
    [Parameter(Mandatory=$true)]
    [string]$Domain   # 你的服务器域名，例如: api.example.com
)

$ErrorActionPreference = "Stop"

$TargetFile = Join-Path $PSScriptRoot "..\..\Framework\Common\Services\SslPinningHandler.cs"
$Host_ = $Domain
$Port = 443

# ---------- 1. 连接服务器获取叶证书 ----------
Write-Host "[1/3] 连接 $Host_`:$Port 获取证书..." -ForegroundColor Cyan

$certBytes = $null
try {
    $tcp = [System.Net.Sockets.TcpClient]::new()
    $tcp.Connect($Host_, $Port)
    $ssl = [System.Net.Security.SslStream]::new($tcp.GetStream(), $false, { $true })
    $ssl.AuthenticateAsClient($Host_)
    $cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($ssl.RemoteCertificate)
    $certBytes = $cert.RawData
    $subject = $cert.Subject
    $expiry = $cert.NotAfter
    $ssl.Close()
    $tcp.Close()
} catch {
    Write-Host "连接失败: $_" -ForegroundColor Red
    exit 1
}

Write-Host "  Subject: $subject"
Write-Host "  Expiry:  $expiry"

# ---------- 2. 用 dotnet 计算 SPKI Pin ----------
Write-Host "[2/3] 计算 SPKI SHA256 Pin..." -ForegroundColor Cyan

$tempDir = Join-Path $env:TEMP "TM_SslPin_$(Get-Random)"
$null = New-Item -ItemType Directory -Path $tempDir -Force

# 导出证书到临时文件
$certPath = Join-Path $tempDir "leaf.cer"
[System.IO.File]::WriteAllBytes($certPath, $certBytes)

# 创建临时 .NET 项目
$projContent = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>
'@

$codeContent = @"
using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

var bytes = File.ReadAllBytes(args[0]);
var cert = new X509Certificate2(bytes);
var spki = cert.PublicKey.ExportSubjectPublicKeyInfo();
var hash = SHA256.HashData(spki);
Console.Write(Convert.ToBase64String(hash));
"@

$projPath = Join-Path $tempDir "Pin.csproj"
$codePath = Join-Path $tempDir "Program.cs"
Set-Content -Path $projPath -Value $projContent -Encoding UTF8
Set-Content -Path $codePath -Value $codeContent -Encoding UTF8

$pin = & dotnet run --project $projPath -- $certPath 2>$null
Remove-Item -Recurse -Force $tempDir

if ([string]::IsNullOrWhiteSpace($pin)) {
    Write-Host "Pin 计算失败" -ForegroundColor Red
    exit 1
}

$expiryStr = $expiry.ToString("yyyy/M/d")
Write-Host "  Pin: $pin" -ForegroundColor Green

# ---------- 3. 写入 C# 代码文件 ----------
Write-Host "[3/3] 更新 C# SslPinningHandler..." -ForegroundColor Cyan

$content = Get-Content $TargetFile -Raw -Encoding UTF8

# 匹配 _fallbackPins 数组中的叶证书行（带注释"叶证书"的行）
$leafPattern = '(?m)^(\s*)"[A-Za-z0-9+/=]+".*//\s*叶证书.*$'
$newLeafLine = "`$1`"$pin`", // 叶证书 ($expiryStr 到期)"

if ($content -match $leafPattern) {
    $content = $content -replace $leafPattern, $newLeafLine
    Write-Host "  已替换叶证书 Pin" -ForegroundColor Green
} else {
    Write-Host "  未找到叶证书行，请确认代码中存在 '// 叶证书' 注释标记" -ForegroundColor Red
    Write-Host "  Pin: $pin"
    exit 1
}

Set-Content -Path $TargetFile -Value $content -NoNewline -Encoding UTF8

Write-Host ""
Write-Host "完成! 新 Pin: $pin (到期: $expiryStr)" -ForegroundColor Green
Write-Host "  SslPinningHandler.cs 已更新" -ForegroundColor Gray
