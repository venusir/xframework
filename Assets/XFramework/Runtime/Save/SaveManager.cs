using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XSave
{
    /// <summary>
    /// 存档管理器静态门面。
    /// <para>第三方业务代码通过本类静态方法进行存档操作，
    /// 内部实现可替换（默认 <see cref="SaveManagerImpl"/>）。</para>
    /// <para>使用前必须调用 <see cref="Initialize"/> 注入实现。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// // 初始化（通常由节点树自动完成）
    /// SaveManager.Initialize();
    ///
    /// // 保存
    /// var meta = await SaveManager.SaveAsync(1);
    ///
    /// // 加载
    /// await SaveManager.LoadAsync(1);
    ///
    /// // 获取存档列表
    /// var metas = await SaveManager.GetSlotMetas();
    /// foreach (var m in metas)
    ///     Debug.Log(m);
    /// </code>
    /// </example>
    public static class SaveManager
    {
        #region Fields

        private static ISaveManager _impl;
        private static string _currentUserId;

        #endregion

        #region Properties

        /// <summary>当前是否有已注入的实现。</summary>
        public static bool IsInitialized => _impl != null;

        /// <summary>是否已在加载/保存操作中。</summary>
        public static bool IsBusy
        {
            get
            {
                EnsureInitialized();
                return _impl.IsBusy;
            }
        }

        /// <summary>
        /// 当前操作的用户 ID。
        /// <para>为 <c>null</c> 时不启用用户隔离，所有存档文件直接存放在 SaveData 根目录。</para>
        /// </summary>
        public static string CurrentUserId => _currentUserId;

        #endregion

        #region Lifecycle

        /// <summary>
        /// 注入 <see cref="ISaveManager"/> 实现。
        /// <para>传入 <c>null</c> 时使用默认的 <see cref="SaveManagerImpl"/>。</para>
        /// <para>第三方可通过 <paramref name="factory"/> 传入自定义实现以接入其他存储后端。</para>
        /// </summary>
        /// <param name="factory">实现工厂委托。为 <c>null</c> 时使用默认实现。</param>
        public static void Initialize(SaveManagerFactory factory = null)
        {
            _impl = factory != null ? factory() : new SaveManagerImpl();
        }

        /// <summary>
        /// 注销当前实现，清空引用。
        /// </summary>
        public static void Shutdown()
        {
            _impl = null;
            _currentUserId = null;
        }

        #endregion

        #region User Context

        /// <summary>
        /// 设置当前操作用户 ID。后续所有 Save/Load/Delete 操作均作用于此用户。
        /// <para>当 <paramref name="userId"/> 为 <c>null</c> 或空字符串时，等同于调用 <see cref="ClearCurrentUser"/>。</para>
        /// <para>如果当前有正在进行的保存/加载操作（<see cref="IsBusy"/> 为 <c>true</c>），调用此方法将抛出异常。</para>
        /// </summary>
        /// <exception cref="InvalidOperationException">当 <see cref="IsBusy"/> 为 <c>true</c> 时抛出。</exception>
        public static void SetCurrentUser(string userId)
        {
            EnsureInitialized();

            if (_impl.IsBusy)
                throw new InvalidOperationException("[Save] 当前有保存/加载操作正在进行，不允许切换用户。");

            if (string.IsNullOrEmpty(userId))
            {
                ClearCurrentUser();
                return;
            }

            _currentUserId = userId;
            if (_impl is SaveManagerImpl impl)
                impl.SetUserId(userId);
        }

        /// <summary>
        /// 清除用户上下文，退回到无用户隔离模式。
        /// <para>如果当前有正在进行的保存/加载操作（<see cref="IsBusy"/> 为 <c>true</c>），调用此方法将抛出异常。</para>
        /// </summary>
        /// <exception cref="InvalidOperationException">当 <see cref="IsBusy"/> 为 <c>true</c> 时抛出。</exception>
        public static void ClearCurrentUser()
        {
            EnsureInitialized();

            if (_impl.IsBusy)
                throw new InvalidOperationException("[Save] 当前有保存/加载操作正在进行，不允许清除用户上下文。");

            _currentUserId = null;
            if (_impl is SaveManagerImpl impl)
                impl.ClearUserId();
        }

        /// <summary>
        /// 获取所有存在存档数据的用户 ID 列表。
        /// <para>不会切换当前用户上下文。</para>
        /// </summary>
        /// <returns>用户 ID 数组，无用户数据时返回空数组。</returns>
        public static UniTask<string[]> GetAllUserIds(CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            if (_impl is SaveManagerImpl impl)
                return impl.GetAllUserIdsAsync(cancellationToken);

            return UniTask.FromResult(Array.Empty<string>());
        }

        /// <summary>
        /// 获取指定用户的存档槽位列表。
        /// <para>不会切换当前用户上下文。</para>
        /// </summary>
        /// <param name="userId">要查询的用户 ID。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>该用户的存档元数据列表。</returns>
        public static async UniTask<List<SaveMeta>> GetUserSlotMetas(string userId, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            if (_impl is SaveManagerImpl impl)
            {
                var previousUserId = _currentUserId;
                try
                {
                    // 临时切换用户上下文以获取指定用户的槽位列表
                    impl.SetUserId(userId);
                    var metas = await impl.GetSlotMetas(cancellationToken);
                    // 修正 userId（impl 填充的是临时 userId，需要与传入参数一致）
                    for (int i = 0; i < metas.Count; i++)
                        metas[i].userId = userId;
                    return metas;
                }
                finally
                {
                    impl.SetUserId(previousUserId);
                }
            }

            return new List<SaveMeta>();
        }

        /// <summary>
        /// 删除指定用户的所有存档数据。
        /// <para>不会切换当前用户上下文。</para>
        /// </summary>
        /// <param name="userId">要删除的用户 ID。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public static async UniTask DeleteUser(string userId, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            if (_impl is SaveManagerImpl impl)
                await impl.DeleteUserAsync(userId, cancellationToken);
        }

        #endregion

        #region Public API

        /// <inheritdoc cref="ISaveManager.GetSlotMetas"/>
        public static UniTask<List<SaveMeta>> GetSlotMetas(CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            return _impl.GetSlotMetas(cancellationToken);
        }

        /// <inheritdoc cref="ISaveManager.GetSlotMeta"/>
        public static UniTask<SaveMeta> GetSlotMeta(int slot, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            return _impl.GetSlotMeta(slot, cancellationToken);
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

        /// <inheritdoc cref="ISaveManager.DeleteSlot"/>
        public static void DeleteSlot(int slot)
        {
            EnsureInitialized();
            _impl.DeleteSlot(slot);
        }

        /// <inheritdoc cref="ISaveManager.DeleteAllSlotsAsync"/>
        public static UniTask DeleteAllSlots(CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            return _impl.DeleteAllSlotsAsync(cancellationToken);
        }

        /// <inheritdoc cref="ISaveManager.SlotExists"/>
        public static bool SlotExists(int slot)
        {
            EnsureInitialized();
            return _impl.SlotExists(slot);
        }

        #endregion

        #region Internal

        private static void EnsureInitialized()
        {
            if (_impl == null)
                throw new InvalidOperationException(
                    "[Save] SaveManager 尚未初始化。请确认节点树中已挂载 SaveBootstrapNode，或手动调用 SaveManager.Initialize()。");
        }

        #endregion
    }
}