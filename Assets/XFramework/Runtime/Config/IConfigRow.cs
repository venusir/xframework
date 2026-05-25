namespace XFramework.XConfig
{
    /// <summary>
    /// Table 配置行统一接口（泛型主键）。
    /// <para>Table 类型的配置数据（多行，按 Id 索引）必须实现此接口。</para>
    /// <para>该接口不约束 class 或 struct，两者均可实现，方便第三方灵活选择。</para>
    /// <para><typeparamref name="TKey"/> 建议使用 int、string、long、enum 等实现了相等比较的类型，
    /// 以确保 <see cref="System.Collections.Generic.Dictionary{TKey, TValue}"/> 正常工作。
    /// 框架内部使用 <see cref="System.Collections.Generic.EqualityComparer{TKey}.Default"/> 进行相等比较。</para>
    /// </summary>
    /// <typeparam name="TKey">配置行主键类型。</typeparam>
    public interface IConfigRow<TKey>
    {
        /// <summary>配置行的唯一主键。</summary>
        TKey Id { get; }
    }
}
