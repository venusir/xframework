namespace XFramework.XConfig
{
    /// <summary>
    /// Table 配置行标记接口（无成员）。
    /// <para>仅用于 <c>where T : IConfigRow</c> 类型约束。
    /// 第三方不应直接实现此接口，应实现 <see cref="IConfigRow{TKey}"/>。</para>
    /// </summary>
    public interface IConfigRow
    {
    }

    /// <summary>
    /// Table 配置行统一接口（泛型主键）。
    /// <para>Table 类型的配置数据（多行，按 Id 索引）必须实现此接口。</para>
    /// <para>该接口不约束 class 或 struct，两者均可实现，方便第三方灵活选择。</para>
    /// <para><typeparamref name="TKey"/> 建议使用 int、string、long、enum 等实现了相等比较的类型，
    /// 以确保 <see cref="System.Collections.Generic.Dictionary{TKey, TValue}"/> 正常工作。
    /// 框架内部使用 <see cref="System.Collections.Generic.EqualityComparer{TKey}.Default"/> 进行相等比较。</para>
    /// <para><typeparamref name="TKey"/> 支持使用 <see cref="System.ValueTuple"/> 作为复合键。
    /// JSON 序列化时，复合键的各字段应拆分定义，通过计算属性组合为 Id：</para>
    /// </summary>
    /// <typeparam name="TKey">配置行主键类型。</typeparam>
    /// <example>
    /// <code>
    /// // 双键示例
    /// [Serializable]
    /// public struct SkillEffectRow : IConfigRow<(int skillId, int level)>
    /// {
    ///     public int  SkillId;
    ///     public int  Level;
    ///     public int  Damage;
    ///     public (int skillId, int level) Id => (SkillId, Level);
    /// }
    /// 
    /// // 三键示例
    /// var row = table.Get((1001, 5));
    /// </code>
    /// </example>
    public interface IConfigRow<TKey> : IConfigRow
    {
        /// <summary>配置行的唯一主键。</summary>
        TKey Id { get; }
    }
}
