# Local WebSocket Agent (Classic IIS 環境での ActiveX 代替)

現行 ActiveX の機能（削除/作成/読み取り/EXE 起動）をローカル WebSocket エージェントで代替します。  
サーバは端末ローカルで `ws://127.0.0.1:8787/ws` を待受、ブラウザ JS から JSON コマンドを送ります。

## 前提
- Windows / .NET Framework 4.5
- IIS は Classic のまま（本エージェントは IIS 非依存）
- 既存のローカルフォルダ例：`C:\Adseven`（サンドボックスのベースディレクトリ）

## 安全対策
- BaseDir 以下のみアクセス可（パストラバーサル拒否）
- 拡張子ホワイトリスト：`.004`/`.txt` など
- EXE ホワイトリスト：`C:\AdSeven\Audatex\Auda7\Bin\AudaMenu.exe` など
- Origin 制限（社内サイトのみ）
- トークン認証（クエリまたは Sec-WebSocket-Protocol）

## ビルド & 起動
```powershell
# URLACL の登録（管理者で）
.\scripts\setup-urlacl.ps1 -Port 8787

# ビルド & 起動
.\scripts\run-agent.ps1 -BaseDir "C:\Adseven" -Port 8787 -Origins "http://intranet.example.local" -Token "YOUR_SECRET"
