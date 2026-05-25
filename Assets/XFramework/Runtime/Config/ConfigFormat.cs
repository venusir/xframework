namespace XFramework.XConfig
{
    /// <summary>
    /// 配置数据格式枚举。
    /// <para>XFramework 内部根据此枚举选择对应的 Loader 实现，调用方无需关心内部差异。</para>
    /// </summary>
    public enum ConfigFormat
    {
        /// <summary>JSON 格式，通过 <c>JsonUtility</c> 反序列化。</summary>
        Json = 0,

        /// <summary>ScriptableObject 格式，通过 <c>Resources.Load</c> 加载。</summary>
        ScriptableObject = 1,
    }
}