namespace XFramework.XConfig.Luban.Editor
{
    /// <summary>
    /// Luban 的 C# 代码目标。
    /// </summary>
    public enum LubanCodeTarget
    {
        /// <summary>二进制读取代码（配 <see cref="LubanDataTarget.Bin"/>）。</summary>
        CsBin = 0,

        /// <summary>SimpleJSON 读取代码（配 <see cref="LubanDataTarget.Json"/>）。</summary>
        CsSimpleJson = 1,
    }

    /// <summary>
    /// Luban 的数据目标。
    /// </summary>
    public enum LubanDataTarget
    {
        /// <summary>二进制数据（配 <see cref="LubanCodeTarget.CsBin"/>）。</summary>
        Bin = 0,

        /// <summary>JSON 数据（配 <see cref="LubanCodeTarget.CsSimpleJson"/>）。</summary>
        Json = 1,
    }
}
