using System;
using System.Collections.Generic;

namespace XFramework.XData
{
    /// <summary>
    /// 运行时数据管理器的内部接口，定义 Block 管理 / 数据快照 / 脏标记能力。
    /// <para>外部业务代码通过 <see cref="DataManager"/> 静态门面访问。</para>
    /// <para>存读档职责由 Save 模块（XFramework.XSave）负责，DataManager 仅提供 <see cref="DataSnapshot"/> 序列化/反序列化接口。</para>
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
        /// <para><b>键是静态类型 <c>typeof(T)</c></b>：请用块的具体类型注册；用基类/接口类型注册会让
        /// <see cref="GetOrCreateBlock{T}"/> / <see cref="TryGetBlock{T}"/> / <see cref="HasBlock{T}"/>
        /// 以具体类型为键时查不到（R3-DATA-005）。</para>
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
        /// 遍历所有已注册的 <see cref="IDataBlock"/>，调用 <see cref="IDataBlock.OnSave"/> 构建 <see cref="DataSnapshot"/> 快照。
        /// <para>序列化委托给 <see cref="XSerialize.Serializer"/>，返回值由 Save 模块负责写入到存储后端。
        /// 导出成功后清空全部脏标记（导出即视为已保存）。</para>
        /// <para><b>快照同时记下这一刻的脏集合</b>（进程内回滚元数据，不随存档持久化），
        /// 供「留回滚点 → 加载失败 → 回滚」把数据与脏集合一起还原——见 <see cref="ApplySnapshot(DataSnapshot)"/>。</para>
        /// </summary>
        DataSnapshot CreateSnapshot();

        /// <summary>
        /// 将 <see cref="DataSnapshot"/> 快照恢复到当前内存数据中。
        /// <para>反序列化委托给 <see cref="XSerialize.Serializer"/>，加载前会清空现有数据。</para>
        /// <para><b>脏标记按快照携带的集合还原</b>：不携带者（从磁盘读回、手工构造）一律清空——「恢复即干净」；
        /// 携带者（同进程内由 <see cref="CreateSnapshot"/> 产出的回滚点）则还原到生成快照那一刻的脏集合。
        /// 因此「留回滚点 → 应用别的快照失败 → 应用回滚点」之后，加载前的未保存改动仍是「未保存」。</para>
        /// </summary>
        /// <param name="data">要应用的快照。</param>
        /// <returns>
        /// <b>未能恢复的数据块数量</b>；<c>0</c> 表示全部恢复成功。
        /// <para>单个块失败（清空阶段 <see cref="IDataBlock.OnClear"/> 抛异常、快照条目为 <c>null</c>、
        /// 快照缺 blockName、块未注册、格式不支持、块版本高于当前代码、反序列化或
        /// <see cref="IDataBlock.OnLoad"/> 抛异常）不会中断整体恢复，而是被跳过并计入此值——
        /// 调用方必须据此判断本次加载是否<b>完整</b>：「部分块未恢复」等价于内存里少了一半数据，
        /// 当作成功处理是危险的。</para>
        /// </returns>
        int ApplySnapshot(DataSnapshot data);

        #endregion

        #region Dirty

        /// <summary>
        /// 标记指定类型的数据块为「已修改，需要保存」。
        /// <para>数据修改后显式调用；Save 模块可结合 <see cref="GetDirtyBlocks"/> 实现增量保存。</para>
        /// <para>未注册的 Block 输出警告并忽略。</para>
        /// </summary>
        void MarkDirty<T>() where T : class, IDataBlock;

        /// <summary>
        /// 判断指定类型的数据块是否被标记为已修改。
        /// <para>未注册的 Block 返回 <c>false</c>。</para>
        /// </summary>
        bool IsDirty<T>() where T : class, IDataBlock;

        /// <summary>
        /// 是否存在被标记为已修改的数据块。
        /// </summary>
        bool HasDirtyBlocks { get; }

        /// <summary>
        /// 获取所有被标记为已修改的数据块。
        /// <para>返回新建列表，调用方持有遍历；快照导出（<see cref="CreateSnapshot"/>）成功后脏标记被清空，先前获取的列表随之失效。</para>
        /// </summary>
        List<IDataBlock> GetDirtyBlocks();

        #endregion

        #region BlockSnapshot

        /// <summary>
        /// 创建单个 <see cref="IDataBlock"/> 的快照（增量保存用，不清空脏标记）。
        /// <para>未注册的 Block 输出警告并返回 <c>null</c>；
        /// <see cref="IDataBlock.OnSave"/> 返回 null 时同样返回 <c>null</c>（不警告）。</para>
        /// </summary>
        DataBlockSnapshot CreateBlockSnapshot<T>() where T : class, IDataBlock;

        /// <summary>
        /// 从单个快照恢复指定数据块（不清空其他 Block）。
        /// <para>未知 blockName 输出警告并返回 <c>false</c>（不创建实例）；
        /// 恢复前清空目标块数据，恢复管线与 <see cref="ApplySnapshot"/> 一致（版本迁移、saveType 回退）。</para>
        /// <para>恢复成功后清除该块的脏标记，失败保留。</para>
        /// </summary>
        bool ApplyBlockSnapshot(DataBlockSnapshot snap);

        #endregion

        #region Clear

        /// <summary>
        /// 清空所有已注册的 Block 数据（触发每个 Block 的 <see cref="IDataBlock.OnClear"/>）。
        /// </summary>
        void ClearAll();

        #endregion
    }
}
