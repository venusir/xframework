using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XData
{
    /// <summary>
    /// 运行时数据管理器公共接口。
    /// <para>管理 Table（多行，按主键索引）和 Global（全局单例）两类运行时可变数据。</para>
    /// <para>支持本地存档的保存/加载，内置 <see cref="JsonFileDataStore"/> 默认实现。</para>
    /// </summary>
    public interface IDataManager
    {
        #region Table

        /// <summary>
        /// 获取或自动创建指定类型的 DataTable。
        /// <para>首次调用时自动创建空表，后续直接返回已有实例。</para>
        /// </summary>
        /// <typeparam name="T">数据行类型，需实现 <see cref="IDataRow{TKey}"/> 并有无参构造函数。</typeparam>
        /// <returns><see cref="DataTable{T}"/> 包装器实例。</returns>
        DataTable<T> GetOrCreateTable<T>() where T : IDataRow, new();

        /// <summary>
        /// 安全获取已创建的 DataTable。未创建时返回 <c>false</c>。
        /// </summary>
        bool TryGetTable<T>(out DataTable<T> table) where T : IDataRow;

        /// <summary>
        /// 注册已创建的 DataTable。
        /// </summary>
        void RegisterTable<T>(DataTable<T> table) where T : IDataRow;

        /// <summary>
        /// 移除并清空指定类型的 DataTable。
        /// </summary>
        bool RemoveTable<T>() where T : IDataRow;

        /// <summary>
        /// 判断指定类型的 DataTable 是否已创建。
        /// </summary>
        bool HasTable<T>();

        #endregion

        #region Global

        /// <summary>
        /// 获取或自动创建 Global 类型单例。
        /// </summary>
        /// <typeparam name="T">Global 类型，需为 class 并有无参构造函数。</typeparam>
        /// <returns>Global 实例。</returns>
        T GetOrCreateGlobal<T>() where T : class, new();

        /// <summary>
        /// 安全获取 Global 单例。
        /// </summary>
        bool TryGetGlobal<T>(out T global) where T : class;

        /// <summary>
        /// 注册 Global 实例（可注册已有实例，如从存档恢复）。
        /// </summary>
        void RegisterGlobal<T>(T global) where T : class;

        /// <summary>
        /// 移除 Global 单例。
        /// </summary>
        bool RemoveGlobal<T>() where T : class;

        /// <summary>
        /// 判断 Global 单例是否已注册。
        /// </summary>
        bool HasGlobal<T>();

        #endregion

        #region Save / Load

        /// <summary>
        /// 异步保存所有已注册的 DataTable 和 Global 数据。
        /// <para>需先通过 <see cref="SetStore"/> 设置存储后端。</para>
        /// </summary>
        /// <param name="name">存档名称（如 "autosave", "slot1"）。</param>
        /// <param name="ct">取消令牌。</param>
        UniTask SaveAsync(string name, CancellationToken ct = default);

        /// <summary>
        /// 异步从存储加载存档，恢复所有 DataTable 和 Global 数据。
        /// <para>加载前会清空现有数据。</para>
        /// </summary>
        /// <param name="name">存档名称。</param>
        /// <param name="ct">取消令牌。</param>
        UniTask LoadAsync(string name, CancellationToken ct = default);

        /// <summary>
        /// 删除指定存档。
        /// </summary>
        void DeleteSave(string name);

        /// <summary>
        /// 判断指定存档是否存在。
        /// </summary>
        bool HasSave(string name);

        /// <summary>
        /// 设置数据存储后端。
        /// <para>默认提供 <see cref="JsonFileDataStore"/>（JsonUtility 本地文件），
        /// 也可传入自定义实现（如 Protobuf、云端存储）。</para>
        /// </summary>
        void SetStore(IDataStore store);

        #endregion

        #region Clear

        /// <summary>
        /// 清空所有已注册的 DataTable 和 Global 数据。
        /// </summary>
        void ClearAll();

        #endregion
    }
}