using UnityEngine.SceneManagement;

namespace XFramework.XAsset
{
    /// <summary>
    /// 场景加载操作的来源缝（为什么要缝，见 <see cref="ISceneLoadOperation"/>）。
    /// <para>默认实现是 <see cref="YooAssetSceneLoadSource"/>；用例注入假实现以掌控完成时机、成败与取消。</para>
    /// </summary>
    internal interface ISceneLoadSource
    {
        /// <summary>
        /// 发起一次场景加载。
        /// <para>本方法只负责「拿到一个操作」，不负责等待、不负责报进度——那些是被用例覆盖的框架逻辑。</para>
        /// </summary>
        /// <param name="location">场景资源地址。</param>
        /// <param name="mode">单场景（<see cref="LoadSceneMode.Single"/>）或叠加（<see cref="LoadSceneMode.Additive"/>）。</param>
        /// <returns>加载操作；资源包不可用时返回 <c>null</c>（调用方按「加载失败」处理）。</returns>
        ISceneLoadOperation LoadScene(string location, LoadSceneMode mode);
    }
}
