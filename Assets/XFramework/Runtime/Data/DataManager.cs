using System.Collections.Generic;
using UnityEngine;

namespace XFramework.XData
{
    /// <summary>
    /// 运行时数据模块的静态门面。
    /// <para>由 <see cref="GameDataNode"/> 在加载管线 LoadAsync 阶段创建 <see cref="DataManagerImpl"/> 并注入，
    /// 外部业务代码通过本类静态方法访问。</para>
    /// <para>使用前必须调用 <see cref="Initialize"/>（或由 GameDataNode 自动调用）。</para>
    /// <para>数据按 <see cref="IDataBlock"/>（GamePlay 模块）组织。</para>
    /// <para>存读档职责由 Save 模块（XFramework.XSave）负责，本类仅暴露 <see cref="CreateSnapshot"/> / <see cref="ApplySnapshot"/> 序列化接口。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// // 在节点树中挂载 GameDataNode 即可自动完成初始化。
    /// // 业务代码直接使用静态调用：
    /// var bag = DataManager.GetOrCreateBlock<BagData>();
    /// bag.Items.Add(new BagItem { id = 1001, count = 1 });
    /// bag.Gold += 100;
    /// var snapshot = DataManager.CreateSnapshot(); // 供 Save 模块持久化
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
        /// <para>已注入非 null 实现时重复调用输出警告并忽略。</para>
        /// </summary>
        public static void Initialize(IDataManager impl)
        {
            if (impl != null && _impl != null)
            {
                Debug.LogWarning("[Data] DataManager.Initialize 被重复调用，忽略重复注入。");
                return;
            }
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

        #region Block

        /// <inheritdoc cref="IDataManager.GetOrCreateBlock{T}"/>
        public static T GetOrCreateBlock<T>() where T : class, IDataBlock, new()
        {
            EnsureInitialized();
            return _impl.GetOrCreateBlock<T>();
        }

        /// <inheritdoc cref="IDataManager.TryGetBlock{T}"/>
        public static bool TryGetBlock<T>(out T block) where T : class, IDataBlock
        {
            EnsureInitialized();
            return _impl.TryGetBlock(out block);
        }

        /// <inheritdoc cref="IDataManager.RegisterBlock{T}"/>
        public static void RegisterBlock<T>(T block) where T : class, IDataBlock
        {
            EnsureInitialized();
            _impl.RegisterBlock(block);
        }

        /// <inheritdoc cref="IDataManager.RemoveBlock{T}"/>
        public static bool RemoveBlock<T>() where T : class, IDataBlock
        {
            EnsureInitialized();
            return _impl.RemoveBlock<T>();
        }

        /// <inheritdoc cref="IDataManager.HasBlock{T}"/>
        public static bool HasBlock<T>() where T : class, IDataBlock
        {
            EnsureInitialized();
            return _impl.HasBlock<T>();
        }

        #endregion

        #region Snapshot

        /// <inheritdoc cref="IDataManager.CreateSnapshot"/>
        public static DataSnapshot CreateSnapshot()
        {
            EnsureInitialized();
            return _impl.CreateSnapshot();
        }

        /// <inheritdoc cref="IDataManager.ApplySnapshot"/>
        public static void ApplySnapshot(DataSnapshot data)
        {
            EnsureInitialized();
            _impl.ApplySnapshot(data);
        }

        #endregion

        #region Dirty

        /// <inheritdoc cref="IDataManager.MarkDirty{T}"/>
        public static void MarkDirty<T>() where T : class, IDataBlock
        {
            EnsureInitialized();
            _impl.MarkDirty<T>();
        }

        /// <inheritdoc cref="IDataManager.IsDirty{T}"/>
        public static bool IsDirty<T>() where T : class, IDataBlock
        {
            EnsureInitialized();
            return _impl.IsDirty<T>();
        }

        /// <inheritdoc cref="IDataManager.HasDirtyBlocks"/>
        public static bool HasDirtyBlocks
        {
            get
            {
                EnsureInitialized();
                return _impl.HasDirtyBlocks;
            }
        }

        /// <inheritdoc cref="IDataManager.GetDirtyBlocks"/>
        public static List<IDataBlock> GetDirtyBlocks()
        {
            EnsureInitialized();
            return _impl.GetDirtyBlocks();
        }

        #endregion

        #region BlockSnapshot

        /// <inheritdoc cref="IDataManager.CreateBlockSnapshot{T}"/>
        public static DataBlockSnapshot CreateBlockSnapshot<T>() where T : class, IDataBlock
        {
            EnsureInitialized();
            return _impl.CreateBlockSnapshot<T>();
        }

        /// <inheritdoc cref="IDataManager.ApplyBlockSnapshot"/>
        public static bool ApplyBlockSnapshot(DataBlockSnapshot snap)
        {
            EnsureInitialized();
            return _impl.ApplyBlockSnapshot(snap);
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
                    "DataManager 尚未初始化。请确认节点树中已挂载 GameDataNode（其加载阶段会自动注入），或手动调用 DataManager.Initialize(impl)。");
        }

        #endregion
    }
}