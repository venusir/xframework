namespace XFramework.XConfig
{
    /// <summary>
    /// Table 配置行统一接口。
    /// <para>Table 类型的配置数据（多行，按 Id 索引）必须实现此接口。</para>
    /// <para>该接口不约束 class 或 struct，两者均可实现，方便第三方灵活选择。</para>
    /// </summary>
    public interface IConfigRow
    {
        /// <summary>配置行的唯一主键。</summary>
        int Id { get; }
    }
}