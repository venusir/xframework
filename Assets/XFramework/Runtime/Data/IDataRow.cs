namespace XFramework.XData
{
    /// <summary>
    /// Data 模块行数据标记接口。
    /// <para>用于 <c>where T : IDataRow</c> 泛型约束，与 Config 模块的 <see cref="XConfig.IConfigRow"/> 类似。</para>
    /// <para>行数据需同时实现 <see cref="IDataRow{TKey}"/> 以提供主键。</para>
    /// <para><see cref="RowKey"/> 提供非泛型主键访问，供存档恢复等框架内部路径使用，无需反射。</para>
    /// </summary>
    public interface IDataRow
    {
        /// <summary>
        /// 非泛型主键访问。实现时委托到 <see cref="IDataRow{TKey}.Id"/> 并装箱返回。
        /// <para>仅框架内部存档恢复路径调用（低频），业务代码不应使用此属性。</para>
        /// </summary>
        object RowKey { get; }
    }

    /// <summary>
    /// Data 模块行数据接口，要求实现 <see cref="Id"/> 属性作为主键。
    /// <para>与 <see cref="XConfig.IConfigRow{TKey}"/> 对等，但用于运行时可变数据。</para>
    /// </summary>
    /// <typeparam name="TKey">主键类型</typeparam>
    public interface IDataRow<TKey> : IDataRow
    {
        /// <summary>行数据主键</summary>
        TKey Id { get; set; }

        /// <summary>非泛型主键访问（显式实现以避免混淆业务代码）。</summary>
        object IDataRow.RowKey => Id;
    }
}