using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using XFramework.XAsset;
using XFramework.XLog;

// 名字必须区分开：本文件同时可见 XFramework.XScene 与 UnityEngine.SceneManagement，两边各有一个
// SceneManager。**裸写不报错**——本命名空间的自有类型会静默遮蔽 Unity 那个（C# 判的是类型名），
// 于是「想调 Unity 的」变成「调了自己」。所以本模块**每个源文件**都写这个别名，且绝不裸写 SceneManager。
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

namespace XFramework.XScene
{
    /// <summary>
    /// 场景管理门面：加载、配对卸载与激活。
    /// <para><b>它只管「一个场景资源怎么加载、怎么卸载」</b>——场景之间怎么编排、加载界面长什么样、
    /// 参数怎么传，都是使用方自己的游戏流程，本模块不预设（四条边界的结论见
    /// <c>Runtime/Scene/README.md</c> 的「设计取舍」）。</para>
    /// <para><b>与 <c>UnityEngine.SceneManagement.SceneManager</c> 同名</b>：同时 <c>using</c> 两者时用别名，
    /// 写法见 README 首屏。本模块源码一律写别名，不裸写 <c>SceneManager</c>。</para>
    /// <para><b>无初始化、无会话复位、无 <c>ISceneManager</c> 接口</b>：本门面的静态成员无状态，后端是
    /// <c>XAsset</c> 的可选能力接口 <see cref="IAssetSceneLoader"/>——本仓「有接口」的分界线是「是否存在
    /// 可替换的后端」，而这里的后端就是它。</para>
    /// </summary>
    public static class SceneManager
    {
        #region Public API — Load

        /// <summary>
        /// 异步加载场景，返回可卸载的句柄。
        /// <para><b>失败返回 <c>default(AssetSceneHandle)</c> 而不抛</b>（与 <c>XAsset</c> 同口径，用
        /// <see cref="AssetSceneHandle.IsDefault"/> 校验）；取消抛 <see cref="OperationCanceledException"/>。</para>
        /// <para><b>取消只中止加载等待</b>——底层加载本身不中止（YooAsset 没有对外可用的中止入口），
        /// 善后由 <c>XAsset</c> 接力完成。加载**一旦成功**，激活与回收两步不再响应调用方令牌
        /// （句柄保证交回；否则 Additive 场景会留在 Hierarchy 且外部无卸载凭据，SCN-001）；
        /// 取消发生在加载成功之前时，两步都不执行。</para>
        /// </summary>
        /// <param name="location">场景资源地址。</param>
        /// <param name="options">加载选项；省略即单场景、不激活、不回收。</param>
        /// <param name="progress">进度上报（可空），见 <see cref="SceneLoadProgress"/>。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public static async UniTask<AssetSceneHandle> LoadSceneAsync(string location,
            SceneLoadOptions options = default,
            IProgress<SceneLoadProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(location))
                throw new ArgumentException("场景地址不能为 null 或空白。", nameof(location));

            EnsureAssetInitialized();

            Action<float> onProgress = null;
            if (progress != null)
            {
                onProgress = new SceneLoadProgressRelay(location, progress).Report;
            }

            var handle = await AssetManager.LoadSceneAsync(location, options.Mode, onProgress, cancellationToken);
            if (handle.IsDefault) return handle;

            if (options.ActivateAfterLoad && options.Mode == LoadSceneMode.Additive)
            {
                if (!SetActiveScene(handle.Scene))
                {
                    // 激活失败不可观测会让「新实例挂错场景」难以排查（SCN-004）。
                    LogManager.Warning(LogCategory.Get("SceneManager"),
                        "场景 '{0}' 加载成功但无法设为激活场景（引擎拒绝）；新实例仍挂在原激活场景下。",
                        handle.Scene.name);
                }
            }

            if (options.UnloadUnusedAfterLoad)
            {
                // 加载成功后的回收段不再响应调用方令牌：此刻场景已进来、句柄已就绪，
                // 因取消丢掉句柄会让 Additive 场景留在 Hierarchy 且外部无卸载凭据（SCN-001）。
                // 回收是尽力而为的顺手动作，后端抛非取消异常时同样不能让 return handle
                // 不可达——否则与取消路径同形：场景已进来、句柄却丢了（SCN-002）。
                try
                {
                    await AssetManager.UnloadUnusedAssetsAsync(cancellationToken: CancellationToken.None);
                }
                catch (Exception e)
                {
                    LogManager.Exception(LogCategory.Get("SceneManager"), e,
                        "场景已加载，但加载后的资源回收失败；句柄仍返回。");
                }
            }

            return handle;
        }

        #endregion

        #region Public API — Activate & Query

        /// <summary>
        /// 把已加载的场景设为激活场景（多场景并存时，新实例挂到哪个场景下由它决定）。
        /// </summary>
        /// <param name="scene">目标场景；来自句柄的 <see cref="AssetSceneHandle.Scene"/>。</param>
        /// <returns>场景无效 / 未加载 / 引擎拒绝时为 <c>false</c>。</returns>
        public static bool SetActiveScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return false;
            return UnitySceneManager.SetActiveScene(scene);
        }

        /// <summary>
        /// 当前激活场景。
        /// <para>这是 Unity 本就有的事实，本模块只是转发——它不构成「场景栈」，也不回答「上一个场景是谁」。</para>
        /// </summary>
        public static Scene ActiveScene => UnitySceneManager.GetActiveScene();

        #endregion

        #region Internal Methods

        /// <summary>
        /// 前置校验：场景加载最终走 <c>XAsset</c>。未初始化时给一条带本模块前缀的错误与修复提示，
        /// 而不是让使用方去猜「为什么拿到的句柄是无效的」。
        /// </summary>
        private static void EnsureAssetInitialized()
        {
            if (AssetManager.IsInitialized) return;

            throw new InvalidOperationException(
                "[SceneManager] 资源管理器尚未初始化，无法加载场景。" +
                "请先调用 AssetManager.InitializeAsync()（通常由 Bootstrap 的 Asset 阶段完成）再加载场景。");
        }

        #endregion
    }
}
