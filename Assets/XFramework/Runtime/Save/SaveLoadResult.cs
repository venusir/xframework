namespace XFramework.XSave
{
    /// <summary>
    /// 存档加载状态。
    /// </summary>
    public enum SaveLoadStatus
    {
        /// <summary>主文件正常加载。</summary>
        Loaded = 0,

        /// <summary>主文件不可用（损坏、校验和不符或无法应用），已从一代备份（<c>.bak</c>）恢复。</summary>
        LoadedFromBackup = 1,

        /// <summary>存档为旧版本，已按逐块迁移链升级后加载。</summary>
        Migrated = 2,

        /// <summary>槽位不存在（主文件与备份均不存在）。</summary>
        Missing = 3,

        /// <summary>主文件与备份均不可用。</summary>
        Corrupt = 4,

        /// <summary>存档版本高于当前客户端支持的版本，已整份拒绝（防止代码回滚后写坏存档）。</summary>
        VersionTooNew = 5,
    }

    /// <summary>
    /// 存档加载结果（中性载荷）。
    /// <para>供 <see cref="ISaveManager.TryLoadAsync"/> 使用：把「槽位不存在」「文件损坏」这类
    /// <b>预期内的失败</b>以状态回报而非抛异常——存档界面需要区分这些情况并给出不同反馈，
    /// 而把它们做成异常会迫使调用方用 try 来表达正常分支。</para>
    /// <para>值语义的 <c>readonly struct</c>；失败时 <see cref="Meta"/> 为 <c>null</c>。</para>
    /// </summary>
    public readonly struct SaveLoadResult
    {
        /// <summary>加载状态。</summary>
        public readonly SaveLoadStatus Status;

        /// <summary>
        /// 已加载存档的元数据。
        /// <para><see cref="SaveLoadStatus.Missing"/> 与 <see cref="SaveLoadStatus.Corrupt"/> 时为 <c>null</c>。</para>
        /// </summary>
        public readonly SaveMeta Meta;

        /// <summary>诊断信息；成功时为 <c>null</c>。</summary>
        public readonly string Message;

        /// <summary>是否成功加载（含从备份恢复与旧版本迁移）。</summary>
        public bool IsSuccess =>
            Status == SaveLoadStatus.Loaded
            || Status == SaveLoadStatus.LoadedFromBackup
            || Status == SaveLoadStatus.Migrated;

        /// <summary>
        /// 构造加载结果。
        /// </summary>
        /// <param name="status">加载状态。</param>
        /// <param name="meta">元数据；失败时传 <c>null</c>。</param>
        /// <param name="message">诊断信息；成功时传 <c>null</c>。</param>
        public SaveLoadResult(SaveLoadStatus status, SaveMeta meta = null, string message = null)
        {
            Status = status;
            Meta = meta;
            Message = message;
        }
    }
}
