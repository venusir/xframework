using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XSave
{
    /// <summary>
    /// 存档管理器接口，负责存档的保存、加载、删除与元数据查询。
    /// <para>内部通过 <see cref="XData.DataManager"/> 收集/应用快照，
    /// 通过 <see cref="XSerialize.Serializer"/> 序列化，
    /// 通过 <see cref="XFileManager.FileManager"/> 写入到 <see cref="XFileManager.FileDomain.SaveData"/>。</para>
    /// <para>第三方可实现此接口接入自定义存储后端（如 Steam Cloud、PS5 SaveData API）。</para>
    /// <para><b>命名约定：</b>所有涉及 IO 的成员一律异步并以 <c>Async</c> 后缀结尾，不提供同步版本——
    /// 同步 IO 会阻塞主线程，而 Console 等平台的 Provider 可能是数百毫秒的平台 SDK 调用。</para>
    /// </summary>
    public interface ISaveManager
    {
        #region 状态

        /// <summary>是否有写操作（保存/加载/删除）正在进行。</summary>
        bool IsBusy { get; }

        /// <summary>
        /// 当前操作玩家 ID。
        /// <para>为 <c>null</c> 时不启用玩家隔离，存档直接位于 <see cref="XFileManager.FileDomain.SaveData"/> 根目录。</para>
        /// </summary>
        string CurrentPlayerId { get; }

        #endregion

        #region 玩家上下文

        /// <summary>
        /// 设置当前操作玩家 ID。后续所有操作均作用于此玩家。
        /// <para>传入 <c>null</c> 或空字符串等同于 <see cref="ClearCurrentPlayer"/>。</para>
        /// <para>非法 ID（含路径分隔符、<c>.</c>、<c>..</c>、文件系统非法字符等）会被立即拒绝。</para>
        /// </summary>
        /// <param name="playerId">玩家 ID。</param>
        /// <exception cref="System.ArgumentException"><paramref name="playerId"/> 非法时抛出。</exception>
        /// <exception cref="System.InvalidOperationException">有写操作进行中时抛出。</exception>
        void SetCurrentPlayer(string playerId);

        /// <summary>
        /// 清除玩家上下文，退回到无玩家隔离模式。
        /// </summary>
        /// <exception cref="System.InvalidOperationException">有写操作进行中时抛出。</exception>
        void ClearCurrentPlayer();

        /// <summary>
        /// 获取所有存在存档数据的玩家 ID 列表。
        /// <para>不会改变当前玩家上下文。</para>
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>玩家 ID 数组，无玩家数据时返回空数组。</returns>
        UniTask<string[]> GetAllPlayerIdsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 删除指定玩家的所有存档数据。
        /// <para>不会改变当前玩家上下文。</para>
        /// </summary>
        /// <param name="playerId">要删除的玩家 ID。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>实际删除的槽位数量。</returns>
        UniTask<int> DeletePlayerAsync(string playerId, CancellationToken cancellationToken = default);

        #endregion

        #region 槽位操作

        /// <summary>
        /// 获取所有存档槽位的元数据列表（按槽位号升序）。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>当前玩家上下文下的存档元数据；无存档时返回空列表。</returns>
        UniTask<List<SaveMeta>> GetSlotMetasAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取指定槽位的元数据。
        /// </summary>
        /// <param name="slot">槽位号。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>该槽位的元数据；槽位不存在时返回 <c>null</c>。</returns>
        UniTask<SaveMeta> GetSlotMetaAsync(int slot, CancellationToken cancellationToken = default);

        /// <summary>
        /// 将当前游戏数据保存到指定槽位。
        /// </summary>
        /// <param name="slot">槽位号。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>保存后该槽位的元信息。</returns>
        UniTask<SaveMeta> SaveAsync(int slot, CancellationToken cancellationToken = default);

        /// <summary>
        /// 从指定槽位加载存档并恢复到当前游戏数据。
        /// <para>加载前会清空现有数据。</para>
        /// </summary>
        /// <param name="slot">槽位号。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <exception cref="System.InvalidOperationException">槽位不存在、文件为空或内容不是有效存档时抛出。</exception>
        UniTask LoadAsync(int slot, CancellationToken cancellationToken = default);

        /// <summary>
        /// 删除指定槽位的存档。
        /// </summary>
        /// <param name="slot">槽位号。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>确实删除了存档返回 <c>true</c>；槽位本就不存在返回 <c>false</c>。</returns>
        UniTask<bool> DeleteSlotAsync(int slot, CancellationToken cancellationToken = default);

        /// <summary>
        /// 删除当前玩家上下文下的所有槽位存档。
        /// <para>同时清理可能残留的临时文件。</para>
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>实际删除的槽位数量。</returns>
        UniTask<int> DeleteAllSlotsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 检查指定槽位是否存在存档。
        /// <para>谓词语义：槽位号非法或不存在都返回 <c>false</c>，不抛异常。</para>
        /// </summary>
        /// <param name="slot">槽位号。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>存在返回 <c>true</c>。</returns>
        UniTask<bool> SlotExistsAsync(int slot, CancellationToken cancellationToken = default);

        #endregion
    }

    /// <summary>
    /// <see cref="ISaveManager"/> 创建工厂委托。
    /// <para>在 <see cref="SaveManager.Initialize(SaveManagerFactory)"/> 中注册，
    /// 第三方可传入自定义实现替代默认的 <see cref="SaveManagerImpl"/>。</para>
    /// </summary>
    public delegate ISaveManager SaveManagerFactory();
}
