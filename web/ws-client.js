// ws-client.js - ブラウザ側クライアント
// 既存フローの擬似同期：削除→作成→EXE起動→読み取り
// Shift-JIS 書込/読取に対応（encoding:"sjis"）

(function(){
  const WS_URL = "ws://127.0.0.1:8787/ws?token=YOUR_SECRET"; // LocalFileAgent の --token と一致
  const USE_SJIS = true;

  let ws;

  function log(msg){ console.log("[WSCLIENT]", msg); }

  function connect(onOpen){
    ws = new WebSocket(WS_URL);
    ws.onopen = () => { log("open"); if (onOpen) onOpen(); };
    ws.onclose = () => log("close");
    ws.onerror = (e) => console.error("[WSCLIENT] error", e);
    ws.onmessage = (ev) => {
      log("message: " + ev.data);
      // 必要なら JSON.parse(ev.data) して UI に反映
    };
  }

  // ActiveX 互換操作
  function deleteFilesWildcard(ext){
    const msg = { action:"delete", path:"*"+ext, wildcard:true };
    ws.send(JSON.stringify(msg));
  }
  function deleteFile(relPath){
    const msg = { action:"delete", path: relPath, wildcard:false };
    ws.send(JSON.stringify(msg));
  }
  function createFile(relPath, content){
    const msg = { action:"create", path: relPath, content: content, encoding: USE_SJIS ? "sjis" : "utf8" };
    ws.send(JSON.stringify(msg));
  }
  function readFile(relPath){
    const msg = { action:"read", path: relPath, encoding: USE_SJIS ? "sjis" : "utf8" };
    ws.send(JSON.stringify(msg));
  }
  function startAudaMenu(){
    const msg = { action:"exec", path: "C:\\\\AdSeven\\\\Audatex\\\\Auda7\\\\Bin\\\\AudaMenu.exe" };
    ws.send(JSON.stringify(msg));
  }

  // 例：ページ表示時に接続＆削除 *.004
  window.LocalWsClient = {
    connect, deleteFilesWildcard, deleteFile, createFile, readFile, startAudaMenu
  };
})();