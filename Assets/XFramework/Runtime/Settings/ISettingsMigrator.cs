namespace XFramework.XSettings
{
    /// <summary>
    /// 设置格式迁移钩子。仅当 <see cref="SettingsOptions.CurrentVersion"/> 大于持久化数据的版本时被调用。
    /// <para>迁移在加载路径上、订阅者被通知之前执行，因此迁移过程中写入设置对象不会产生多余通知。</para>
    /// <para>由 <see cref="ISettingsManager{T}.Migrator"/> 注册。</para>
    /// </summary>
    /// <typeparam name="T">设置对象类型。</typeparam>
    public interface ISettingsMigrator<T> where T : class, new()
    {
        /// <summary>
        /// 将设置对象从 <paramref name="fromVersion"/> 迁移到 <paramref name="toVersion"/>。
        /// <para>实现应<b>就地修改</b> <paramref name="settings"/>。逐级迁移还是直接跳到目标版本
        /// 由实现决定——框架不保证 <paramref name="toVersion"/> 与 <paramref name="fromVersion"/> 之差为 1。</para>
        /// </summary>
        /// <param name="fromVersion">持久化数据的格式版本。</param>
        /// <param name="toVersion">当前代码的格式版本（即 <see cref="SettingsOptions.CurrentVersion"/>）。</param>
        /// <param name="settings">待迁移的设置对象。</param>
        void Migrate(int fromVersion, int toVersion, T settings);
    }
}
