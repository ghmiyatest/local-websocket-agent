// LocalFileAgent - .NET Framework 4.5 Console (self-host WebSocket via HttpListener)
// 現行 ActiveX の代替：ローカルフォルダの削除/作成/読み取り/EXE 起動を WebSocket 経由で提供
// 互換：gKillLinkFile / SetLinkFileData / GetLinkFileData / ExecuteProc に対応
// 安全：BaseDir サンドボックス, 拡張子/EXE ホワイトリスト, Origin 制限, 簡易トークン

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Diagnostics;
namespace LocalFileAgent
{
    // .NET Framework 4.5 では HttpListener が WebSocket をサポート
    // HttpListener は管理者権限が必要な場合があるので注意
    // URLACL は scripts/setup-urlacl.ps1 を使用可
    // WebSocket は同期待受で疑似同期フローにしている（C# 5 制約）
    // JSON は最小限の自前パーサーを使用（依存を増やさないため）
    // コマンド例: {"action":"delete|create|read|exec","path":"*.004 or file","wildcard":true|false,"content":"...","encoding":"sjis|utf8"}
    // レスポンス例: {"ok":true|false,"message":"...","data":"..."}
    // 注意：実運用ではセキュリティに十分注意してください
    // 注意：.NET Framework 4.5 はサポート終了しているため、可能なら .NET 6+ 等の使用を検討してください
    // 注意：本コードはサンプルで提供されており、商用利用や重要なシステムでの使用に際しては十分なテストとセキュリティ評価を行ってください
    //       作者は本コードの使用に起因するいかなる損害についても責任を負いません   
    class Program
    {
        /// <summary>
        /// ベースディレクトリ（サンドボックス）
        /// </summary>
        static string BaseDir = @"C:\AgentTest";

        /// <summary>
        /// ポート番号
        /// </summary>
        static int Port = 8787;

        /// <summary>
        /// 許可する Origin（空ならチェックなし）
        /// </summary>
        static HashSet<string> AllowedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "http://localhost" };

        /// <summary>
        /// API トークン（必要に応じて設定）
        /// </summary>
        static string ApiToken = ""; 

        /// <summary>
        /// 制限リスト
        /// 注意：必要に応じて適切に変更してください
        /// 例：.004, .txt のみ許可        //
        /// </summary>
        static HashSet<string> AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".004", ".txt" };
    
        /// <summary>
        /// 例：特定 EXE のみ許可
        /// 注意：フルパスで指定
        /// 例：calc.exe のみ許可
        /// 注意：必要に応じて適切に変更してください
        /// 注意：不適切な EXE を許可するとセキュリティリスクとなる可能性があります
        /// </summary>
        static HashSet<string> AllowedExecs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Windows\System32\calc.exe"
        };

        /// <summary>
        /// Shift_JIS エンコーディング
        /// 注意：.NET Framework 4.5 では標準でサポートされている
        /// 注意：.NET Core/.NET 5+ では System.Text.Encoding.CodePages パッケージが必要
        /// </summary>
        static Encoding ShiftJis = Encoding.GetEncoding("Shift_JIS");

        /// <summary>
        /// エントリポイント
        /// </summary>
        /// <param name="args"></param>
        static void Main(string[] args)
        {
            // コマンドライン引数解析
            ParseArgs(args);

            // HttpListener 初期化
            // 注意：管理者権限が必要な場合がある
            // 注意：URLACL は scripts/setup-urlacl.ps1 を使用可
            // WebSocket エンドポイントは /ws/
            // 例: http://127.0.0.1:8787/ws/
            // 注意：実運用では HTTPS の使用を検討してください
            // 注意：HttpListener は高負荷には向かないため、必要に応じて他のサーバーソリューションを検討してください            
            var prefix = "http://127.0.0.1:" + Port + "/ws/";
            var listener = new HttpListener();
            // WebSocket 用エンドポイント
            // 注意：HttpListener はワイルドカードをサポートしないため、正確なパスを指定する必要がある
            // 注意：実運用では適切なパスを設定してください
            listener.Prefixes.Add(prefix);

            try
            {
                // リスナー開始
                listener.Start();
                // 起動情報表示
                // 注意：実運用ではログ機能を検討してください
                // 注意：API トークンは表示しない
                // 注意：実運用では詳細な情報を表示しないことを検討してください
                Console.WriteLine("[LocalFileAgent] Listening " + prefix);
                Console.WriteLine("BaseDir=" + BaseDir);
                Console.WriteLine("AllowedOrigins=" + string.Join(",", AllowedOrigins));
                Console.WriteLine("Token=" + (string.IsNullOrEmpty(ApiToken) ? "(none)" : "***"));
            }
            catch (HttpListenerException hlex)
            {
                Console.Error.WriteLine("HttpListener start failed: " + hlex.Message);
                Console.Error.WriteLine("管理者権限/URLACL/ポート占有を確認してください。scripts/setup-urlacl.ps1 を使用可。");
                return;
            }

            while (true)
            {
                // リクエスト受信待機
                // 注意：同期的に動作する（C# 5 制約）
                var ctx = listener.GetContext();
                // WebSocket チェック
                // 注意：HttpListener は WebSocket 以外のリクエストも受け付けるため、明示的にチェックする必要がある
                // 注意：実運用ではログ機能を検討してください
                // 注意：WebSocket 以外のリクエストは拒否することを推奨します
                // 注意：必要に応じて他のリクエストを処理するロジックを追加してください
                // 注意：セキュリティ上の理由から、不要なリクエストは拒否することを推奨します
                if (!ctx.Request.IsWebSocketRequest)
                {
                    // 非 WebSocket リクエスト拒否
                    // 注意：実運用では詳細な情報を返さないことを検討してください
                    ctx.Response.StatusCode = 400;
                    var msg = Encoding.UTF8.GetBytes("WebSocket only");
                    ctx.Response.OutputStream.Write(msg, 0, msg.Length);
                    ctx.Response.Close();
                    continue;
                }

                // Origin チェック
                // 注意：実運用では詳細な情報を返さないことを検討してください
                var origin = ctx.Request.Headers["Origin"];
                 if (origin != null && AllowedOrigins.Count > 0 && !AllowedOrigins.Contains(origin))
                {
                    // Origin 不許可
                    ctx.Response.StatusCode = 403;
                    ctx.Response.Close();
                    continue;
                }

                // トークンチェック
                string token = ctx.Request.QueryString["token"];
                // 注意：Sec-WebSocket-Protocol ヘッダーにもトークンを指定可能
                if (string.IsNullOrEmpty(token))
                {
                    var proto = ctx.Request.Headers["Sec-WebSocket-Protocol"];
                    if (!string.IsNullOrEmpty(proto)) token = proto.Trim();
                }
                if (!string.IsNullOrEmpty(ApiToken) && !string.Equals(ApiToken, token, StringComparison.Ordinal))
                {
                    ctx.Response.StatusCode = 401;
                    ctx.Response.Close();
                    continue;
                }
                // WebSocket ハンドリング
                HandleWebSocket(ctx);
            }
        }

        /// <summary>
        /// WebSocket ハンドリング
        /// </summary>
        /// <param name="ctx"></param>
        static async void HandleWebSocket(HttpListenerContext ctx)
        {
            // WebSocket ハンドシェイク
            HttpListenerWebSocketContext wsCtx = null;
            try
            {
                // 注意：subProtocol は使用しない
                // 注意：実運用では必要に応じてサブプロトコルを使用してください
                // 注意：セキュリティ上の理由から、不要なサブプロトコルは拒否することを推奨します
                wsCtx = await ctx.AcceptWebSocketAsync(subProtocol: null);
            }
            catch (Exception ex)
            {
                ctx.Response.StatusCode = 500;
                ctx.Response.Close();
                Console.Error.WriteLine("[Handshake Error] " + ex);
                return;
            }

            // WebSocket 通信ループ
            // 注意：非同期メソッドを使用（C# 5 制約により async void）
            var ws = wsCtx.WebSocket;
            // 64KB バッファ
            // 注意：必要に応じてバッファサイズを調整してください
            var buffer = new ArraySegment<byte>(new byte[64 * 1024]);

            // クローズステータス初期値
            WebSocketCloseStatus closeStatus = WebSocketCloseStatus.NormalClosure;
            // クローズ記述初期値
            string closeDesc = "bye";

            // 通信ループ
            try
            {
                // 注意：同期的に動作する（C# 5 制約）
                while (ws.State == WebSocketState.Open)
                {
                    // メッセージ受信待機
                    var recv = await ws.ReceiveAsync(buffer, CancellationToken.None);
                    // クローズ要求チェック
                    // 注意：クローズ要求を受け取った場合、ループを抜けてクローズ処理に進む
                    if (recv.MessageType == WebSocketMessageType.Close)
                    {
                        // クローズ要求受信
                        closeStatus = WebSocketCloseStatus.NormalClosure;
                        closeDesc = "bye";
                        break;
                    }

                    // テキストメッセージ処理
                    // 注意：バイナリメッセージはサポートしない
                    string text = Encoding.UTF8.GetString(buffer.Array, 0, recv.Count);
                    // コマンド処理
                    var resp = ProcessCommand(text);
                    // レスポンス送信
                    var respBytes = Encoding.UTF8.GetBytes(resp);
                    await ws.SendAsync(new ArraySegment<byte>(respBytes), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[WS Error] " + ex);
                closeStatus = WebSocketCloseStatus.InternalServerError;
                closeDesc = "error";
            }
            finally
            {
                try
                {
                    // クローズ処理
                    if (ws.State == WebSocketState.Open)
                    {
                        // クローズ送信                        
                        ws.CloseAsync(closeStatus, closeDesc, CancellationToken.None).Wait();
                    }
                }
                catch { }
                ws.Dispose();
            }
        }

        /// <summary>
        /// コマンド処理
        /// </summary>
        /// <param name="json"></param>
        /// <returns></returns>
        static string ProcessCommand(string json)
        {
            // 入力例: {"action":"delete|create|read|exec","path":"*.004 or file","wildcard":true|false,"content":"...","encoding":"sjis|utf8"}
            try
            {
                // JSON パース
                // 注意：簡易パーサーを使用（依存を増やさないため）
                // 注意：実運用では堅牢な JSON ライブラリの使用を検討してください
                var cmd = SimpleJson.Parse(json);

                // パラメーター取得
                string action  = cmd.GetString("action");
                string path    = cmd.GetString("path");
                bool wildcard  = cmd.GetBool("wildcard");
                string content = cmd.GetString("content");
                string encoding= cmd.GetString("encoding");
                // エンコーディング選択
                Encoding enc   = (string.Equals(encoding, "sjis", StringComparison.OrdinalIgnoreCase) ? ShiftJis : Encoding.UTF8);

                // パラメーター検証
                if (string.IsNullOrEmpty(action)) return SimpleJson.Ok(false, "missing action");
                if (string.IsNullOrEmpty(path))   return SimpleJson.Ok(false, "missing path");

                // アクション処理
                switch (action)
                {
                    // ファイル削除
                    case "delete":
                        {
                            int count = 0;
                            if (wildcard)
                            {
                                // ワイルドカード削除
                                string pattern = Path.GetFileName(path) ?? "*";
                                string ext = Path.GetExtension(pattern);

                                if (!string.IsNullOrEmpty(ext) && !AllowedExtensions.Contains(ext))
                                    return SimpleJson.Ok(false, "disallowed extension: " + ext);

                                // ベースディレクトリ内のファイルを削除
                                foreach (var f in Directory.EnumerateFiles(BaseDir, pattern, SearchOption.TopDirectoryOnly))
                                { File.Delete(f); count++; }
                            }
                            else
                            {
                                // 単一ファイル削除
                                string full = ResolveRestrictedPath(path);
                                var ext = Path.GetExtension(full);
                                if (!AllowedExtensions.Contains(ext)) return SimpleJson.Ok(false, "disallowed extension: " + ext);
                                
                                // ファイル削除
                                if (File.Exists(full)) { File.Delete(full); count = 1; }
                            }
                            // 結果返却
                            return SimpleJson.Ok(true, "deleted " + count + " file(s)");
                        }
                        // ファイル作成/上書き
                    case "create":
                        {
                            // ファイル作成
                            string full = ResolveRestrictedPath(path);
                            var ext = Path.GetExtension(full);
                            if (!AllowedExtensions.Contains(ext)) return SimpleJson.Ok(false, "disallowed extension: " + ext);
                            // ディレクトリ作成
                            Directory.CreateDirectory(Path.GetDirectoryName(full));
                            // ファイル書き込み
                            File.WriteAllText(full, content ?? "", enc);
                            // 結果返却
                            return SimpleJson.Ok(true, "created " + full);
                        }
                        // ファイル読み取り
                    case "read":
                        {
                            // ファイル読み取り
                            string full = ResolveRestrictedPath(path);
                            var ext = Path.GetExtension(full);
                            if (!AllowedExtensions.Contains(ext)) return SimpleJson.Ok(false, "disallowed extension: " + ext);
                            // ファイル存在チェック
                            if (!File.Exists(full)) return SimpleJson.Ok(false, "file not found");
                            // ファイル読み取り
                            var txt = File.ReadAllText(full, enc);
                            // 結果返却
                            return SimpleJson.Ok(true, "read ok", txt);
                        }
                        // EXE 起動
                    case "exec":
                        {
                            // EXE 起動
                            string full = Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(BaseDir, path));
                            // EXE 許可チェック
                            // 注意：不適切な EXE を許可するとセキュリティリスクとなる可能性があります
                            if (!AllowedExecs.Contains(full)) return SimpleJson.Ok(false, "exe not allowed");
                            // プロセス起動
                            var p = Process.Start(new ProcessStartInfo { FileName = full, UseShellExecute = false });
                            // 結果返却
                            return SimpleJson.Ok(true, "started pid=" + p.Id);
                        }
                    default:
                        return SimpleJson.Ok(false, "unknown action");
                }
            }
            catch (Exception ex)
            {
                return SimpleJson.Ok(false, "error: " + ex.Message);
            }
        }

        /// <summary>
        /// パス解決（BaseDir サンドボックス）
        /// </summary>
        /// <param name="relativeOrFull"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        static string ResolveRestrictedPath(string relativeOrFull)
        {
            // フルパス解決
            // 注意：BaseDir 外を指す場合は例外をスロー
            // 注意：セキュリティ上の理由から、BaseDir 外のアクセスを防止することを推奨します
            string full = Path.IsPathRooted(relativeOrFull)
                ? Path.GetFullPath(relativeOrFull)
                : Path.GetFullPath(Path.Combine(BaseDir, relativeOrFull));

            // BaseDir チェック
            string baseFull = Path.GetFullPath(BaseDir).TrimEnd('\\') + "\\";
            if (!full.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("outside baseDir");
            return full;
        }

        /// <summary>
        /// コマンドライン引数解析
        /// </summary>
        /// <param name="args"></param>
        static void ParseArgs(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--baseDir": BaseDir = args[++i]; break;
                    case "--port":    Port    = int.Parse(args[++i]); break;
                    case "--origins": AllowedOrigins = new HashSet<string>(args[++i].Split(',').Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase); break;
                    case "--token":   ApiToken = args[++i]; break;
                }
            }
        }

        /// <summary>
        /// 簡易 JSON パーサー/ジェネレーター（.NET 4.5）
        /// </summary>
        class SimpleJson
        {
            /// <summary>
            /// キー/値ペア
            /// </summary>
            readonly Dictionary<string, string> _kv;

            /// <summary>
            /// コンストラクター
            /// </summary>
            /// <param name="kv"></param>
            SimpleJson(Dictionary<string, string> kv) { _kv = kv; }

            /// <summary>
            /// JSON パース
            /// </summary>
            /// <param name="json"></param>
            /// <returns></returns>
            /// <exception cref="FormatException"></exception>
            public static SimpleJson Parse(string json)
            {
                // 注意：簡易的な JSON パーサー
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                int i = 0;
                Func<char?> Peek = () => (i < json.Length ? (char?)json[i] : null);
                Action<char> Expect = (ch) => { if (Peek() != ch) throw new FormatException("json"); i++; };
                Action SkipWs = () => { while (i < json.Length && char.IsWhiteSpace(json[i])) i++; };

                SkipWs(); Expect('{'); SkipWs();
                while (Peek() != '}')
                {
                    SkipWs(); Expect('"');
                    int s = i; while (Peek() != '"') i++;
                    string key = json.Substring(s, i - s); Expect('"'); SkipWs(); Expect(':'); SkipWs();
                    string val;
                    if (Peek() == '"')
                    {
                        i++; int t = i; while (Peek() != '"') i++;
                        val = json.Substring(t, i - t); Expect('"');
                    }
                    else
                    {
                        int t = i; while (Peek() != null && ",}".IndexOf(Peek().Value) < 0) i++;
                        val = json.Substring(t, i - t).Trim();
                    }
                    dict[key] = val;
                    SkipWs();
                    if (Peek() == ',') { i++; SkipWs(); } else break;
                }
                Expect('}');
                return new SimpleJson(dict);
            }

            /// <summary>
            /// キーから文字列取得
            /// </summary>
            /// <param name="key"></param>
            /// <returns></returns>
            public string GetString(string key) { return _kv.ContainsKey(key) ? _kv[key] : null; }

            /// <summary>
            /// キーから真偽値取得
            /// </summary>
            /// <param name="key"></param>
            /// <returns></returns>
            public bool GetBool(string key)
            {
                var v = GetString(key);
                if (string.IsNullOrEmpty(v)) return false;
                return v.Equals("true", StringComparison.OrdinalIgnoreCase);
            }

            /// <summary>
            /// レスポンス生成
            /// </summary>
            /// <param name="ok"></param>
            /// <param name="message"></param>
            /// <param name="data"></param>
            /// <returns></returns>

            public static string Ok(bool ok, string message, string data = null)
            {
                var sb = new StringBuilder();
                sb.Append("{\"ok\":").Append(ok ? "true" : "false").Append(",\"message\":\"").Append(Escape(message)).Append("\"");
                if (data != null) sb.Append(",\"data\":\"").Append(Escape(data)).Append("\"");
                sb.Append("}");
                return sb.ToString();
            }
            /// <summary>
            /// 文字列エスケープ
            /// </summary>
            /// <param name="s"></param>
            /// <returns></returns>

            static string Escape(string s) { return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n"); }
        }
    }
}
