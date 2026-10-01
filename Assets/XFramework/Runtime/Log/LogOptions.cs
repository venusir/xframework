using System;
using System.IO;
using UnityEngine;

namespace XFramework.XLog
{
    /// <summary>
    /// 日志配置。仅在调用 <c>LogManager.Configure</c> 时读取；之后改本对象不影响已生效的配置。
    /// <para><b>不配置也能用</b>：门面在没有任何配置时用 <see cref="Default"/> 构建默认实现——
    /// 日志不能有「必须先初始化」这道门槛。</para>
    /// </summary>
    public sealed class LogOptions
    {
        /// <summary>
        /// 构造一份默认配置。默认值随运行环境推导：<b>Editor / Development 构建</b>下
        /// 全局档位为 <see cref="LogLevel.Debug"/>、默认抓栈；<b>Release</b> 下为 <see cref="LogLevel.Info"/>、不抓栈。
        /// <para>Release 不得低于 <see cref="LogLevel.Info"/>：迁移前 18 处 <c>Debug.Log</c> 映射到 Info，
        /// 低于它就会静默改变现有可见性。</para>
        /// </summary>
        public LogOptions()
        {
            bool development = Application.isEditor || Debug.isDebugBuild;

            MinimumLevel = development ? LogLevel.Debug : LogLevel.Info;
            EnableConsoleSink = true;
            CaptureStackTrace = development;
            StackTraceMinLevel = LogLevel.Error;

            EnableFileSink = development;
            CaptureUnityLogs = EnableFileSink;
            FileDirectory = ResolveDefaultDirectory();
            MaxFileBytes = 32 * 1024 * 1024;
            MaxRetainedFiles = 10;
            ImmediateFlushMinLevel = LogLevel.Warning;
            FlushEveryEntries = 64;
        }

        /// <summary>
        /// 默认配置实例。<b>每次访问都返回新实例</b>——共享可变实例曾把别的模块的 <c>Default</c> 变成一个
        /// 「改一处静音全局」的陷阱，不重蹈。
        /// </summary>
        public static LogOptions Default => new LogOptions();

        /// <summary>全局最低档位。默认：Editor/Development 为 <see cref="LogLevel.Debug"/>，其余为 <see cref="LogLevel.Info"/>。</summary>
        public LogLevel MinimumLevel { get; set; }

        /// <summary>是否渲染到 Unity 控制台。默认 <c>true</c>——关掉它 <c>LogAssert</c> 就看不到任何东西。</summary>
        public bool EnableConsoleSink { get; set; }

        /// <summary>是否捕获调用点托管堆栈。默认：Editor/Development <c>true</c>，Release <c>false</c>（抓栈不便宜）。</summary>
        public bool CaptureStackTrace { get; set; }

        /// <summary>抓栈的最低级别。默认 <see cref="LogLevel.Error"/>——Warning 及以下默认不抓。</summary>
        public LogLevel StackTraceMinLevel { get; set; }

        /// <summary>
        /// 是否写 JSONL 文件。默认：Editor / Development <c>true</c>，Release <c>false</c>——不为不用的功能
        /// 在发布版里付 IO 代价。开启后每条日志一行、每会话一个文件（见模块 README 的 schema 表）。
        /// </summary>
        public bool EnableFileSink { get; set; }

        /// <summary>
        /// 是否把引擎 / 第三方库 / 未捕获异常的日志也收进同一份文件（每条标 <c>src:"unity"</c>）。
        /// <para>默认与 <see cref="EnableFileSink"/> 的默认值相同（Editor / Development 开）。开启后**外部日志也走
        /// 同一套档位过滤**——这保证 <see cref="MinimumLevel"/> 的「低于它一律丢弃」不会自相矛盾。</para>
        /// <para>关闭它，文件里就只剩经 <c>LogManager</c> 发出的日志（<c>src:"fw"</c>）。</para>
        /// </summary>
        public bool CaptureUnityLogs { get; set; }

        /// <summary>
        /// JSONL 落盘目录。默认 <c>{persistentDataPath}/XLog</c>；取不到时回退 <c>{临时目录}/XLog</c>。
        /// </summary>
        public string FileDirectory { get; set; }

        /// <summary>单个分片文件的上限字节数，超过则切分 <c>-p2</c>。默认 32 MiB。</summary>
        public long MaxFileBytes { get; set; }

        /// <summary>目录内保留的最新文件数（含所有分片），超出的按时间删除。默认 10。</summary>
        public int MaxRetainedFiles { get; set; }

        /// <summary>达到该级别即立即 flush（崩溃后 AI 要能读到现场）。默认 <see cref="LogLevel.Warning"/>。</summary>
        public LogLevel ImmediateFlushMinLevel { get; set; }

        /// <summary>低于立即 flush 门槛的条目按条数批量 flush。默认 64 条。</summary>
        public int FlushEveryEntries { get; set; }

        /// <summary>
        /// 解析默认落盘目录。<b>绝不抛</b>：<c>persistentDataPath</c> 取不到（早期启动阶段或平台差异）时
        /// 回退到临时目录——「日志写不出去」不该升级成「日志模块初始化失败」。
        /// </summary>
        private static string ResolveDefaultDirectory()
        {
            try
            {
                string root = Application.persistentDataPath;
                if (!string.IsNullOrEmpty(root))
                    return Path.Combine(root, "XLog");
            }
            catch (Exception)
            {
                // 落到下面的临时目录
            }

            return Path.Combine(Path.GetTempPath(), "XLog");
        }
    }
}
