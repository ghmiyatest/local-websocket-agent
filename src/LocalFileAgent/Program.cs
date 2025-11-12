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
    class Program
    {
        static string BaseDir = @"C:\Adseven";
        static int Port = 8787;
        static HashSet<string> AllowedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "http://localhost" };
        static string ApiToken = ""; // 必須なら設定
        static HashSet<string> AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".004", ".txt" };
        static HashSet<string> AllowedExecs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\AdSeven\Audatex\Auda7\Bin\AudaMenu.exe"
        };
        static Encoding ShiftJis = Encoding.GetEncoding("Shift_JIS");

        static void Main(string[] args)
        {
            ParseArgs(args);

            var prefix = $"http://127.0.0.1:{Port}/ws/";
            var listener = new HttpListener();
            listener.Prefixes.Add(prefix);

            try
            {
                listener.Start();
                Console.WriteLine($"[LocalFileAgent] Listening {prefix}");
                Console.WriteLine($"BaseDir={BaseDir}");
                Console.WriteLine($"AllowedOrigins={string.Join(",", AllowedOrigins)}");
                Console.WriteLine($"Token={(string.IsNullOrEmpty(ApiToken) ? "(none)" : "***")}");
            }
            catch (HttpListenerException hlex)
            {
                Console.Error.WriteLine("HttpListener start failed: " + hlex.Message);
                Console.Error.WriteLine("管理者権限/URLACL/ポート占有を確認してください。scripts/setup-urlacl.ps1 を使用可。");
                return;
            }

            while (true)
            {
                var ctx = listener.GetContext(); // 同期待受（疑似同期フロー）
                if (!ctx.Request.IsWebSocketRequest)
                {
                    ctx.Response.StatusCode = 400;
                    var msg = Encoding.UTF8.GetBytes("WebSocket only");
                    ctx.Response.OutputStream.Write(msg, 0, msg.Length);
                    ctx.Response.Close();
                    continue;
                }

                // Origin チェック
                var origin = ctx.Request.Headers["Origin"];
                if (origin != null && AllowedOrigins.Count > 0 && !AllowedOrigins.Contains(origin))
                {
                    ctx.Response.StatusCode = 403;
                    ctx.Response.Close();
                    continue;
                }

                // トークン：クエリ or Sec-WebSocket-Protocol
                string token = ctx.Request.QueryString["token"];
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

                HandleWebSocket(ctx);
            }
        }

        static async void HandleWebSocket(HttpListenerContext ctx)
        {
            HttpListenerWebSocketContext wsCtx = null;
            try
            {
                wsCtx = await ctx.AcceptWebSocketAsync(subProtocol: null);
            }
            catch (Exception ex)
            {
                ctx.Response.StatusCode = 500;
                ctx.Response.Close();
                Console.Error.WriteLine("[Handshake Error] " + ex);
                return;
            }

            var ws = wsCtx.WebSocket;
            var buffer = new ArraySegment<byte>(new byte[64 * 1024]);

            try
            {
                while (ws.State == WebSocketState.Open)
                {
                    var recv = await ws.ReceiveAsync(buffer, CancellationToken.None);
                    if (recv.MessageType == WebSocketMessageType.Close)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                        break;
                    }

                    string text = Encoding.UTF8.GetString(buffer.Array, 0, recv.Count);
                    var resp = ProcessCommand(text);
                    var respBytes = Encoding.UTF8.GetBytes(resp);
                    await ws.SendAsync(new ArraySegment<byte>(respBytes), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[WS Error] " + ex);
                try { await ws.CloseAsync(WebSocketCloseStatus.InternalServerError, "error", CancellationToken.None); } catch { }
            }
            finally
            {
                ws.Dispose();
            }
        }

        static string ProcessCommand(string json)
        {
            // 入力例: {"action":"delete|create|read|exec","path":"*.004 or file","wildcard":true|false,"content":"...","encoding":"sjis|utf8"}
            try
            {
                var cmd = SimpleJson.Parse(json);

                string action  = cmd.GetString("action");
                string path    = cmd.GetString("path");
                bool wildcard  = cmd.GetBool("wildcard");
                string content = cmd.GetString("content");
                string encoding= cmd.GetString("encoding");
                Encoding enc   = (string.Equals(encoding, "sjis", StringComparison.OrdinalIgnoreCase) ? ShiftJis : Encoding.UTF8);

                if (string.IsNullOrEmpty(action)) return SimpleJson.Ok(false, "missing action");
                if (string.IsNullOrEmpty(path))   return SimpleJson.Ok(false, "missing path");

                switch (action)
                {
                    case "delete":
                        {
                            int count = 0;
                            if (wildcard)
                            {
                                string pattern = Path.GetFileName(path) ?? "*";
                                string ext = Path.GetExtension(pattern);
                                if (!string.IsNullOrEmpty(ext) && !AllowedExtensions.Contains(ext))
                                    return SimpleJson.Ok(false, $"disallowed extension: {ext}");

                                foreach (var f in Directory.EnumerateFiles(BaseDir, pattern, SearchOption.TopDirectoryOnly))
                                { File.Delete(f); count++; }
                            }
                            else
                            {
                                string full = ResolveRestrictedPath(path);
                                var ext = Path.GetExtension(full);
                                if (!AllowedExtensions.Contains(ext)) return SimpleJson.Ok(false, $"disallowed extension: {ext}");
                                if (File.Exists(full)) { File.Delete(full); count = 1; }
                            }
                            return SimpleJson.Ok(true, $"deleted {count} file(s)");
                        }
                    case "create":
                        {
                            string full = ResolveRestrictedPath(path);
                            var ext = Path.GetExtension(full);
                            if (!AllowedExtensions.Contains(ext)) return SimpleJson.Ok(false, $"disallowed extension: {ext}");
                            Directory.CreateDirectory(Path.GetDirectoryName(full));
                            File.WriteAllText(full, content ?? "", enc);
                            return SimpleJson.Ok(true, $"created {full}");
                        }
                    case "read":
                        {
                            string full = ResolveRestrictedPath(path);
                            var ext = Path.GetExtension(full);
                            if (!AllowedExtensions.Contains(ext)) return SimpleJson.Ok(false, $"disallowed extension: {ext}");
                            if (!File.Exists(full)) return SimpleJson.Ok(false, "file not found");
                            var txt = File.ReadAllText(full, enc);
                            return SimpleJson.Ok(true, "read ok", txt);
                        }
                    case "exec":
                        {
                            // EXE はフルパスで AllowedExecs に一致させる
                            string full = Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(BaseDir, path));
                            if (!AllowedExecs.Contains(full)) return SimpleJson.Ok(false, "exe not allowed");
                            var p = Process.Start(new ProcessStartInfo { FileName = full, UseShellExecute = false });
                            return SimpleJson.Ok(true, $"started pid={p.Id}");
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

        static string ResolveRestrictedPath(string relativeOrFull)
        {
            // BaseDir 以下にサンドボックス
            string full = Path.IsPathRooted(relativeOrFull)
                ? Path.GetFullPath(relativeOrFull)
                : Path.GetFullPath(Path.Combine(BaseDir, relativeOrFull));

            string baseFull = Path.GetFullPath(BaseDir).TrimEnd('\\') + "\\";
            if (!full.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("outside baseDir");
            return full;
        }

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

        // 依存を増やさない最小 JSON ユーティリティ（.NET 4.5）
        class SimpleJson
        {
            readonly Dictionary<string, string> _kv;
            SimpleJson(Dictionary<string, string> kv) { _kv = kv; }

            public static SimpleJson Parse(string json)
            {
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

            public string GetString(string key) => _kv.ContainsKey(key) ? _kv[key] : null;
            public bool GetBool(string key)
            {
                var v = GetString(key);
                if (string.IsNullOrEmpty(v)) return false;
                return v.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            public static string Ok(bool ok, string message, string data = null)
            {
                var sb = new StringBuilder();
                sb.Append("{\"ok\":").Append(ok ? "true" : "false").Append(",\"message\":\"").Append(Escape(message)).Append("\"");
                if (data != null) sb.Append(",\"data\":\"").Append(Escape(data)).Append("\"");
                sb.Append("}");
                return sb.ToString();
            }
            static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }
    }
}