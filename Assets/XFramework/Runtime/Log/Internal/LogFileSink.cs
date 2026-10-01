using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace XFramework.XLog.Internal
{
    /// <summary>
    /// JSONL 文件输出端：每条日志一行，引擎/第三方/未捕获异常与框架日志共用同一份 schema。
    /// <para><b>每会话一文件</b>（不是往同一个文件里滚动追加）：「一次运行一条干净的时间线」是 AI 分析的价值所在，
    /// 追加会把崩溃现场与正常运行混在一起。超 <c>MaxFileBytes</c> 时切分片 <c>-p2</c>、<c>-p3</c>…，
    /// 每个分片首行都重写会话头，因此任意单个文件都自描述。</para>
    /// <para><b>同步加锁写、不开后台线程</b>：崩溃后 AI 要能读到——后台队列会把最后几条留在内存里。
    /// 写文件不在每帧路径上，锁竞争可忽略。</para>
    /// <para><b>失败即静默停用</b>：磁盘满、目录不可写等 IO 失败后本输出端自我关闭，<b>不记日志</b>
    /// （「因为日志坏了而记一条日志」会递归，且测试把意外 Error 当失败）；控制台通路不受影响，
    /// 那是 Unity 自己 Player.log 的职责。</para>
    /// </summary>
    internal sealed class LogFileSink : ILogSink, IDisposable
    {
        #region Private Fields

        private const string FilePrefix = "xlog-";
        private const string FileExtension = ".jsonl";

        private readonly object _lock = new object();

        private readonly string _directory;
        private readonly string _session;
        private readonly string _baseName;
        private readonly long _maxFileBytes;
        private readonly int _maxRetainedFiles;
        private readonly LogLevel _immediateFlushMinLevel;
        private readonly int _flushEveryEntries;
        private readonly LogLevel _configuredLevel;

        private StreamWriter _writer;
        private int _part;
        private long _writtenBytes;
        private int _pendingWrites;
        private bool _dead;

        #endregion

        #region Constructors

        /// <summary>按配置构建输出端；文件在第一次写入时才真正打开（没日志就不会留下空文件）。</summary>
        /// <param name="options">配置。</param>
        internal LogFileSink(LogOptions options)
        {
            _directory = options.FileDirectory;
            _session = LogSession.Id;
            _baseName = FilePrefix
                        + LogSession.StartUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
                        + "-" + _session;
            _maxFileBytes = options.MaxFileBytes;
            _maxRetainedFiles = options.MaxRetainedFiles;
            _immediateFlushMinLevel = options.ImmediateFlushMinLevel;
            _flushEveryEntries = options.FlushEveryEntries;
            _configuredLevel = options.MinimumLevel;
        }

        #endregion

        #region Internal API

        /// <summary>测试缝：本输出端是否因 IO 失败而静默停用。</summary>
        internal bool IsDead => _dead;

        #endregion

        #region ILogSink

        /// <summary>写入一条日志。任意线程调用；IO 失败时静默停用，不抛异常。</summary>
        /// <param name="entry">日志条目。</param>
        public void Write(in LogEntry entry)
        {
            if (_dead)
                return;

            lock (_lock)
            {
                if (_dead)
                    return;

                try
                {
                    EnsureOpen();

                    string line = BuildLine(in entry);
                    _writer.Write(line);
                    _writtenBytes += Encoding.UTF8.GetByteCount(line);

                    if (entry.Level >= _immediateFlushMinLevel)
                    {
                        FlushWriter(); // 崩溃可读性的下限：Warning 及以上立即落盘
                    }
                    else if (++_pendingWrites >= _flushEveryEntries)
                    {
                        FlushWriter();
                    }

                    if (_writtenBytes >= _maxFileBytes)
                    {
                        CloseWriter();
                        OpenPart(_part + 1);
                    }
                }
                catch (Exception)
                {
                    Die();
                }
            }
        }

        /// <summary>把缓冲刷到磁盘。任意线程调用；失败时静默停用，不抛异常。</summary>
        public void Flush()
        {
            if (_dead)
                return;

            lock (_lock)
            {
                if (_dead || _writer == null)
                    return;

                try
                {
                    FlushWriter();
                }
                catch (Exception)
                {
                    Die();
                }
            }
        }

        /// <summary>冲刷并关闭文件。</summary>
        public void Dispose()
        {
            lock (_lock)
            {
                CloseWriter();
                _dead = true;
            }
        }

        #endregion

        #region Private — 文件生命周期

        private void EnsureOpen()
        {
            if (_writer != null)
                return;

            Directory.CreateDirectory(_directory);
            RotateOldFiles();
            OpenPart(1);
        }

        private void OpenPart(int part)
        {
            _part = part;

            string path = Path.Combine(_directory, _baseName + "-p" + part + FileExtension);

            // FileShare.Read：写入端允许他人读取，但**不允许他人并发写**（防日志被第三方进程写坏）。
            // 注意 Windows 的共享规则是双向的：读者必须自己声明 FileShare.ReadWrite 才能与写者共存——
            // 用 .NET 的 File.ReadAllLines（内部是 FileShare.Read）会撞 Sharing violation，
            // 这是读者侧的要求，模块 README 已写明。
            var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            _writer = new StreamWriter(stream, new UTF8Encoding(false))
            {
                NewLine = "\n", // 绝不写 \r\n：AI 侧按行解析与 grep 都要求行尾稳定
                AutoFlush = false,
            };

            _writtenBytes = 0;
            _pendingWrites = 0;

            StringBuilder sb = JsonLineWriter.Rent();
            LogSession.AppendHeader(sb, part, _configuredLevel);
            string header = sb.ToString();

            _writer.Write(header);
            _writtenBytes += Encoding.UTF8.GetByteCount(header);
            _writer.Flush(); // 头行立即落盘：文件一旦存在就自描述
        }

        private void FlushWriter()
        {
            _writer.Flush();
            _pendingWrites = 0;
        }

        private void CloseWriter()
        {
            StreamWriter writer = _writer;
            _writer = null;
            if (writer == null)
                return;

            try
            {
                writer.Flush();
            }
            catch (Exception)
            {
                // 关闭路径上的失败无处可去，也不该再记日志
            }

            try
            {
                writer.Dispose();
            }
            catch (Exception)
            {
                // 同上
            }
        }

        /// <summary>IO 失败：静默停用（不记日志、不抛出）。</summary>
        private void Die()
        {
            _dead = true;
            CloseWriter();
        }

        /// <summary>轮转：保留最新 <c>MaxRetainedFiles</c> 个文件（含即将创建的这一个）。</summary>
        private void RotateOldFiles()
        {
            string[] files = Directory.GetFiles(_directory, FilePrefix + "*" + FileExtension);

            int removeCount = files.Length - _maxRetainedFiles + 1;
            if (removeCount <= 0)
                return;

            // 文件名带时间戳与会话 id，序号序即时间序
            Array.Sort(files, StringComparer.Ordinal);

            for (int i = 0; i < removeCount && i < files.Length; i++)
            {
                try
                {
                    File.Delete(files[i]);
                }
                catch (Exception)
                {
                    // 删不掉旧文件的唯一后果是占点磁盘，不该因此停掉日志
                }
            }
        }

        #endregion

        #region Private — 行构建

        /// <summary>构建一行 JSONL（含行尾换行）。固定键序：<c>t/session/seq/ts/up/lvl/cat/msg/frame/thread/src</c>，可选尾字段整体省略。</summary>
        private string BuildLine(in LogEntry entry)
        {
            StringBuilder sb = JsonLineWriter.Rent();

            sb.Append("{\"t\":\"log\",\"session\":\"").Append(_session);
            sb.Append("\",\"seq\":").Append(entry.Sequence);
            sb.Append(",\"ts\":\"");
            JsonLineWriter.AppendIsoTimestamp(sb, entry.UtcTime);
            sb.Append("\",\"up\":").Append(LogSession.ElapsedMs);
            sb.Append(",\"lvl\":\"").Append(LogTokens.Level(entry.Level));
            sb.Append("\",\"cat\":");
            JsonLineWriter.AppendEscaped(sb, entry.Category.Name);
            sb.Append(",\"msg\":");
            JsonLineWriter.AppendEscaped(sb, entry.Message);
            sb.Append(",\"frame\":").Append(entry.Frame);
            sb.Append(",\"thread\":").Append(entry.ThreadId);
            sb.Append(",\"src\":\"").Append(LogTokens.Source(entry.Source)).Append('"');

            if (entry.Exception != null)
            {
                sb.Append(",\"exc\":");
                JsonLineWriter.AppendEscaped(sb, entry.Exception.ToString());
            }

            if (entry.StackTrace != null)
            {
                sb.Append(",\"stack\":");
                JsonLineWriter.AppendEscaped(sb, entry.StackTrace);
            }

            sb.Append("}\n");
            return sb.ToString();
        }

        #endregion
    }
}
