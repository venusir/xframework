using System;

namespace XFramework.XData
{
    /// <summary>
    /// 运行时数据管理器的内部接口，定义 Block 管理 / 数据快照能力。
    /// <para>外部业务代码通过 <see cref="DataManager"/> 静态门面访问。</para>
    /// <para>存读档职责由 SaveLoadModule 负责，DataManager 仅提供 <see cref="SaveData"/> 序列化/反序列化接口。</para>
    /// </summary>
    public interface IDataManager
    {
        #region Block

        /// <summary>
        /// 获取或自动创建指定类型的数据块。
        /// <para>首次调用时通过无参构造函数自动创建并注册，后续直接返回已有实例。</para>
        /// </summary>
        /// <typeparam name="T">数据块类型，需实现 <see cref="IDataBlock"/> 并有无参构造函数。</typeparam>
        T GetOrCreateBlock<T>() where T : class, IDataBlock, new();

        /// <summary>
        /// 安全获取已创建的数据块。未创建时返回 <c>false</c>。
        /// </summary>
        bool TryGetBlock<T>(out T block) where T : class, IDataBlock;

        /// <summary>
        /// 注册已创建的数据块实例。
        /// </summary>
        void RegisterBlock<T>(T block) where T : class, IDataBlock;

        /// <summary>
        /// 移除并清空指定类型的数据块。
        /// </summary>
        bool RemoveBlock<T>() where T : class, IDataBlock;

        /// <summary>
        /// 判断指定类型的数据块是否已注册。
        /// </summary>
        bool HasBlock<T>() where T : class, IDataBlock;

        /// <summary>
        /// 遍历当前所有已注册的 <see cref="IDataBlock"/> 并执行指定操作。
        /// <para>仅在存档、清空等低频路径使用。</para>
        /// </summary>
        void ForEachBlock(Action<IDataBlock> action);

        #endregion

        #region Snapshot

        /// <summary>
        /// 遍历所有已注册的 <see cref="IDataBlock"/>，调用 <see cref="IDataBlock.OnSave"/> 构建 <see cref="SaveData"/> 快照。
        /// <para>序列化委托给 <see cref="XSerialize.Serializer"/>，返回值由 SaveLoadModule 负责写入到存储后端。</para>
        /// </summary>
        SaveData CreateSnapshot();

        /// <summary>
        /// 将 <see cref="SaveData"/> 快照恢复到当前内存数据中。
        /// <para>反序列化委托给 <see cref="XSerialize.Serializer"/>，加载前会清空现有数据。</para>
        /// </summary>
        void ApplySnapshot(SaveData data);

        #endregion

        #region Clear

        /// <summary>
        /// 清空所有已注册的 Block 数据（触发每个 Block 的 <see cref="IDataBlock.OnClear"/>）。
        /// </summary>
        void ClearAll();

        #endregion
    }
}
