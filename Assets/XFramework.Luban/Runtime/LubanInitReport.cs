namespace XFramework.XConfig.Luban
{
    /// <summary>
    /// Luban 配置加载进度载荷。
    /// </summary>
    public readonly struct LubanInitReport
    {
        /// <summary>
        /// 进度（0..1）。
        /// </summary>
        public float Progress { get; }

        /// <summary>
        /// 当前步骤描述。
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// 构造进度载荷。
        /// </summary>
        /// <param name="progress">进度（0..1）。</param>
        /// <param name="description">当前步骤描述。</param>
        public LubanInitReport(float progress, string description)
        {
            Progress = progress;
            Description = description;
        }
    }
}
