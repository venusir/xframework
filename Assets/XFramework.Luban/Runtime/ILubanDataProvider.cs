using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XConfig.Luban
{
    /// <summary>
    /// 一个待加载的 Luban 数据文件。
    /// </summary>
    internal readonly struct LubanTableFile
    {
        /// <summary>表逻辑名（Luban 的 <c>output_data_file</c>）。</summary>
        internal string TableName { get; }

        /// <summary>AssetManager location。</summary>
        internal string Location { get; }

        internal LubanTableFile(string tableName, string location)
        {
            TableName = tableName;
            Location = location;
        }

        public override string ToString()
        {
            return TableName + " -> " + Location;
        }
    }

    /// <summary>
    /// 数据加载后端契约。默认实现走 <see cref="XFramework.XAsset.AssetManager"/>；
    /// 测试可注入 fake。仅 internal——没有需要对外替换的真实后端。
    /// </summary>
    internal interface ILubanDataProvider
    {
        /// <summary>
        /// 加载全部数据文件，返回「表逻辑名 → 字节」。
        /// </summary>
        /// <param name="files">待加载文件。</param>
        /// <param name="loadMode">加载方式。</param>
        /// <param name="progress">进度回调，可为 null。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        UniTask<Dictionary<string, byte[]>> LoadAsync(
            IReadOnlyList<LubanTableFile> files,
            LubanDataLoadMode loadMode,
            IProgress<LubanInitReport> progress,
            CancellationToken cancellationToken);
    }
}
