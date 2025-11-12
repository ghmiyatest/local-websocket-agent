param(
  [int]$Port = 8787,
  [string]$Url = "http://127.0.0.1:$Port/ws/"
)

Write-Host "Registering URLACL for $Url ..."
# Users グループに付与（必要に応じてローカルユーザー名やサービスユーザーへ変更）
netsh http add urlacl url=$Url user="Users" listen=yes
Write-Host "Done."
