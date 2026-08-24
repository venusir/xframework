namespace XFramework.XData
{
    /// <summary>
    /// 数据块接口。
    /// <para>每个 <see cref="IDataBlock"/> 都由 DataManager 管理，
    /// 生命周期见各方法文档。</para>
    /// <para>序列化/反序列化委托给 <see cref="XSerialize.Serializer"/>，
    /// 第三方可实现 <see cref="XSerialize.ISerializer"/> 扩展自定义格式（如 MessagePack、Protobuf）。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// [Serializable]
    /// public class BagData : IDataBlock
    /// {
    ///     public string BlockName => nameof(BagData);
    ///     public List<BagItem> Items = new();
    ///     public int Gold;
    ///
    ///     [Serializable]
    ///     private struct SaveSnap { public List<BagItem> items; public int gold; }
    ///
    ///     public object OnSave() => new SaveSnap { items = Items, gold = Gold };
    ///     public void OnLoad(object data) { if (data is SaveSnap s) { Items = s.items; Gold = s.gold; } }
    ///     public void OnClear() { Items.Clear(); Gold = 0; }
    /// }
    /// </code>
    /// </example>
    public interface IDataBlock
    {
        /// <summary>
        /// 数据块唯一名称，用于快照索引，替代 AssemblyQualifiedName 反射方式。
        /// <para>建议使用 nameof(MyBlock) 或字符串常量，与类名保持一致。</para>
        /// </summary>
        string BlockName { get; }

        /// <summary>
        /// 当前数据结构版本号，用于存档迁移。数值越大版本越新。
        /// <para>读档时若快照 <see cref="DataBlockSnapshot.version"/> 小于此值，
        /// DataManager 会在 <see cref="OnLoad"/> 前按版本差依次调用 <see cref="OnMigrate"/> 迁移数据。</para>
        /// <para>新增字段或调整结构时版本号 +1，并实现对应的 <see cref="OnMigrate"/> 迁移逻辑；
        /// 尚未引入过版本控制的 Block 返回 0 且 <see cref="OnMigrate"/> 恒等返回即可。</para>
        /// </summary>
        int DataVersion { get; }

        /// <summary>
        /// 存档时调用。返回需要持久化的数据快照对象，返回 <c>null</c> 表示不参与本次存档。
        /// <para>由 DataManager 委托 <see cref="XSerialize.ISerializer"/> 序列化写入存档。</para>
        /// </summary>
        object OnSave();

        /// <summary>
        /// 数据迁移回调。快照版本低于 <see cref="DataVersion"/> 时由 DataManager 逐版本调用，
        /// 将旧版本数据迁移到当前结构。
        /// <para>入参 <paramref name="saveData"/> 是快照中 <see cref="OnSave"/> 返回对象经反序列化后的实例，
        /// 返回迁移到 <paramref name="fromVersion"/> + 1 版本的实例（通常就地修改后返回同一实例）。</para>
        /// <para>示例：旧存档无 version 字段时 <paramref name="fromVersion"/> 为 0，
        /// 实现 0→1 迁移后，返回对象最终传给 <see cref="OnLoad"/>。</para>
        /// </summary>
        object OnMigrate(object saveData, int fromVersion);

        /// <summary>
        /// 读档时调用。传入该数据块上次 <see cref="OnSave"/> 的返回值。
        /// <para>由 DataManager 委托 <see cref="XSerialize.ISerializer"/> 从存档反序列化后传入；
        /// 若快照版本低于 <see cref="DataVersion"/>，传入的是迁移链最终输出的对象。</para>
        /// </summary>
        void OnLoad(object saveData);

        /// <summary>
        /// 数据块被移除、<see cref="IDataManager.ClearAll"/> 或 <see cref="IDataManager.ApplySnapshot"/> 恢复前调用，释放内部资源。
        /// </summary>
        void OnClear();
    }
}