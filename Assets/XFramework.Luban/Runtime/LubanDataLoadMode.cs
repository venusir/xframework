namespace XFramework.XConfig.Luban
{
    /// <summary>
    /// Luban 数据文件的加载方式。
    /// </summary>
    public enum LubanDataLoadMode
    {
        /// <summary>
        /// 按 <c>TextAsset</c> 加载（Unity 默认会把 <c>.bytes</c> / <c>.json</c> 导入为 TextAsset）。
        /// </summary>
        TextAsset = 0,

        /// <summary>
        /// 按 YooAsset 原始文件加载（collector 需把数据文件标记为 RawFile）。
        /// </summary>
        RawFile = 1,
    }
}
