param(
  [string]$RepoName = "local-websocket-agent",
  [string]$GitUser  = "",
  [string]$GitEmail = "",
  [string]$Remote   = "origin",
  [string]$UseGhCli = "true"  # gh CLI を使うなら true
)

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$projRoot = Split-Path -Parent $root

Set-Location $projRoot

if ($GitUser -and $GitEmail) {
  git config user.name  $GitUser
  git config user.email $GitEmail
}

if (!(Test-Path ".git")) { git init }

git add .
git commit -m "Initial commit: Local WebSocket Agent (.NET 4.5, Classic IIS compatible)"

if ($UseGhCli -eq "true") {
  # GitHub CLI（gh）がインストール済み前提
  # 公開/プライベートは --public/--private の切替で
  gh repo create $RepoName --private --source . --remote $Remote --push
} else {
  Write-Host "Remote を手動設定してください:"
  Write-Host "  git remote add origin https://github.com/<your-account>/$RepoName.git"
  Write-Host "  git branch -M main"
  Write-Host "  git push -u origin main"
}