using System;

namespace XFramework.XScene
{
    /// <summary>
    /// 把 <c>XAsset</c> 的 <c>Action&lt;float&gt;</c> 进度接到本模块的 <see cref="IProgress{T}"/> 载荷上。
    /// <para><b>每次加载一个实例</b>：地址随加载走，不必在每帧的轮询路径上重复传递。
    /// <c>Report(float)</c> 只构造一个结构体并走一次泛型接口调用——无闭包、无装箱。</para>
    /// </summary>
    internal sealed class SceneLoadProgressRelay
    {
        #region Private Fields

        private readonly string _location;
        private readonly IProgress<SceneLoadProgress> _inner;

        #endregion

        #region Lifecycle

        internal SceneLoadProgressRelay(string location, IProgress<SceneLoadProgress> inner)
        {
            _location = location;
            _inner = inner;
        }

        #endregion

        #region Internal Methods

        /// <summary>转发一次进度；方法组直接绑给 <c>Action&lt;float&gt;</c>，不额外包 lambda。</summary>
        internal void Report(float progress) => _inner.Report(new SceneLoadProgress(progress, _location));

        #endregion
    }
}
