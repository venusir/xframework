using System;
using System.Collections.Generic;

namespace XFramework
{
    /// <summary>
    /// 全局逻辑锁系统静态入口。提供与节点树无关的公共锁管理 API。
    /// <para>所有方法委托到 <see cref="Instance"/>，需先调用 <see cref="Initialize"/> 初始化。</para>
    /// <para>使用示例:</para>
    /// <code>
    /// LockSystem.Initialize();
    /// using (LockSystem.Acquire(LockType.Input, myLock))
    /// {
    ///     // 锁定输入期间的操作
    /// }
    /// using (LockSystem.Acquire(player, LockType.Update, playerLock))
    /// {
    ///     // 针对 player 的 Update 锁
    /// }
    /// if (LockSystem.IsLocked(LockType.Update))
    ///     return;
    /// </code>
    /// </summary>
    public static class LockSystem
    {
        #region Private Fields

        private static ILockService _instance;
        private static bool _initialized;

        #endregion

        #region Public Properties

        /// <summary>
        /// 全局逻辑锁服务实例。未初始化时访问会抛出 <see cref="InvalidOperationException"/>。
        /// </summary>
        public static ILockService Instance
        {
            get
            {
                if (!_initialized || _instance == null)
                    throw new InvalidOperationException(
                        "LockSystem is not initialized. Call LockSystem.Initialize() first.");
                return _instance;
            }
        }

        /// <summary>
        /// 是否已初始化。
        /// </summary>
        public static bool IsInitialized => _initialized && _instance != null;

        #endregion

        #region Initialize

        /// <summary>
        /// 初始化全局逻辑锁系统。
        /// <para>通常在 <see cref="GameLauncher"/> 中显式调用。</para>
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;

            _instance = new LockService();
            _initialized = true;
        }

        /// <summary>
        /// 使用外部已创建的 <see cref="ILockService"/> 实例设置为全局服务。
        /// <para>适用于依赖注入或单元测试场景。</para>
        /// </summary>
        /// <param name="service">已创建的逻辑锁服务实例。</param>
        public static void SetInstance(ILockService service)
        {
            _instance = service ?? throw new ArgumentNullException(nameof(service));
            _initialized = true;
        }

        /// <summary>
        /// 销毁全局逻辑锁系统，释放所有资源。
        /// </summary>
        public static void Destroy()
        {
            if (_instance != null)
            {
                _instance.Dispose();
                _instance = null;
            }
            _initialized = false;
        }

        #endregion

        #region Static Delegates - Acquire

        /// <inheritdoc cref="ILockService.Acquire(int, object)"/>
        public static LockHandle Acquire(int lockType, object lockObj)
            => Instance.Acquire(lockType, lockObj);

        /// <inheritdoc cref="ILockService.Acquire(object, int, object)"/>
        public static LockHandle Acquire(object lockSubject, int lockType, object lockObj)
            => Instance.Acquire(lockSubject, lockType, lockObj);

        #endregion

        #region Static Delegates - Release

        /// <inheritdoc cref="ILockService.Release(int, object)"/>
        public static void Release(int lockType, object lockObj)
            => Instance.Release(lockType, lockObj);

        /// <inheritdoc cref="ILockService.Release(object, int, object)"/>
        public static void Release(object lockSubject, int lockType, object lockObj)
            => Instance.Release(lockSubject, lockType, lockObj);

        #endregion

        #region Static Delegates - Query

        /// <inheritdoc cref="ILockService.IsLocked(int)"/>
        public static bool IsLocked(int lockType)
            => Instance.IsLocked(lockType);

        /// <inheritdoc cref="ILockService.IsLocked(object, int)"/>
        public static bool IsLocked(object lockSubject, int lockType)
            => Instance.IsLocked(lockSubject, lockType);

        /// <inheritdoc cref="ILockService.GetLockCount(int)"/>
        public static int GetLockCount(int lockType)
            => Instance.GetLockCount(lockType);

        /// <inheritdoc cref="ILockService.GetLockObjects(int)"/>
        public static IReadOnlyList<object> GetLockObjects(int lockType)
            => Instance.GetLockObjects(lockType);

        #endregion

        #region Static Delegates - Events

        /// <inheritdoc cref="ILockService.OnLocked"/>
        public static event Action<object, int, object> OnLocked
        {
            add => Instance.OnLocked += value;
            remove => Instance.OnLocked -= value;
        }

        /// <inheritdoc cref="ILockService.OnUnlocked"/>
        public static event Action<object, int, object> OnUnlocked
        {
            add => Instance.OnUnlocked += value;
            remove => Instance.OnUnlocked -= value;
        }

        #endregion
    }
}
