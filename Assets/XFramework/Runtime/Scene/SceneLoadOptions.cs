using UnityEngine.SceneManagement;

namespace XFramework.XScene
{
    /// <summary>
    /// 场景加载选项。
    /// <para><b><c>default</c> 与 <c>new SceneLoadOptions()</c> 都表示「单场景 / 不激活 / 不回收」</b>
    /// ——全零在这里是有意义的默认，不是陷阱。</para>
    /// </summary>
    public readonly struct SceneLoadOptions
    {
        #region Public Fields

        /// <summary>加载模式：<see cref="LoadSceneMode.Single"/> 卸载当前场景后加载；<see cref="LoadSceneMode.Additive"/> 叠加。</summary>
        public readonly LoadSceneMode Mode;

        /// <summary>
        /// 加载完成后是否把新场景设为激活场景。
        /// <para><b>只在叠加模式有意义</b>：<c>Single</c> 由 Unity 自己激活。叠加加载而不激活是真实陷阱——
        /// 新场景的物体会挂到旧场景下，并随旧场景一起被销毁。</para>
        /// </summary>
        public readonly bool ActivateAfterLoad;

        /// <summary>
        /// 成功加载后是否顺带回收未使用资源（转调 <c>AssetManager.UnloadUnusedAssetsAsync</c>）。
        /// <para>切换场景之后旧场景的 bundle 往往就此闲置，但回收有代价——什么时候划算由使用方决定，
        /// 故默认不做。</para>
        /// </summary>
        public readonly bool UnloadUnusedAfterLoad;

        #endregion

        #region Lifecycle

        /// <summary>构造选项。</summary>
        /// <param name="mode">加载模式。</param>
        /// <param name="activateAfterLoad">叠加加载后是否激活新场景。</param>
        /// <param name="unloadUnusedAfterLoad">成功加载后是否回收未使用资源。</param>
        public SceneLoadOptions(LoadSceneMode mode = LoadSceneMode.Single,
            bool activateAfterLoad = false, bool unloadUnusedAfterLoad = false)
        {
            Mode = mode;
            ActivateAfterLoad = activateAfterLoad;
            UnloadUnusedAfterLoad = unloadUnusedAfterLoad;
        }

        #endregion

        #region Public API

        /// <summary>单场景加载（缺省），不额外回收资源。</summary>
        public static SceneLoadOptions Single => new SceneLoadOptions(LoadSceneMode.Single);

        /// <summary>叠加加载，不改变激活场景、不额外回收资源。</summary>
        public static SceneLoadOptions Additive => new SceneLoadOptions(LoadSceneMode.Additive);

        #endregion
    }
}
