using System;
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
    /// </summary>
    public interface ISaveManager
    {
        /// <summary>是否已在加载/保存操作中。</summary>
        bool IsBusy { get; }

        /// <summary>获取所有存档槽位的元数据列表。</summary>
        /// <returns>所有存档的元信息，无存档时返回空列表。</returns>
        UniTask<List<SaveMeta>> GetSlotMetas(CancellationToken cancellationToken = default);

        /// <summary>获取指定槽位的元数据。槽位不存在时返回 <c>null</c>。</summary>
        UniTask<SaveMeta> GetSlotMeta(int slot, CancellationToken cancellationToken = default);

        /// <summary>
        /// 将当前游戏数据保存到指定槽位。
        /// <para>保存完成后返回该槽位的元信息。</para>
        /// </summary>
        UniTask<SaveMeta> SaveAsync(int slot, CancellationToken cancellationToken = default);

        /// <summary>
        /// 从指定槽位加载存档并恢复到当前游戏数据。
        /// <para>加载前会清空现有数据。</para>
        /// </summary>
        UniTask LoadAsync(int slot, CancellationToken cancellationToken = default);

        /// <summary>删除指定槽位的存档。</summary>
        void DeleteSlot(int slot);

        /// <summary>异步删除所有槽位的存档。</summary>
        UniTask DeleteAllSlotsAsync(CancellationToken cancellationToken = default);

        /// <summary>检查指定槽位是否存在存档。</summary>
        bool SlotExists(int slot);
    }

    /// <summary>
    /// <see cref="ISaveManager"/> 创建工厂委托。
    /// <para>在 <see cref="SaveManager.Initialize(System.Func{ISaveManager})"/> 中注册，
    /// 第三方可传入自定义实现替代默认的 <see cref="SaveManagerImpl"/>。</para>
    /// </summary>
    public delegate ISaveManager SaveManagerFactory();
}