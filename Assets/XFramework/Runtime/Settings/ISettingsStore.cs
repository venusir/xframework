namespace XFramework.XSettings
{
    /// <summary>
    /// 设置存储后端抽象。负责将设置对象序列化到持久层。
    /// <para>默认实现：<see cref="JsonFileStore"/>（基于 JSON 文件存储）。</para>
    /// <para>可通过实现此接口替换存储后端（如加密存储、远程云存档等）。</para>
    /// </summary>
    public interface ISettingsStore
    {
        /// <summary>
        /// 从持久层加载设置对象。
        /// <para>如果持久层不存在数据，应返回 <c>new T()</c>。</para>
        /// </summary>
        /// <typeparam name="T">设置对象类型。必须满足 <c>class, new()</c> 约束。</typeparam>
        /// <returns>加载的设置对象，如果无持久化数据则返回默认值。</returns>
        T Load<T>() where T : class, new();

        /// <summary>
        /// 将设置对象保存到持久层。
        /// </summary>
        /// <typeparam name="T">设置对象类型。</typeparam>
        /// <param name="settings">要保存的设置对象。</param>
        void Save<T>(T settings) where T : class, new();

        /// <summary>
        /// 检查持久层是否存在已保存的数据。
        /// </summary>
        /// <returns>如果持久层存在数据，返回 <c>true</c>。</returns>
        bool Exists();

        /// <summary>
        /// 删除持久层数据，恢复默认。
        /// </summary>
        void Delete();
    }
}