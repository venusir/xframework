using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XConfig;
using XFramework.XConfig.Luban;

namespace XFramework.XConfig.Luban.Tests
{
    /// <summary>
    /// 可编程的适配器替身：记录调用次数，默认行为是读取第一张表并返回一个假 Tables。
    /// </summary>
    internal sealed class FakeLubanAdapter : ILubanConfigAdapter
    {
        public string DataFileExtension { get; set; } = ".bytes";

        public IReadOnlyList<string> TableNames { get; set; } = new[] { "demo_tbitem" };

        public Func<Func<string, byte[]>, object> CreateTablesHandler { get; set; }

        public Action<object> RegisterHandler { get; set; }

        public Action UnregisterHandler { get; set; }

        public int CreateCalls { get; private set; }

        public int RegisterCalls { get; private set; }

        public int UnregisterCalls { get; private set; }

        public byte[] LastLoadedBytes { get; private set; }

        public object LastTables { get; private set; }

        public object CreateTables(Func<string, byte[]> loadBytes)
        {
            CreateCalls++;
            if (CreateTablesHandler != null)
            {
                return CreateTablesHandler(loadBytes);
            }

            LastLoadedBytes = loadBytes(TableNames[0]);
            LastTables = new FakeTables();
            return LastTables;
        }

        public void RegisterToConfigManager(object tables)
        {
            RegisterCalls++;
            RegisterHandler?.Invoke(tables);
        }

        public void UnregisterFromConfigManager()
        {
            UnregisterCalls++;
            UnregisterHandler?.Invoke();
        }
    }

    /// <summary>
    /// 数据后端替身：按表名返回预置字节，可注入异常/取消。
    /// </summary>
    internal sealed class FakeLubanProvider : ILubanDataProvider
    {
        public Dictionary<string, byte[]> Data { get; } = new(StringComparer.Ordinal);

        public Exception LoadException { get; set; }

        public bool ThrowCancellation { get; set; }

        public int LoadCalls { get; private set; }

        public IReadOnlyList<LubanTableFile> LastFiles { get; private set; }

        public async UniTask<Dictionary<string, byte[]>> LoadAsync(
            IReadOnlyList<LubanTableFile> files,
            LubanDataLoadMode loadMode,
            IProgress<LubanInitReport> progress,
            CancellationToken cancellationToken)
        {
            LoadCalls++;
            LastFiles = files;

            if (ThrowCancellation)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (LoadException != null)
            {
                throw LoadException;
            }

            await UniTask.Yield();

            var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            for (int i = 0; i < files.Count; i++)
            {
                if (Data.TryGetValue(files[i].TableName, out var bytes))
                {
                    result[files[i].TableName] = bytes;
                }
            }

            return result;
        }
    }

    /// <summary>
    /// 假 Tables 对象。
    /// </summary>
    internal sealed class FakeTables
    {
    }

    /// <summary>
    /// 测试行类型。
    /// </summary>
    internal sealed class FakeRow : IConfigRow<int>
    {
        public FakeRow(int id)
        {
            Id = id;
        }

        public int Id { get; }
    }

    /// <summary>
    /// 测试 Global 类型。
    /// </summary>
    internal sealed class FakeGlobal
    {
        public string Name { get; set; }
    }

    /// <summary>
    /// 同步收集进度报告的替身（避免 <see cref="Progress{T}"/> 的线程调度）。
    /// </summary>
    internal sealed class ListProgress : IProgress<LubanInitReport>
    {
        public List<LubanInitReport> Reports { get; } = new();

        public void Report(LubanInitReport value)
        {
            Reports.Add(value);
        }
    }
}
