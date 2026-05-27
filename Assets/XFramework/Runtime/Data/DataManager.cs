using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XData
{
    /// <summary>
    /// 运行时数据模块的静态门面。
    /// <para>由 <see cref="GameDataNode"/> 在 Awake 时创建 <see cref="DataManagerImpl"/> 并注入，
    /// 外部业务代码通过本类静态方法访问。</para>
    /// <para>使用前必须调用 <see cref="Initialize"/>（或由 GameDataNode 自动调用）。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// // 在节点树中挂载 GameDataNode 即可自动完成初始化。
    /// // 业务代码直接使用静态调用：
    /// var table = DataManager.GetOrCreateTable<PlayerData>();
    /// var player = table.Get("player_001");
    /// player.hp -= 10;
    /// await DataManager.SaveAsync("autosave");
    /// </code>
    /// </example>
    public static class DataManager
    {
        private static IDataManager _impl;

        /// <summary>当前是否有已注入的实现。</summary>
        public static bool IsInitialized => _impl != null;

        #region Lifecycle

        /// <summary>
        /// 注入 IDataManager 实现（由 GameDataNode 自动调用）。
        /// <para>传入 null 等效于调用 <see cref="Shutdown"/>。</para>
        /// </summary>
        public static void Initialize(IDataManager impl)
        {
            _impl = impl;
        }

        /// <summary>
        /// 注销当前实现，清空引用。
        /// </summary>
        public static void Shutdown()
        {
            _impl = null;
        }

        #endregion

        #region Table

        /// <inheritdoc cref="IDataManager.GetOrCreateTable{T}"/>
        public static DataTable<T> GetOrCreateTable<T>() where T : IDataRow, new()
        {
            EnsureInitialized();
            return _impl.GetOrCreateTable<T>();
        }

        /// <inheritdoc cref="IDataManager.TryGetTable{T}"/>
        public static bool TryGetTable<T>(out DataTable<T> table) where T : IDataRow
        {
            EnsureInitialized();
            return _impl.TryGetTable(out table);
        }

        /// <inheritdoc cref="IDataManager.RegisterTable{T}"/>
        public static void RegisterTable<T>(DataTable<T> table) where T : IDataRow
        {
            EnsureInitialized();
            _impl.RegisterTable(table);
        }

        /// <inheritdoc cref="IDataManager.RemoveTable{T}"/>
        public static bool RemoveTable<T>() where T : IDataRow
        {
            EnsureInitialized();
            return _impl.RemoveTable<T>();
        }

        /// <inheritdoc cref="IDataManager.HasTable{T}"/>
        public static bool HasTable<T>()
        {
            EnsureInitialized();
            return _impl.HasTable<T>();
        }

        #endregion

        #region Global

        /// <inheritdoc cref="IDataManager.GetOrCreateGlobal{T}"/>
        public static T GetOrCreateGlobal<T>() where T : class, new()
        {
            EnsureInitialized();
            return _impl.GetOrCreateGlobal<T>();
        }

        /// <inheritdoc cref="IDataManager.TryGetGlobal{T}"/>
        public static bool TryGetGlobal<T>(out T global) where T : class
        {
            EnsureInitialized();
            return _impl.TryGetGlobal(out global);
        }

        /// <inheritdoc cref="IDataManager.RegisterGlobal{T}"/>
        public static void RegisterGlobal<T>(T global) where T : class
        {
            EnsureInitialized();
            _impl.RegisterGlobal(global);
        }

        /// <inheritdoc cref="IDataManager.RemoveGlobal{T}"/>
        public static bool RemoveGlobal<T>() where T : class
        {
            EnsureInitialized();
            return _impl.RemoveGlobal<T>();
        }

        /// <inheritdoc cref="IDataManager.HasGlobal{T}"/>
        public static bool HasGlobal<T>()
        {
            EnsureInitialized();
            return _impl.HasGlobal<T>();
        }

        #endregion

        #region Save / Load

        /// <inheritdoc cref="IDataManager.SaveAsync"/>
        public static UniTask SaveAsync(string name, CancellationToken ct = default)
        {
            EnsureInitialized();
            return _impl.SaveAsync(name, ct);
        }

        /// <inheritdoc cref="IDataManager.LoadAsync"/>
        public static UniTask LoadAsync(string name, CancellationToken ct = default)
        {
            EnsureInitialized();
            return _impl.LoadAsync(name, ct);
        }

        /// <inheritdoc cref="IDataManager.DeleteSave"/>
        public static void DeleteSave(string name)
        {
            EnsureInitialized();
            _impl.DeleteSave(name);
        }

        /// <inheritdoc cref="IDataManager.HasSave"/>
        public static bool HasSave(string name)
        {
            EnsureInitialized();
            return _impl.HasSave(name);
        }

        /// <inheritdoc cref="IDataManager.SetStore"/>
        public static void SetStore(IDataStore store)
        {
            EnsureInitialized();
            _impl.SetStore(store);
        }

        #endregion

        #region Clear

        /// <inheritdoc cref="IDataManager.ClearAll"/>
        public static void ClearAll()
        {
            EnsureInitialized();
            _impl.ClearAll();
        }

        #endregion

        #region Internal

        private static void EnsureInitialized()
        {
            if (_impl == null)
                throw new DataException(
                    "DataManager not initialized. Ensure GameDataNode is present in scene.");
        }

        #endregion
    }
}