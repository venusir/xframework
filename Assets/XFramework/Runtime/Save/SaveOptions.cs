namespace XFramework.XSave
{
    /// <summary>
    /// 存档模块初始化选项。
    /// <para>经 <see cref="SaveManager.Initialize(SaveManagerFactory, SaveOptions)"/> 传入，
    /// 或由节点树在挂载 <c>SaveBootstrapNode</c> 时经 <c>AddNode&lt;SaveBootstrapNode&gt;(options)</c> 传入。</para>
    /// </summary>
    public sealed class SaveOptions
    {
        /// <summary>
        /// 当前客户端支持的存档格式版本上限，默认 <c>1</c>。
        /// <para>保存时写入快照的 <see cref="XData.DataSnapshot.version"/>；
        /// 加载时<b>高于</b>该值的存档会被整份拒绝（见 <see cref="SaveLoadStatus.VersionTooNew"/>），
        /// <b>低于</b>该值的存档正常加载并按逐块迁移链升级（见 <see cref="SaveLoadStatus.Migrated"/>）。</para>
        /// <para>游戏每次发布新存档格式时递增此值，即可防止旧客户端把新格式存档「半加载」
        /// （数据块各自跳过，结果一半是新数据一半是空的）。</para>
        /// </summary>
        public int CurrentVersion = 1;
    }
}
