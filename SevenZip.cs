// layerpeel 3.0 —— 解压 · 去套娃助手
// Copyright (C) 2026 layerpeel contributors
//
// 本程序是 ssh-buanshishi / f-keydle（GPL-3.0）的 C# 重写版，自 2026-10-12 起：
// 用 C# 重新实现全部功能，解压后端换成 7-Zip，去掉了 Bandizip / AHK 依赖。
// 按 GNU General Public License v3.0（或更新版本）发布，全文见 LICENSE。
// 本程序不提供任何担保。
// layerpeel 3.0 (C# 版) — 7-Zip 后端封装
// 成败判定只依赖 7z 的退出码与自身校验，不解析任何窗口文本。
// 退出码：0=正常 1=警告 2=致命 7=命令行错 8=内存不足 255=需要密码
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace LayerPeel
{
    public class SevenZip
    {
        public string Exe;
        public int ProbeTimeoutMs;
        public int ExtractTimeoutMs;

        public SevenZip(string exe, int probeSec, int extractSec)
        {
            Exe = exe;
            ProbeTimeoutMs = probeSec * 1000;
            ExtractTimeoutMs = extractSec * 1000;
            if (!File.Exists(Exe)) throw new Exception("找不到 7-Zip 可执行文件：" + Exe);
        }

        // ───────────── 进程启动：参数数组 + 自己加引号（.NET Framework 没有 ArgumentList）─────────────
        public static string Quote(string s)
        {
            if (s == null) return "\"\"";
            if (s.Length > 0 && s.IndexOfAny(new char[] { ' ', '\t', '"', '\n', '\v' }) < 0) return s;
            var sb = new StringBuilder();
            sb.Append('"');
            int backslashes = 0;
            foreach (char c in s)
            {
                if (c == '\\') { backslashes++; continue; }
                if (c == '"') { sb.Append('\\', backslashes * 2 + 1); sb.Append('"'); backslashes = 0; continue; }
                if (backslashes > 0) { sb.Append('\\', backslashes); backslashes = 0; }
                sb.Append(c);
            }
            if (backslashes > 0) sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }

        public class RunResult
        {
            public int Code = -1;
            public string Out = "";
            public string Err = "";
            public bool TimedOut;
        }

        /// <summary>启动外部程序（参数数组，绝不拼命令行），同步等待，带超时</summary>
        public static RunResult Run(string exe, List<string> args, int timeoutMs, Action<string> onOutput)
        {
            var r = new RunResult();
            var psi = new ProcessStartInfo();
            psi.FileName = exe;
            var parts = new List<string>();
            foreach (string a in args) parts.Add(Quote(a));
            psi.Arguments = string.Join(" ", parts.ToArray());
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            var enc = new UTF8Encoding(false);
            psi.StandardOutputEncoding = enc;
            psi.StandardErrorEncoding = enc;

            var sbOut = new StringBuilder();
            var sbErr = new StringBuilder();
            var gate = new object();
            Process p = null;
            try
            {
                p = new Process();
                p.StartInfo = psi;
                p.OutputDataReceived += delegate (object s, DataReceivedEventArgs e)
                {
                    if (e.Data == null) return;
                    lock (gate) { sbOut.AppendLine(e.Data); }
                    if (onOutput != null) { try { onOutput(e.Data); } catch { } }
                };
                p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e)
                {
                    if (e.Data == null) return;
                    lock (gate) { sbErr.AppendLine(e.Data); }
                    if (onOutput != null) { try { onOutput(e.Data); } catch { } }
                };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (timeoutMs > 0)
                {
                    if (!p.WaitForExit(timeoutMs))
                    {
                        r.TimedOut = true;
                        try { p.Kill(); } catch { }
                        try { p.WaitForExit(5000); } catch { }
                    }
                }
                else p.WaitForExit();
                try { r.Code = p.ExitCode; } catch { r.Code = -1; }
            }
            catch (Exception e)
            {
                r.Code = -1;
                r.Err = e.Message;
            }
            finally { try { if (p != null) p.Dispose(); } catch { } }
            lock (gate) { r.Out = sbOut.ToString(); r.Err = sbErr.ToString(); }
            return r;
        }

        // ───────────── 魔数粗筛 ─────────────
        public class Sniff
        {
            public bool Hit;
            public string Kind;
            public int Offset = -1;
            public bool Prefixed;
        }

        private static readonly object[][] Signatures = new object[][]
        {
            new object[] { "zip",    new byte[] { 0x50, 0x4b, 0x03, 0x04 } },
            new object[] { "zip",    new byte[] { 0x50, 0x4b, 0x05, 0x06 } },
            new object[] { "zip",    new byte[] { 0x50, 0x4b, 0x07, 0x08 } },
            new object[] { "7z",     new byte[] { 0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c } },
            new object[] { "rar",    new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 0x00 } },
            new object[] { "rar",    new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 0x01, 0x00 } },
            new object[] { "gzip",   new byte[] { 0x1f, 0x8b, 0x08 } },
            new object[] { "bzip2",  new byte[] { 0x42, 0x5a, 0x68 } },
            new object[] { "xz",     new byte[] { 0xfd, 0x37, 0x7a, 0x58, 0x5a, 0x00 } },
            new object[] { "zstd",   new byte[] { 0x28, 0xb5, 0x2f, 0xfd } },
            new object[] { "lz4",    new byte[] { 0x04, 0x22, 0x4d, 0x18 } },
            new object[] { "wim",    new byte[] { 0x4d, 0x53, 0x57, 0x49, 0x4d, 0x00, 0x00, 0x00 } },
            new object[] { "cab",    new byte[] { 0x4d, 0x53, 0x43, 0x46 } },
            new object[] { "zpaq",   new byte[] { 0x37, 0x6b, 0x53, 0x74 } },
            new object[] { "lzh",    new byte[] { 0x2d, 0x6c, 0x68 } },
            new object[] { "arj",    new byte[] { 0x60, 0xea } },
            new object[] { "compress", new byte[] { 0x1f, 0x9d } },
        };

        private static readonly string[] ContainerProbes = new string[]
        {
            "[Content_Types].xml", "_rels", "docProps", "AndroidManifest.xml", "META-INF", "mimetype"
        };

        public static Sniff SniffFile(string path, int scanLimit)
        {
            var s = new Sniff();
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length < 8) return s;
                int readLen = (int)Math.Min(fi.Length, scanLimit);
                var buf = new byte[readLen];
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    int got = fs.Read(buf, 0, readLen);
                    if (got < readLen) Array.Resize(ref buf, got);
                }
                // PE 一律不算压缩包（2.1 把 exe 当压缩包，导致原程序被删）
                if (buf.Length >= 2 && buf[0] == 0x4d && buf[1] == 0x5a) { s.Kind = "pe"; s.Offset = 0; return s; }

                for (int i = 0; i < Signatures.Length; i++)
                {
                    string kind = (string)Signatures[i][0];
                    byte[] sig = (byte[])Signatures[i][1];
                    if (buf.Length < sig.Length) continue;
                    bool match = true;
                    for (int j = 0; j < sig.Length; j++) if (buf[j] != sig[j]) { match = false; break; }
                    if (!match) continue;
                    if (kind == "zip")
                    {
                        string rej = DetectContainer(buf);
                        if (rej != null) { s.Kind = "container:" + rej; return s; }
                    }
                    s.Hit = true; s.Kind = kind; s.Offset = 0;
                    return s;
                }
                // 有限偏移扫描：只扫最有价值的三种，支持"头部前面塞了伪装字节"的包
                string[] scanKinds = new string[] { "zip", "7z", "rar" };
                for (int i = 0; i < Signatures.Length; i++)
                {
                    string kind = (string)Signatures[i][0];
                    if (Array.IndexOf(scanKinds, kind) < 0) continue;
                    byte[] sig = (byte[])Signatures[i][1];
                    int idx = IndexOf(buf, sig);
                    if (idx <= 0) continue;
                    if (kind == "zip")
                    {
                        var sub = new byte[buf.Length - idx];
                        Array.Copy(buf, idx, sub, 0, sub.Length);
                        if (DetectContainer(sub) != null) continue;
                    }
                    s.Hit = true; s.Kind = kind; s.Offset = idx; s.Prefixed = true;
                    return s;
                }
                return s;
            }
            catch { return s; }
        }

        private static int IndexOf(byte[] haystack, byte[] needle)
        {
            int n = haystack.Length - needle.Length;
            for (int i = 0; i <= n; i++)
            {
                bool ok = true;
                for (int j = 0; j < needle.Length; j++) if (haystack[i + j] != needle[j]) { ok = false; break; }
                if (ok) return i;
            }
            return -1;
        }

        public static string DetectContainer(byte[] buf)
        {
            var enc = Encoding.GetEncoding(28591); // latin1
            int headLen = Math.Min(buf.Length, 65536);
            foreach (string probe in ContainerProbes)
            {
                byte[] pb = enc.GetBytes(probe);
                if (buf.Length >= 0x1e + pb.Length)
                {
                    bool at = true;
                    for (int i = 0; i < pb.Length; i++) if (buf[0x1e + i] != pb[i]) { at = false; break; }
                    if (at) return probe;
                }
                var head = new byte[headLen];
                Array.Copy(buf, 0, head, 0, headLen);
                if (IndexOf(head, pb) >= 0) return probe;
            }
            return null;
        }

        public static bool LooksLikeVolumeTail(string name)
        {
            string lower = name.ToLowerInvariant();
            if (System.Text.RegularExpressions.Regex.IsMatch(lower, @"\.(zip|7z|rar|zipx)\.\d{3}$")) return true;
            if (System.Text.RegularExpressions.Regex.IsMatch(lower, @"\.part\d+\.rar$")) return true;
            if (System.Text.RegularExpressions.Regex.IsMatch(lower, @"\.(r|z)\d{2}$")) return true;
            if (System.Text.RegularExpressions.Regex.IsMatch(lower, @"\.haozip\d+\.(zip|7z)$")) return true;
            return false;
        }

        // ───────────── 7z l / t / x ─────────────
        private static string PwArg(string password)
        {
            // 永远显式传 -p，杜绝 7z 弹交互式密码提示把进程挂死
            return "-p" + (password == null ? "" : password);
        }

        public class Entry
        {
            public Dictionary<string, string> K = new Dictionary<string, string>();
            public string Get(string k) { string v; return K.TryGetValue(k, out v) ? v : null; }
            public bool IsFolder { get { return Get("Folder") == "+"; } }
            public string Path { get { return Get("Path"); } }
            public long Size { get { try { return long.Parse(Get("Size") ?? "0", CultureInfo.InvariantCulture); } catch { return 0; } } }
        }

        public class Listing
        {
            public int Code = -1;
            public string Out = "";
            public string Err = "";
            public bool TimedOut;
            public string Type;
            public Dictionary<string, string> Header = new Dictionary<string, string>();
            public List<Entry> Entries = new List<Entry>();
            public long TotalSize;
            public long PackedSize;
        }

        public Listing List(string file, string password, int timeoutMs)
        {
            var args = new List<string>();
            args.Add("l"); args.Add("-slt"); args.Add("-sccUTF-8"); args.Add("-bd"); args.Add("-y"); args.Add(PwArg(password));
            args.Add("--"); args.Add(file);
            RunResult r = Run(Exe, args, timeoutMs > 0 ? timeoutMs : ProbeTimeoutMs, null);
            var L = ParseSlt(r.Out);
            L.Code = r.Code; L.Out = r.Out; L.Err = r.Err; L.TimedOut = r.TimedOut;
            return L;
        }

        /// <summary>解析 -slt：按空行分块，首个含 Type 的块是档案头（分隔线长度不稳定，不能靠它切）</summary>
        public static Listing ParseSlt(string text)
        {
            var L = new Listing();
            var blocks = new List<Dictionary<string, string>>();
            Dictionary<string, string> cur = null;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Trim() == "") { if (cur != null) { blocks.Add(cur); cur = null; } continue; }
                int i = line.IndexOf(" = ", StringComparison.Ordinal);
                if (i <= 0) continue;
                if (System.Text.RegularExpressions.Regex.IsMatch(line.Trim(), "^-+$")) continue;
                if (cur == null) cur = new Dictionary<string, string>();
                cur[line.Substring(0, i).Trim()] = line.Substring(i + 3);
            }
            if (cur != null) blocks.Add(cur);

            int headerIdx = -1;
            for (int i = 0; i < blocks.Count; i++) if (blocks[i].ContainsKey("Type")) { headerIdx = i; break; }
            if (headerIdx >= 0)
            {
                L.Header = blocks[headerIdx];
                string t; if (L.Header.TryGetValue("Type", out t)) L.Type = t;
                string ps; if (L.Header.TryGetValue("Physical Size", out ps)) long.TryParse(ps, out L.PackedSize);
                for (int i = headerIdx + 1; i < blocks.Count; i++)
                {
                    if (!blocks[i].ContainsKey("Path")) continue;
                    var e = new Entry();
                    e.K = blocks[i];
                    L.Entries.Add(e);
                    L.TotalSize += e.Size;
                }
            }
            return L;
        }

        public class Probe
        {
            public bool IsArchive;
            public string Type;
            public Sniff SniffInfo;
            public bool Encrypted;
            public bool HeaderEncrypted;
            public int EntryCount;
            public long TotalSize;
            public long PackedSize;
            public List<Entry> Entries = new List<Entry>();
            public bool NeedPassword;
            public string Password;
            public string Reason;
            public string Raw;
        }

        private static readonly string[] PwHints = new string[]
        { "wrong password", "can not open encrypted archive", "enter password", "password is not correct", "data error in encrypted file" };

        private static bool HasAny(string haystack, string[] needles)
        {
            string h = haystack.ToLowerInvariant();
            foreach (string n in needles) if (h.IndexOf(n, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        public Probe ProbeFile(string file, List<string> passwords)
        {
            var p = new Probe();
            p.SniffInfo = SniffFile(file, 1024 * 1024);
            if (!p.SniffInfo.Hit) return p;

            Listing r = List(file, null, 0);
            bool looksEncrypted = HasAny(r.Out + r.Err, PwHints) || r.Code == 255;

            if ((r.Code == 0 || r.Code == 1) && r.Type != null)
            {
                p.IsArchive = true;
                p.Type = r.Type;
                foreach (Entry e in r.Entries) if (e.Get("Encrypted") == "+") p.Encrypted = true;
                string hdrEnc; if (r.Header.TryGetValue("Encrypted", out hdrEnc) && hdrEnc == "+") p.Encrypted = true;
                p.EntryCount = r.Entries.Count;
                p.TotalSize = r.TotalSize;
                p.PackedSize = r.PackedSize;
                p.Entries = r.Entries;
                p.NeedPassword = p.Encrypted;
                return p;
            }

            if (looksEncrypted)
            {
                foreach (string pw in passwords)
                {
                    Listing rp = List(file, pw, 0);
                    if ((rp.Code == 0 || rp.Code == 1) && rp.Type != null)
                    {
                        p.IsArchive = true; p.Type = rp.Type; p.Encrypted = true; p.HeaderEncrypted = true;
                        p.EntryCount = rp.Entries.Count; p.TotalSize = rp.TotalSize; p.PackedSize = rp.PackedSize;
                        p.Entries = rp.Entries; p.NeedPassword = true; p.Password = pw;
                        return p;
                    }
                }
                p.IsArchive = true; p.Type = p.SniffInfo.Kind; p.Encrypted = true; p.HeaderEncrypted = true;
                p.NeedPassword = true; p.Reason = "need-password";
                p.Raw = Trunc(r.Out + r.Err, 400);
                return p;
            }

            p.Reason = LooksLikeVolumeTail(System.IO.Path.GetFileName(file)) ? "maybe-missing-volume" : "not-archive";
            p.Raw = Trunc(r.Out + r.Err, 400);
            return p;
        }

        private static string Trunc(string s, int n) { if (s == null) return ""; return s.Length <= n ? s : s.Substring(0, n); }

        public class TestResult
        {
            public bool Ok;
            public int Code;
            public bool PasswordWrong;
            public bool MissingVolume;
            public bool TimedOut;
            public string Out = "";
            public string Err = "";
        }

        private static readonly string[] MissingVolumeHints = new string[]
        { "missing volume", "cannot open the file as", "unexpected end of archive" };

        public TestResult Test(string file, string password, Action<string> onOutput)
        {
            var args = new List<string>();
            args.Add("t"); args.Add("-sccUTF-8"); args.Add("-bd"); args.Add("-y"); args.Add(PwArg(password));
            args.Add("--"); args.Add(file);
            RunResult r = Run(Exe, args, ExtractTimeoutMs, onOutput);
            var t = new TestResult();
            t.Ok = r.Code == 0;
            t.Code = r.Code;
            t.TimedOut = r.TimedOut;
            t.Out = r.Out; t.Err = r.Err;
            string msg = r.Out + r.Err;
            t.PasswordWrong = r.Code != 0 && HasAny(msg, PwHints);
            t.MissingVolume = HasAny(msg, MissingVolumeHints);
            return t;
        }

        public class FindPwResult
        {
            public string Password;
            public TestResult Result;
            public int Tried;
        }

        public FindPwResult FindPassword(string file, List<string> passwords, Action<string> onTry)
        {
            var res = new FindPwResult();
            foreach (string pw in passwords)
            {
                if (onTry != null) onTry(pw);
                res.Tried++;
                TestResult t = Test(file, pw, null);
                res.Result = t;
                if (t.Ok) { res.Password = pw; return res; }
                if (t.TimedOut) return res;
            }
            return res;
        }

        public RunResult Extract(string file, string destDir, string password, Action<string> onOutput)
        {
            Directory.CreateDirectory(destDir);
            var args = new List<string>();
            args.Add("x"); args.Add("-sccUTF-8"); args.Add("-bd"); args.Add("-y"); args.Add("-aoa");
            args.Add("-o" + destDir); args.Add(PwArg(password));
            args.Add("--"); args.Add(file);
            return Run(Exe, args, ExtractTimeoutMs, onOutput);
        }
    }
}
