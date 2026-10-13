using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using XFramework.XLog;

namespace XFramework.XAsset
{
    /// <summary>
    /// 场景句柄：一次成功加载的凭据，也是卸载它的入口。
    /// <para><b>为什么它归资源层</b>：场景的存活就是 YooAsset <c>SceneHandle</c> 的引用计数，
    /// 而只有持有资源后端的一侧拿得到它。</para>
    /// <para><b>它是 <c>readonly struct</c> 且字段全为值类型</b>：可复制、可长期持有、零 GC。
    /// <see cref="Id"/> 由实现方分配，<c>0</c> 保留给 <c>default</c>（无效句柄）。</para>
    /// <para><b>失败时拿到的是 <c>default</c></b>：<see cref="IsDefault"/> 为 <c>true</c>，
    /// <see cref="Dispose"/> 与 <see cref="UnloadAsync"/> 对它都是安全的空操作。</para>
    /// </summary>
    public readonly struct AssetSceneHandle : IDisposable
    {
        #region Public Fields

        /// <summary>实现方分配的标识；<c>0</c> 表示无效句柄（加载失败）。</summary>
        public readonly int Id;

        /// <summary>加载完成的场景；无效句柄时为 <c>default(Scene)</c>。</summary>
        public readonly Scene Scene;

        #endregion

        #region Lifecycle

        /// <summary>
        /// 构造句柄。
        /// <para><b>为什么是公开构造</b>：第三方替换后端时也要能产出句柄——照抄 <c>LockHandle</c> 的
        /// <c>internal</c> 构造会把「整体替换」这条路堵死（同 <c>AudioHandle</c> 的取舍）。</para>
        /// </summary>
        /// <param name="id">实现方分配的标识；<c>0</c> 表示无效。</param>
        /// <param name="scene">加载完成的场景。</param>
        public AssetSceneHandle(int id, Scene scene)
        {
            Id = id;
            Scene = scene;
        }

        #endregion

        #region Query

        /// <summary>是否为无效句柄（未加载成功）。</summary>
        public bool IsDefault => Id == 0;

        /// <summary>
        /// 句柄是否仍然可用：非默认、场景有效且仍在场。
        /// <para>纯字段检查，不做引擎查找；场景被别人卸载后会变为 <c>false</c>。</para>
        /// </summary>
        public bool IsValid => Id != 0 && Scene.IsValid() && Scene.isLoaded;

        #endregion

        #region Public API — Unload

        /// <summary>
        /// 卸载该场景并释放底层句柄。
        /// <para>失败（句柄已失效 / 场景已不在场 / 后端不支持 / 门面未初始化 / 它是当前唯一的场景）
        /// 返回 <c>false</c> <b>而不抛异常</b>——与 <see cref="IAssetManager"/> 的「失败返回 default」同一口径。</para>
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        public UniTask<bool> UnloadAsync(CancellationToken cancellationToken = default)
            => AssetManager.UnloadSceneAsync(this, cancellationToken);

        /// <summary>
        /// 即发即忘地卸载：不等结果、不抛异常（异常只记一条日志）。
        /// <para>要拿卸载结果请用 <see cref="UnloadAsync"/>。无效句柄下空转。</para>
        /// </summary>
        public void Dispose() => UnloadAndForgetAsync().Forget();

        /// <summary>
        /// fire-and-forget 的异常收口。
        /// <para><b>不能裸 <c>.Forget()</c></b>：异步段的异常会落到 <c>UniTaskScheduler</c>，记成没有分类前缀的
        /// 日志（同 <c>AudioManagerImpl.RunFireAndForget</c>）。</para>
        /// </summary>
        private async UniTaskVoid UnloadAndForgetAsync()
        {
            try
            {
                await AssetManager.UnloadSceneAsync(this, CancellationToken.None);
            }
            catch (Exception ex)
            {
                LogManager.Error(LogCategories.AssetManager, "场景句柄的即发即忘卸载失败：{0}", ex.Message);
            }
        }

        #endregion
    }
}
