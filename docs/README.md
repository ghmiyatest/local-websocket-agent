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
- EXE ホワイトリスト：`C:\Windows\System32\calc.exe` など
- Origin 制限（社内サイトのみ）
- トークン認証（クエリまたは Sec-WebSocket-Protocol）

## ビルド & 起動 
- パラメータは環境に合わせて書き換えてさい。
```powershell
# URLACL の登録（管理者で）
.\setup-urlacl.ps1 -Port 8787

# ビルド
.\run-agent.ps1 -BaseDir "C:\Adseven" -Port 8787 -Origins "http://10.160.28.4" -Token "YOUR_SECRET"

# 起動
C:\AgentTest\LocalFileAgent.exe --baseDir "C:\AgentTest" --port 8787 --origins "http://10.160.28.4" --token "YOUR_SECRET"
```
## 検証
- ローカル端末から Edge で web サーバの test.html にアクセスする。
- F12 でデバッグツールを開き、コンソールに以下をペースト。
```JavaScript
// 接続
const ws = new WebSocket("ws://127.0.0.1:8787/ws?token=YOUR_SECRET");

// --- 送信→次の1メッセージを待って resolve するヘルパー ---
function sendAndWait(obj, timeoutMs = 5000) {
  return new Promise((resolve, reject) => {
    const t = setTimeout(() => reject(new Error("timeout")), timeoutMs);
    const onMsg = ev => {
      clearTimeout(t);
      ws.removeEventListener("message", onMsg);
      resolve(ev.data);              // サーバは JSON文字列を返す
    };
    ws.addEventListener("message", onMsg);
    ws.send(JSON.stringify(obj));
  });
}

// --- 使い方（チェーン） ---
ws.addEventListener("open", () => {
  console.log("open");

  sendAndWait({action:"delete", path:"*.004", wildcard:true})
    .then(resp1 => {
      console.log("delete:", resp1);
      return sendAndWait({action:"create", path:"Jxb.004", content:"テストデータSJIS", encoding:"sjis"});
    })
    .then(resp2 => {
      console.log("create:", resp2);
      // 必要なら少し待ってから read（外部EXE起動などがあるなら適宜調整）
      return new Promise(r => setTimeout(r, 300)).then(() =>
        sendAndWait({action:"read", path:"Jxb.004", encoding:"sjis"})
      );
    })
    .then(resp3 => {
      console.log("read:", resp3);
      // 例：EXE（calc.exe）を AllowedExecs に登録済みなら：
      return sendAndWait({action:"exec", path:"C:\\Windows\\System32\\calc.exe"});
    })
    .then(resp4 => {
      console.log("exec:", resp4);
    })
    .catch(err => {
      console.error("chain error:", err);
      // 必要なら ws.close(); など
    });
});

ws.addEventListener("error", ev => console.error("ws error:", ev));
ws.addEventListener("close", ev => console.log("ws close:", ev.code, ev.reason));
```
- 以下のような結果になれば OK
```JavaScript
open
delete: {"ok":true,"message":"deleted 1 file(s)"}
create: {"ok":true,"message":"created C:\\AgentTest\\Jxb.004"}
read: {"ok":true,"message":"read ok","data":"テストデータSJIS"}
exec: {"ok":true,"message":"started pid=9920"}
```