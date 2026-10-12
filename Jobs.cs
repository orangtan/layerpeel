// layerpeel 3.0 —— 解压 · 去套娃助手
// Copyright (C) 2026 layerpeel contributors
//
// 本程序是 ssh-buanshishi / f-keydle（GPL-3.0）的 C# 重写版，自 2026-10-12 起：
// 用 C# 重新实现全部功能，解压后端换成 7-Zip，去掉了 Bandizip / AHK 依赖。
// 按 GNU General Public License v3.0（或更新版本）发布，全文见 LICENSE。
// 本程序不提供任何担保。
//
// 任务执行层：界面（WPF）与 HTTP 模式共用，避免两套逻辑走偏。
using System;
using System.Collections.Generic;
using System.Threading;

namespace LayerPeel
{
    /// <summary>一次任务的参数。界面和 HTTP 两种入口都构造它。</summary>
    public class Job
    {
        public string Task = "extract";       // extract | flatten | clean | cleanup | acl | acl-restore
        public List<string> Targets = new List<string>();
        public string Mode;                   // 覆盖 delete.mode
        public int Depth = -1;
        public bool Verify = true;
        public int AclLevel = 2;
        public List<string> FilePatterns;     // clean 用
        public List<string> DirPatterns;
        public volatile bool Aborted;
        public long StartedAt = DateTime.UtcNow.Ticks;
        public string LogFile;
    }

    public class JobContext
    {
        public string ConfigPath;
        public string LogDir;
        public string AclBackupDir;
        public Action<Dictionary<string, object>> OnEvent;   // 事件（含 job-start / job-done）
        public bool SilentConsole;                           // WPF 模式下不要把审计行也打到控制台
    }

    public static class JobService
    {
        /// <summary>在后台线程上跑一个任务；返回的 Thread 已在运行。</summary>
        public static Thread Start(JobContext ctx, Job job, Action onFinished)
        {
            var t = new Thread(delegate ()
            {
                try { Run(ctx, job); }
                catch (Exception e)
                {
                    Emit(ctx, "job-error", "detail", e.Message);
                }
                finally { if (onFinished != null) { try { onFinished(); } catch { } } }
            });
            t.IsBackground = true;
            t.Start();
            return t;
        }

        private static void Emit(JobContext ctx, string type, params object[] kv)
        {
            if (ctx.OnEvent == null) return;
            var ev = new Dictionary<string, object>();
            ev["time"] = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
            for (int i = 0; i + 1 < kv.Length; i += 2) ev[Convert.ToString(kv[i])] = kv[i + 1];
            ev["type"] = type;
            try { ctx.OnEvent(ev); } catch { }
        }

        private static void Run(JobContext ctx, Job job)
        {
            Config cfg;
            try { cfg = Config.Load(ctx.ConfigPath); }
            catch (Exception e) { Emit(ctx, "job-error", "detail", e.Message); return; }

            if (job.Mode != null) Json.Obj(cfg.Data, "delete")["mode"] = job.Mode;
            if (job.Depth > 0) Json.Obj(cfg.Data, "behavior")["maxDepth"] = job.Depth;
            if (!job.Verify) Json.Obj(cfg.Data, "behavior")["verifyByCompare"] = false;
            if (job.FilePatterns != null) Json.Obj(cfg.Data, "delete")["filePatterns"] = job.FilePatterns.ToArray();
            if (job.DirPatterns != null) Json.Obj(cfg.Data, "delete")["dirPatterns"] = job.DirPatterns.ToArray();

            var audit = new Audit();
            audit.EchoConsole = !ctx.SilentConsole;
            string logFile = audit.Open(ctx.LogDir, "");            job.LogFile = logFile;

            Emit(ctx, "job-start", "task", job.Task, "targets", job.Targets.ToArray(),                 "logFile", logFile, "mode", Json.PathStr(cfg.Data, "delete.mode", "recycle"));

            Func<bool> aborted = delegate () { return job.Aborted; };
            Dictionary<string, object> statsMap = null;
            try
            {
                if (job.Task == "clean")
                {
                    var engine0 = new Engine(cfg, audit);
                    var cleaner = new JunkCleaner(cfg, audit, engine0);
                    cleaner.OnEvent = ctx.OnEvent;
                    JunkCleaner.RunOutcome o = cleaner.Run(job.Targets, aborted);                    statsMap = o.Stats.ToMap();
                    if (o.Refused) Emit(ctx, "job-error", "detail", string.Join("；", o.Errors.ToArray()));
                }
                else if (job.Task == "acl" || job.Task == "acl-restore")
                {
                    var fixer = new AclFixer(ctx.AclBackupDir, audit);
                    fixer.OnEvent = ctx.OnEvent;
                    long t0 = DateTime.UtcNow.Ticks;
                    var st = new Stats();
                    if (job.Task == "acl-restore")
                    {
                        foreach (string t in job.Targets)
                        {
                            AclFixer.RestoreResult r = fixer.Restore(t);
                            if (r.Ok) st.Restored++; else st.Failed++;
                        }
                    }
                    else
                    {
                        foreach (string t in job.Targets)
                        {
                            if (job.Aborted) break;
                            AclFixer.FixResult r = fixer.Fix(t, job.AclLevel);
                            if (r.Ok) st.Fixed++; else st.Failed++;
                        }
                    }
                    st.ElapsedMs = (DateTime.UtcNow.Ticks - t0) / TimeSpan.TicksPerMillisecond;
                    statsMap = st.ToMap();
                }
                else if (job.Task == "flatten")
                {
                    var engine = new Engine(cfg, audit);
                    engine.OnEvent = ctx.OnEvent;
                    engine.FlattenOnly(job.Targets, aborted);                    engine.RunFinish();                    statsMap = engine.Stats.ToMap();
                }
                else if (job.Task == "cleanup")
                {
                    var engine = new Engine(cfg, audit);
                    engine.OnEvent = ctx.OnEvent;
                    engine.CleanupLeftovers(job.Targets, aborted);                    engine.RunFinish();                    statsMap = engine.Stats.ToMap();
                }
                else
                {
                    var engine = new Engine(cfg, audit);
                    engine.OnEvent = ctx.OnEvent;
                    engine.Run(job.Targets, aborted);                    statsMap = engine.Stats.ToMap();
                }
            }
            catch (Exception e)
            {
                Emit(ctx, "job-error", "detail", e.Message);
            }
            finally
            {
                audit.Close();
                Emit(ctx, "job-done", "task", job.Task, "stats", statsMap, "logFile", logFile);            }
        }
    }
}
