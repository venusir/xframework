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
        /// 存档时调用。返回需要持久化的数据快照对象，返回 <c>null</c> 表示不参与本次存档。
        /// <para>由 DataManager 委托 <see cref="XSerialize.ISerializer"/> 序列化写入存档。</para>
        /// </summary>
        object OnSave();

        /// <summary>
        /// 读档时调用。传入该数据块上次 <see cref="OnSave"/> 的返回值。
        /// <para>由 DataManager 委托 <see cref="XSerialize.ISerializer"/> 从存档反序列化后传入。</para>
        /// </summary>
        void OnLoad(object saveData);

        /// <summary>
        /// 数据块被移除或 <see cref="IDataManager.ClearAll"/> 时调用，释放内部资源。
        /// </summary>
        void OnClear();
    }
}