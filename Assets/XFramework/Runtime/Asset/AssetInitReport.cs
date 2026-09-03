namespace XFramework.XAsset
{
    /// <summary>
    /// 资源初始化进度报告(中性载荷)。初始化 API(<see cref="IAssetManager.InitializeAsync"/> 等)经
    /// <see cref="System.IProgress{T}"/> 上报——不依赖管线/节点树,模块可独立使用。
    /// <para>readonly struct 值语义;报告值为 0~1 的整体进度与当前步骤描述。</para>
    /// </summary>
    public readonly struct AssetInitReport
    {
        /// <summary>整体进度,0~1。</summary>
        public readonly float Progress;

        /// <summary>当前步骤描述。</summary>
        public readonly string Description;

        public AssetInitReport(float progress, string description)
        {
            Progress = progress;
            Description = description;
        }
    }
}
