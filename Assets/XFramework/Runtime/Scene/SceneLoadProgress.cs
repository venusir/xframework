namespace XFramework.XScene
{
    /// <summary>
    /// 场景加载进度载荷（本模块自持的中性载荷，符合框架「模块自持进度上报」的约定）。
    /// <para>只读结构体：进度回调在每帧轮询路径上被调用，载荷必须是值类型。</para>
    /// </summary>
    public readonly struct SceneLoadProgress
    {
        /// <summary>进度（0~1）；成功时以 <c>1f</c> 收尾，失败路径不补发。</summary>
        public readonly float Progress;

        /// <summary>正在加载的场景地址。</summary>
        public readonly string Location;

        /// <summary>构造载荷。</summary>
        /// <param name="progress">进度（0~1）。</param>
        /// <param name="location">场景地址。</param>
        public SceneLoadProgress(float progress, string location)
        {
            Progress = progress;
            Location = location;
        }
    }
}
