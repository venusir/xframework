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