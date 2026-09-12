using System;

namespace XFramework.XSave
{
    /// <summary>
    /// 存档元数据，用于 <see cref="ISaveManager.GetSlotMetasAsync"/> 返回存档列表概览。
    /// <para>包含版本号、时间戳、文件路径等基本信息，不包含完整数据块快照。</para>
    /// <para>第三方可继承此类以扩展元数据字段，
    /// 配合 <see cref="XData.DataSnapshot.CreateMeta"/> 在存档读/写时自动映射。</para>
    /// <para>本类会被序列化为槽位文件的元数据侧车（<c>slot_N.save.meta</c>），
    /// 故子类的公共字段同样需要可序列化。</para>
    /// </summary>
    [Serializable]
    public class SaveMeta
    {
        /// <summary>所属玩家 ID，未启用玩家隔离时为 <c>null</c>。</summary>
        public string playerId;

        /// <summary>槽位编号。</summary>
        public int slot;

        /// <summary>存档格式版本号，数值越大版本越新。</summary>
        public int version;

        /// <summary>保存时间戳（ISO 8601）。</summary>
        public string timestamp;

        /// <summary>存档文件相对路径。</summary>
        public string relativePath;

        /// <summary>存档文件大小（字节）。</summary>
        public long fileSize;

        /// <summary>
        /// 该槽位的存档文件是否已损坏（文件存在但内容无法解析为有效存档）。
        /// <para>为 <c>true</c> 时其余字段取自文件系统与文件名：<see cref="version"/> 为 0、
        /// <see cref="timestamp"/> 为 <c>null</c>、<see cref="fileSize"/> 为文件实际大小。</para>
        /// <para>把损坏槽位列出来而不是隐藏它，是为了让存档界面能展示并提供删除；
        /// 隐藏会与 <see cref="ISaveManager.SlotExistsAsync"/> 返回 <c>true</c> 自相矛盾——
        /// 界面既看不到也删不掉它。</para>
        /// </summary>
        public bool isCorrupted;

        /// <summary>
        /// 存档载荷的完整性校验和（FNV-1a 64 位，见 <see cref="SaveIntegrity"/>）。
        /// <para>0 表示「未记录校验和」——此时加载不做校验（兼容手工构造或外部工具写出的侧车）。</para>
        /// <para>只能发现损坏，<b>不提供防篡改</b>；防篡改需要加密。</para>
        /// </summary>
        public ulong checksum;

        public override string ToString()
        {
            var state = isCorrupted ? " [损坏]" : string.Empty;
            return $"[Slot:{slot}] v{version} @ {timestamp} ({fileSize} bytes){state}";
        }
    }
}