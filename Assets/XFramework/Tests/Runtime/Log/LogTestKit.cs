using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace XFramework.XLog.Tests
{
    /// <summary>
    /// 日志测试的共用夹具与 JSONL 读取工具。
    /// <para><b>复位为什么用 <c>Shutdown</c> 而不是专门的测试 API</b>：<c>Shutdown</c> 本身就是公开、
    /// 语义正确的复位入口（冲刷 → 释放 sink → 复位档位 → 回到未装入状态，下次使用按默认配置重建）。
    /// 为测试再造一个 <c>ResetForTests</c> 只会多一条与生产路径不同的复位路径。</para>
    /// <para><b>配置固定为「全局 Verbose + 控制台开 + 不抓栈 + 不写文件 + 不捕获」</b>：让用例自己用档位控制输出，
    /// 不依赖 Editor/Development 的默认档位，也不往用户目录写文件。文件与捕获的用例自行覆盖配置。</para>
    /// </summary>
    internal static class LogTestKit
    {
        #region JSONL 读取

        /// <summary>
        /// JSONL 行的解析目标。数字字段一律用 <c>int</c>——Unity 的序列化器不支持 <c>long</c>，
        /// 用 <c>long</c> 会让字段被静默忽略（断言随之失去意义）。
        /// </summary>
        [Serializable]
        internal class LogLineDto
        {
            public string t;
            public int v;
            public string session;
            public int seq;
            public string ts;
            public int up;
            public string lvl;
            public string cat;
            public string msg;
            public int frame;
            public int thread;
            public string src;
            public string exc;
            public string stack;
            public int part;
            public string unity;
        }

        /// <summary>
        /// 按序号序列出目录里的 JSONL 文件（文件名带时间戳，序号序即时间序）。
        /// <para>「目录不存在」是合法状态（一条日志都没写过，文件就不会被打开、目录也不会被创建），
        /// 返回空数组而不是抛异常——否则断言「什么都没写」的用例会红在自己的读取工具上。</para>
        /// </summary>
        internal static string[] JsonlFiles(string directory)
        {
            if (!Directory.Exists(directory))
                return Array.Empty<string>();

            string[] files = Directory.GetFiles(directory, "*.jsonl");
            Array.Sort(files, StringComparer.Ordinal);
            return files;
        }

        /// <summary>
        /// 读取文件全部行。
        /// <para><b>必须显式传 <c>FileShare.ReadWrite</c></b>：Windows 的共享规则是双向的，写入端还开着写句柄，
        /// 读者只声明「允许别人读」照样冲突——<c>File.ReadAllLines</c> 内部就是这种声明，所以不能用它。
        /// 这条同时是给外部分析脚本的提示（模块 README 已写明）。</para>
        /// </summary>
        internal static string[] ReadLines(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                var lines = new List<string>();
                string line;
                while ((line = reader.ReadLine()) != null)
                    lines.Add(line);
                return lines.ToArray();
            }
        }

        /// <summary>解析一行 JSONL。解析失败会抛——这本身就是「输出不是合法 JSON」的断言。</summary>
        internal static LogLineDto ParseLine(string line)
        {
            return JsonUtility.FromJson<LogLineDto>(line);
        }

        /// <summary>把目录下所有文件的日志行读成 DTO（跳过会话头行）。</summary>
        internal static List<LogLineDto> ReadEntries(string directory)
        {
            var entries = new List<LogLineDto>();

            foreach (string file in JsonlFiles(directory))
            {
                foreach (string line in ReadLines(file))
                {
                    LogLineDto dto = ParseLine(line);
                    if (dto.t == "log")
                        entries.Add(dto);
                }
            }

            return entries;
        }

        #endregion

        #region 夹具

        /// <summary>复位门面并装入确定性配置。每个用例的 SetUp 调用。</summary>
        internal static void SetUp()
        {
            LogManager.Shutdown();
            LogManager.Configure(new LogOptions
            {
                MinimumLevel = LogLevel.Verbose,
                EnableConsoleSink = true,
                CaptureStackTrace = false,
                StackTraceMinLevel = LogLevel.Error,
                EnableFileSink = false, // 用例不往用户目录写文件；文件 sink 与捕获的用例自行开
                CaptureUnityLogs = false,
            });
        }

        /// <summary>复位门面。每个用例的 TearDown 调用——PlayMode 下所有用例共享一个 player 实例。</summary>
        internal static void TearDown()
        {
            LogManager.Shutdown();
        }

        #endregion
    }
}
