using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace XFramework.XLog.Internal
{
    /// <summary>
    /// 会话标识与环境快照：一次运行一份，供 JSONL 的会话头行与每行的 <c>session</c> 字段使用。
    /// <para><b>为什么环境信息在这里抓</b>：<c>Application.*</c> 与 <c>CultureInfo.CurrentCulture</c> 是主线程语境，
    /// 而文件可能在任意线程上首次打开。于是环境快照在 <see cref="Reset"/>（由 <c>AutoInit</c> 在主线程调用）
    /// 时一次性抓取，写路径只读快照。</para>
    /// <para><b>必须可复位</b>：关闭域重载时静态字段跨播放会话存活，不复位就会让两次运行共用同一个 session id
    /// ——而「按 session 切片」正是 AI 分析的第一条纪律。</para>
    /// </summary>
    internal static class LogSession
    {
        #region Private Fields

        private static readonly Stopwatch Clock = Stopwatch.StartNew();

        private static string _id = NewId();
        private static string _unityVersion = string.Empty;
        private static string _product = string.Empty;
        private static string _company = string.Empty;
        private static string _platform = string.Empty;
        private static string _culture = string.Empty;
        private static bool _development;
        private static bool _editor;

        /// <summary>会话起始 UTC 刻度。<b>不能留零值</b>：若 AutoInit 尚未跑过就被用到，零值会写出年份 1 的时间戳。</summary>
        private static long _startTicks = DateTime.UtcNow.Ticks;

        #endregion

        #region Internal API

        /// <summary>会话 id：8 位小写十六进制，进程内唯一（每次 <see cref="Reset"/> 重新生成）。</summary>
        internal static string Id => _id;

        /// <summary>自本次会话开始以来的毫秒数（单调时钟，不是墙钟——墙钟会被系统时间调整影响）。</summary>
        internal static long ElapsedMs => Clock.ElapsedMilliseconds;

        /// <summary>自本次会话开始的 UTC 时刻。</summary>
        internal static DateTime StartUtc => new DateTime(_startTicks, DateTimeKind.Utc);

        /// <summary>重新生成会话（新的 id 与计时起点）并抓取环境快照。在主线程调用。</summary>
        internal static void Reset()
        {
            ResetClock();

            try
            {
                _id = NewId();
                _unityVersion = Application.unityVersion;
                _product = Application.productName;
                _company = Application.companyName;
                _platform = Application.platform.ToString();
                _development = UnityEngine.Debug.isDebugBuild; // 全名：本文件同时引用了 System.Diagnostics（Stopwatch）
                _editor = Application.isEditor;
                _culture = CultureInfo.CurrentCulture.Name;
            }
            catch (Exception)
            {
                // 环境信息取不到不致命：字段留空即可，日志永不因元数据失败而停摆
            }
        }

        /// <summary>把会话头行写进 <paramref name="sb"/>（含行尾换行）。每个 part 文件的首行都要写。</summary>
        /// <param name="sb">目标缓冲。</param>
        /// <param name="part">分片号，从 1 起。</param>
        /// <param name="configuredLevel">配置的全局档位。</param>
        internal static void AppendHeader(StringBuilder sb, int part, LogLevel configuredLevel)
        {
            sb.Append("{\"t\":\"session\",\"v\":1,\"session\":\"");
            sb.Append(_id);
            sb.Append("\",\"ts\":\"");
            JsonLineWriter.AppendIsoTimestamp(sb, StartUtc);
            sb.Append("\",\"up\":0,\"unity\":");
            JsonLineWriter.AppendEscaped(sb, _unityVersion);
            sb.Append(",\"product\":");
            JsonLineWriter.AppendEscaped(sb, _product);
            sb.Append(",\"company\":");
            JsonLineWriter.AppendEscaped(sb, _company);
            sb.Append(",\"platform\":");
            JsonLineWriter.AppendEscaped(sb, _platform);
            sb.Append(",\"dev\":").Append(_development ? "true" : "false");
            sb.Append(",\"editor\":").Append(_editor ? "true" : "false");
            sb.Append(",\"lvl\":\"").Append(LogTokens.Level(configuredLevel));
            sb.Append("\",\"part\":").Append(part);
            sb.Append(",\"culture\":");
            JsonLineWriter.AppendEscaped(sb, _culture);
            sb.Append("}\n");
        }

        #endregion

        #region Private

        private static void ResetClock()
        {
            Clock.Restart();
            _startTicks = DateTime.UtcNow.Ticks;
        }

        private static string NewId()
        {
            return Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        #endregion
    }
}
