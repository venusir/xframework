using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XData
{
    /// <summary>
    /// 数据持久化存储接口。
    /// <para>定义存档的保存、加载、删除、存在性检查等操作。
    /// 框架内置 <see cref="FileDataStore"/> 抽象基类和 <see cref="JsonFileDataStore"/> 默认实现。</para>
    /// <para>第三方可实现此接口接入自定义存储后端（如 Protobuf、MessagePack、云端存储等）。</para>
    /// </summary>
    public interface IDataStore
    {
        /// <summary>
        /// 异步保存存档数据。
        /// </summary>
        /// <param name="name">存档名称（如 "autosave", "slot1"）。</param>
        /// <param name="data">已填充好 tables / globals 的 <see cref="SaveData"/> 快照。</param>
        /// <param name="ct">取消令牌。</param>
        UniTask SaveAsync(string name, SaveData data, CancellationToken ct = default);

        /// <summary>
        /// 异步加载存档数据。文件不存在时返回 <c>null</c>。
        /// </summary>
        /// <param name="name">存档名称。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns><see cref="SaveData"/> 快照，不存在时返回 <c>null</c>。</returns>
        UniTask<SaveData> LoadAsync(string name, CancellationToken ct = default);

        /// <summary>
        /// 删除指定存档。
        /// </summary>
        /// <param name="name">存档名称。</param>
        void Delete(string name);

        /// <summary>
        /// 判断指定存档是否存在。
        /// </summary>
        /// <param name="name">存档名称。</param>
        /// <returns>存在时返回 <c>true</c>。</returns>
        bool Exists(string name);
    }
}