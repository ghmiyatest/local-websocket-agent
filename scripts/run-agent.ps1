param(
  [string]$BaseDir = "C:\Adseven",
  [int]$Port = 8787,
  [string]$Origins = "http://intranet.example.local",
  [string]$Token = "YOUR_SECRET"
)

$exe = Join-Path $PSScriptRoot "..\src\LocalFileAgent\bin\Release\LocalFileAgent.exe"
if (!(Test-Path $exe)) {
  Write-Host "Building LocalFileAgent..."
  & "${env:WINDIR}\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe" (Join-Path $PSScriptRoot "..\src\LocalFileAgent\LocalFileAgent.csproj") /p:Configuration=Release
}
Write-Host "Starting LocalFileAgent..."
