namespace XFramework.XData
{
    /// <summary>
    /// 游戏数据块接口。一个 gameplay 模块对应一个 DataBlock，
    /// 内部自行管理数据结构（列表、字典、单值等），不再强制主键约束。
    /// </summary>
    /// <example>
    /// <code>
    /// [Serializable]
    /// public class BagData : IDataBlock
    /// {
    ///     public string BlockName => "Bag";
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
        /// 数据块名称，用于存档索引和调试，不可为空。
        /// <para>建议使用 PascalCase 英文名（如 "Bag", "Quest"），与类名保持一致。</para>
        /// </summary>
        string BlockName { get; }

        /// <summary>
        /// 存档时调用。返回需要持久化的数据快照对象，返回 <c>null</c> 表示不参与本次存档。
        /// <para>返回值需为 <c>[Serializable]</c> 且与 <see cref="UnityEngine.JsonUtility"/> 兼容。</para>
        /// </summary>
        object OnSave();

        /// <summary>
        /// 读档时调用。传入该数据块上次 <see cref="OnSave"/> 的返回值。
        /// </summary>
        void OnLoad(object saveData);

        /// <summary>
        /// 数据块被移除或 <see cref="IDataManager.ClearAll"/> 时调用，释放内部资源。
        /// </summary>
        void OnClear();
    }
}