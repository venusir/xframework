namespace XFramework.XSave
{
    /// <summary>
    /// 存档元数据，用于 <see cref="ISaveManager.GetSlotMetas"/> 返回存档列表概览。
    /// <para>包含版本号、时间戳、文件路径等基本信息，不包含完整数据块快照。</para>
    /// <para>第三方可继承此类以扩展元数据字段，
    /// 配合 <see cref="XData.DataSnapshot.CreateMeta"/> 在存档读/写时自动映射。</para>
    /// </summary>
    public class SaveMeta
    {
        /// <summary>所属玩家 ID，未启用玩家隔离时为 <c>null</c>。</summary>
        public string playerId;

        /// <summary>槽位编号。</summary>
        public int slot;

        /// <summary>存档格式版本号。</summary>
        public string version;

        /// <summary>保存时间戳（ISO 8601）。</summary>
        public string timestamp;

        /// <summary>存档文件相对路径。</summary>
        public string relativePath;

        /// <summary>存档文件大小（字节）。</summary>
        public long fileSize;

        public override string ToString()
        {
            return $"[Slot:{slot}] v{version} @ {timestamp} ({fileSize} bytes)";
        }
    }
}