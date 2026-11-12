using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XFileManager;
using XFramework.XLog;
using XFramework.XSerialize;

namespace XFramework.Samples.Integration
{
    /// <summary>上次游玩摘要（File + Serialize 的落盘载荷）。</summary>
    [Serializable]
    public sealed class LastRunSummary
    {
        /// <summary>累计游玩场次。</summary>
        public int Sessions;

        /// <summary>上次玩的模式 id。</summary>
        public string LastMode = "(none)";

        /// <summary>历史最佳分。</summary>
        public float BestScore;
    }

    /// <summary>
    /// 上次游玩摘要的读写：<c>FileManager</c>（AppData 域）+ <c>Serializer</c>（默认 JSON 序列化器）。
    /// <para>它和 Save 模块的分工：存档走 <c>SaveManager</c>（槽位、版本、备份），这里是
    /// 「跨存档的小型展示数据」——主菜单上那行「上次玩了什么」。</para>
    /// </summary>
    public static class LastRunStore
    {
        #region Constants

        private const string RelativePath = "integration/last-run.json";

        #endregion

        #region Public API

        /// <summary>读取摘要；文件不存在或损坏时返回默认值，不抛。</summary>
        public static LastRunSummary Read()
        {
            try
            {
                if (!FileManager.Exists(FileDomain.AppData, RelativePath))
                    return new LastRunSummary();

                var bytes = FileManager.ReadAllBytes(FileDomain.AppData, RelativePath);
                var summary = Serializer.Default.Deserialize(bytes, typeof(LastRunSummary)) as LastRunSummary;
                return summary ?? new LastRunSummary();
            }
            catch (Exception e)
            {
                LogManager.Exception(IntegrationLogCategories.Flow, e, "读取上次游玩摘要失败");
                return new LastRunSummary();
            }
        }

        /// <summary>写入摘要。</summary>
        public static async UniTask WriteAsync(LastRunSummary summary,
            CancellationToken cancellationToken = default)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));

            var bytes = Serializer.Default.Serialize(summary, typeof(LastRunSummary));
            await FileManager.WriteAllBytesAsync(FileDomain.AppData, RelativePath, bytes, cancellationToken);
        }

        #endregion
    }
}
