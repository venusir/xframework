using System;
using XFramework.XData;

namespace XFramework.XSave
{
    /// <summary>
    /// 存档元数据，用于 <see cref="ISaveManager.GetSlotMetas"/> 返回存档列表概览。
    /// <para>包含版本号、时间戳、文件路径等基本信息，不包含完整数据块快照。</para>
    /// <para>第三方可通过设置 <see cref="Factory"/> 委托返回自定义子类来扩展元数据字段，
    /// 无需重新实现 <see cref="ISaveManager"/>。</para>
    /// </summary>
    public class SaveMeta
    {
        /// <summary>
        /// 创建 <see cref="SaveMeta"/> 实例的工厂委托。
        /// <para>第三方可替换此委托以返回自定义子类（如 <c>MySaveMeta : SaveMeta</c>），
        /// 从而在默认 <see cref="SaveManagerImpl"/> 中自动使用扩展的元数据字段。</para>
        /// </summary>
        public static Func<SaveMeta> Factory = () => new SaveMeta();

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

        /// <summary>
        /// 由 <see cref="SaveManagerImpl"/> 在填充完基础字段后调用，
        /// 用于从 <see cref="DataSnapshot"/> 中提取自定义元数据。
        /// <para>第三方子类可重写此方法以填充扩展字段（如缩略图、游玩时长等）。</para>
        /// </summary>
        /// <param name="snapshot">反序列化后的数据快照，可能是第三方的自定义子类。</param>
        protected internal virtual void OnPopulate(DataSnapshot snapshot) { }

        public override string ToString()
        {
            return $"[Slot:{slot}] v{version} @ {timestamp} ({fileSize} bytes)";
        }
    }
}