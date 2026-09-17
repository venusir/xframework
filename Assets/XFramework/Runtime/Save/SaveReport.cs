namespace XFramework.XSave
{
    /// <summary>
    /// 存档操作进度报告（中性载荷）。
    /// <para>多步操作（枚举槽位、批量删除）经 <see cref="System.IProgress{T}"/> 上报——
    /// 不依赖管线，模块可独立使用。</para>
    /// <para>形状与 <see cref="XAsset.AssetInitReport"/> 一致：值语义的 <c>readonly struct</c>，
    /// 报告 0~1 的整体进度与当前步骤描述。分步计数（第 k / 共 N 个）并入描述文本，
    /// 调用方直接显示即可，无需再拼装。</para>
    /// </summary>
    public readonly struct SaveReport
    {
        /// <summary>整体进度，0~1。</summary>
        public readonly float Progress;

        /// <summary>当前步骤描述。</summary>
        public readonly string Description;

        /// <summary>
        /// 构造进度报告。
        /// </summary>
        /// <param name="progress">整体进度，0~1。</param>
        /// <param name="description">当前步骤描述。</param>
        public SaveReport(float progress, string description)
        {
            Progress = progress;
            Description = description;
        }
    }
}
