using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XFileManager;

namespace XFramework.XSave
{
    /// <summary>
    /// 存档管理器静态门面。
    /// <para>第三方业务代码通过本类静态方法进行存档操作，
    /// 内部实现可替换（默认 <see cref="SaveManagerImpl"/>）。</para>
    /// <para>使用前必须调用 <see cref="Initialize"/> 注入实现。</para>
    /// <para>所有涉及 IO 的成员一律异步并以 <c>Async</c> 后缀结尾，与 <see cref="ISaveManager"/> 逐字对应。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// // 初始化（通常由启动流程经 SaveBootstrapStage 完成）
    /// SaveManager.Initialize();
    ///
    /// // 保存
    /// var meta = await SaveManager.SaveAsync(1);
    ///
    /// // 加载
    /// await SaveManager.LoadAsync(1);
    ///
    /// // 获取存档列表
    /// var metas = await SaveManager.GetSlotMetasAsync();
    /// foreach (var m in metas)
    ///     Debug.Log(m);
    /// </code>
    /// </example>
    public static class SaveManager
    {
        #region Fields

        private static ISaveManager _impl;

        #endregion

        #region Properties

        /// <summary>当前是否有已注入的实现。</summary>
        public static bool IsInitialized => _impl != null;

        /// <summary>是否有写操作（保存/加载/删除）正在进行。</summary>
        public static bool IsBusy
        {
            get
            {
                EnsureInitialized();
                return _impl.IsBusy;
            }
        }

        /// <summary>
        /// 当前操作玩家 ID。
        /// <para>为 <c>null</c> 时不启用玩家隔离，所有存档文件直接存放在 SaveData 根目录。</para>
        /// </summary>
        public static string CurrentPlayerId
        {
            get
            {
                EnsureInitialized();
                return _impl.CurrentPlayerId;
            }
        }

        #endregion

        #region Lifecycle

        /// <summary>
        /// 注入 <see cref="ISaveManager"/> 实现。
        /// <para>传入 <c>null</c> 时使用默认的 <see cref="SaveManagerImpl"/>。</para>
        /// <para>第三方可通过 <paramref name="factory"/> 传入自定义实现以接入其他存储后端。</para>
        /// </summary>
        /// <param name="factory">实现工厂委托。为 <c>null</c> 时使用默认实现。</param>
        /// <param name="options">初始化选项。为 <c>null</c> 时使用默认值（存档格式版本 1）。</param>
        public static void Initialize(SaveManagerFactory factory = null, SaveOptions options = null)
        {
            if (_impl != null)
            {
                Debug.LogWarning("[Save] SaveManager.Initialize 被重复调用，忽略重复注入。");
                return;
            }
            _impl = factory != null ? factory() : new SaveManagerImpl();

            if (options == null)
                return;

            _impl.SetCurrentVersion(options.CurrentVersion);

            // 加密接线放在初始化而非节点里：调用方直接 Initialize 时也应生效，
            // 否则「传了 CryptoProvider 却被静默忽略」会很难查。
            // 作用域限定 SaveData，避免连带加密 AppData / Cache（见 SaveOptions.CryptoProvider）
            if (options.CryptoProvider != null)
                FileManager.SetCryptoProvider(options.CryptoProvider, FileDomain.SaveData);
        }

        /// <summary>
        /// 注销当前实现，清空引用。
        /// </summary>
        public static void Shutdown()
        {
            _impl = null;
        }

        #endregion

        #region Internal

        /// <summary>
        /// 执行启动恢复扫描（内部使用，由 <see cref="SaveBootstrapStage"/> 在启动管线中调用）：
        /// 用一代备份恢复丢失的载荷、清掉崩溃残留、补齐元数据侧车。
        /// <para><b>不切回主线程</b>——全程只碰文件系统与线程安全的 <c>Debug.Log</c>，
        /// 因此启动管线中同步阻塞等待它也不会死锁。</para>
        /// <para>注入的自定义实现没有恢复契约，此时跳过（恢复属内部维护动作，不是接口义务）。</para>
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        internal static UniTask RecoverAsync(CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            if (_impl is SaveManagerImpl impl)
                return impl.RecoverAsync(cancellationToken);

            return UniTask.CompletedTask;
        }

        #endregion

        #region 存档格式版本

        /// <inheritdoc cref="ISaveManager.CurrentVersion"/>
        public static int CurrentVersion
        {
            get
            {
                EnsureInitialized();
                return _impl.CurrentVersion;
            }
        }

        /// <inheritdoc cref="ISaveManager.SetCurrentVersion"/>
        public static void SetCurrentVersion(int version)
        {
            EnsureInitialized();

            if (_impl.IsBusy)
                throw new InvalidOperationException("[Save] 当前有写操作正在进行，不允许切换存档格式版本。");

            _impl.SetCurrentVersion(version);
        }

        #endregion

        #region Player Context

        /// <summary>
        /// 设置当前操作玩家 ID。后续所有 Save/Load/Delete 操作均作用于此玩家。
        /// <para>当 <paramref name="playerId"/> 为 <c>null</c> 或空字符串时，等同于调用 <see cref="ClearCurrentPlayer"/>。</para>
        /// <para>如果当前有正在进行的写操作（<see cref="IsBusy"/> 为 <c>true</c>），调用此方法将抛出异常。</para>
        /// </summary>
        /// <param name="playerId">玩家 ID。</param>
        /// <exception cref="InvalidOperationException">当 <see cref="IsBusy"/> 为 <c>true</c> 时抛出。</exception>
        public static void SetCurrentPlayer(string playerId)
        {
            EnsureInitialized();

            if (_impl.IsBusy)
                throw new InvalidOperationException("[Save] 当前有保存/加载操作正在进行，不允许切换玩家。");

            if (string.IsNullOrEmpty(playerId))
            {
                ClearCurrentPlayer();
                return;
            }

            _impl.SetCurrentPlayer(playerId);
        }

        /// <summary>
        /// 清除玩家上下文，退回到无玩家隔离模式。
        /// <para>如果当前有正在进行的写操作（<see cref="IsBusy"/> 为 <c>true</c>），调用此方法将抛出异常。</para>
        /// </summary>
        /// <exception cref="InvalidOperationException">当 <see cref="IsBusy"/> 为 <c>true</c> 时抛出。</exception>
        public static void ClearCurrentPlayer()
        {
            EnsureInitialized();

            if (_impl.IsBusy)
                throw new InvalidOperationException("[Save] 当前有保存/加载操作正在进行，不允许清除玩家上下文。");

            _impl.ClearCurrentPlayer();
        }

        /// <inheritdoc cref="ISaveManager.GetAllPlayerIdsAsync"/>
        public static UniTask<string[]> GetAllPlayerIdsAsync(CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            return _impl.GetAllPlayerIdsAsync(cancellationToken);
        }

        /// <inheritdoc cref="ISaveManager.GetPlayerSlotMetasAsync(string, CancellationToken)"/>
        public static UniTask<List<SaveMeta>> GetPlayerSlotMetasAsync(string playerId, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            return _impl.GetPlayerSlotMetasAsync(playerId, cancellationToken);
        }

        /// <inheritdoc cref="ISaveManager.GetPlayerSlotMetasAsync(string, IProgress{SaveReport}, CancellationToken)"/>
        public static UniTask<List<SaveMeta>> GetPlayerSlotMetasAsync(string playerId, IProgress<SaveReport> progress, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            return _impl.GetPlayerSlotMetasAsync(playerId, progress, cancellationToken);
        }

        /// <inheritdoc cref="ISaveManager.DeletePlayerAsync(string, CancellationToken)"/>
        public static UniTask<int> DeletePlayerAsync(string playerId, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            return _impl.DeletePlayerAsync(playerId, cancellationToken);
        }

        /// <inheritdoc cref="ISaveManager.DeletePlayerAsync(string, IProgress{SaveReport}, CancellationToken)"/>
        public static UniTask<int> DeletePlayerAsync(string playerId, IProgress<SaveReport> progress, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            return _impl.DeletePlayerAsync(playerId, progress, cancellationToken);
        }

        #endregion

        #region Public API

        /// <inheritdoc cref="ISaveManager.GetSlotMetasAsync(CancellationToken)"/>
        public static UniTask<List<SaveMeta>> GetSlotMetasAsync(CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            return _impl.GetSlotMetasAsync(cancellationToken);
        }

        /// <inheritdoc cref="ISaveManager.GetSlotMetasAsync(IProgress{SaveReport}, CancellationToken)"/>
        public static UniTask<List<SaveMeta>> GetSlotMetasAsync(IProgress<SaveReport> progress, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            return _impl.GetSlotMetasAsync(progress, cancellationToken);
        }

        /// <inheritdoc cref="ISaveManager.GetSlotMetaAsync"/>
        public static UniTask<SaveMeta> GetSlotMetaAsync(int slot, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            return _impl.GetSlotMetaAsync(slot, cancellationToken);
        }

        /// <inheritdoc cref="ISaveManager.SaveAsync"/>
        public static UniTask<SaveMeta> SaveAsync(int slot, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            return _impl.SaveAsync(slot, cancellationToken);
        }

        /// <inheritdoc cref="ISaveManager.LoadAsync"/>
        public static UniTask LoadAsync(int slot, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            return _impl.LoadAsync(slot, cancellationToken);
        }

        /// <inheritdoc cref="ISaveManager.TryLoadAsync"/>
        public static UniTask<SaveLoadResult> TryLoadAsync(int slot, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            return _impl.TryLoadAsync(slot, cancellationToken);
        }

        /// <inheritdoc cref="ISaveManager.DeleteSlotAsync"/>
        public static UniTask<bool> DeleteSlotAsync(int slot, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            return _impl.DeleteSlotAsync(slot, cancellationToken);
        }

        /// <inheritdoc cref="ISaveManager.DeleteAllSlotsAsync(CancellationToken)"/>
        public static UniTask<int> DeleteAllSlotsAsync(CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            return _impl.DeleteAllSlotsAsync(cancellationToken);
        }

        /// <inheritdoc cref="ISaveManager.DeleteAllSlotsAsync(IProgress{SaveReport}, CancellationToken)"/>
        public static UniTask<int> DeleteAllSlotsAsync(IProgress<SaveReport> progress, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            return _impl.DeleteAllSlotsAsync(progress, cancellationToken);
        }

        /// <inheritdoc cref="ISaveManager.CopySlotAsync"/>
        public static UniTask<SaveMeta> CopySlotAsync(int fromSlot, int toSlot, bool overwrite = false, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            return _impl.CopySlotAsync(fromSlot, toSlot, overwrite, cancellationToken);
        }

        /// <inheritdoc cref="ISaveManager.MoveSlotAsync"/>
        public static UniTask<SaveMeta> MoveSlotAsync(int fromSlot, int toSlot, bool overwrite = false, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            return _impl.MoveSlotAsync(fromSlot, toSlot, overwrite, cancellationToken);
        }

        /// <inheritdoc cref="ISaveManager.SlotExistsAsync"/>
        public static UniTask<bool> SlotExistsAsync(int slot, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            return _impl.SlotExistsAsync(slot, cancellationToken);
        }

        #endregion

        #region Internal

        private static void EnsureInitialized()
        {
            if (_impl == null)
                throw new InvalidOperationException(
                    "[Save] SaveManager 尚未初始化。请登记 SaveBootstrapStage（Bootstrap.RegisterDefaults() 已包含），或手动调用 SaveManager.Initialize()。");
        }

        #endregion
    }
}
