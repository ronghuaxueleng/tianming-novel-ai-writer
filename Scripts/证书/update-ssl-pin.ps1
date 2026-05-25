# ============================================
# 天命 - SSL Pin 自动更新脚本
# 用法：直接运行即可获取服务器当前叶证书Pin并写入代码
# ============================================

$ErrorActionPreference = "Stop"

$TargetFile = Join-Path $PSScriptRoot "..\..\Framework\Common\Services\SslPinningHandler.cs"
$Host_ = "api.example.com"
$Port = 443

# ---------- 1. 连接服务器获取叶证书 ----------
Write-Host "[1/4] 连接 $Host_`:$Port 获取证书..." -ForegroundColor Cyan

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
Write-Host "[2/4] 计算 SPKI SHA256 Pin..." -ForegroundColor Cyan

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
Write-Host "[3/4] 更新 C# SslPinningHandler..." -ForegroundColor Cyan

$content = Get-Content $TargetFile -Raw -Encoding UTF8

# 匹配 _fallbackPins 数组中的 CF 边缘叶证书行（不动源站 Origin CA 行）
$leafPattern = '(?m)^(\s*)"[A-Za-z0-9+/=]+".*//\s*CF边缘叶证书.*$'
$newLeafLine = "`$1`"$pin`", // CF边缘叶证书 ($expiryStr 到期)"

if ($content -match $leafPattern) {
    $content = $content -replace $leafPattern, $newLeafLine
    Write-Host "  已替换叶证书 Pin" -ForegroundColor Green
} else {
    Write-Host "  未找到叶证书行，请确认代码中存在 '// CF边缘叶证书' 注释标记" -ForegroundColor Red
    Write-Host "  Pin: $pin"
    exit 1
}

Set-Content -Path $TargetFile -Value $content -NoNewline -Encoding UTF8

# ---------- 说明 ----------
# 开源版本中 SslPinningHandler.cs 的 ValidateCertificate 已改为直接 return true，
# 无需 SSL Pin。若你自行部署服务端并需要证书钉扎，请在 SslPinningHandler.cs
# 中恢复验证逻辑，并将此脚本中的域名改为你自己的服务器地址后重新运行。

Write-Host ""
Write-Host "完成! Pin: $pin (到期: $expiryStr)" -ForegroundColor Green
Write-Host "  C#: SslPinningHandler.cs 已更新" -ForegroundColor Gray
Write-Host "  注意: 开源版本默认跳过证书验证，此 Pin 仅供自部署服务端使用" -ForegroundColor Yellow
