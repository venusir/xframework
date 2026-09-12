using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XPipeline;
using XFramework.XSave;

namespace XFramework.XNode
{
    /// <summary>
    /// Save 模块的节点树桥梁。挂载在 <see cref="ServiceInitializerNode"/> 下，
    /// 作为 <see cref="IPhaseStage"/> 在启动管线的相位分组中初始化 <see cref="XSave.SaveManager"/> 静态门面。
    /// </summary>
    internal sealed class SaveBootstrapNode : LeafNode, IPhaseStage
    {
        #region Private Fields

        private SaveOptions _options;

        #endregion

        #region Lifecycle

        /// <summary>
        /// 接收初始化选项。
        /// <para>节点树经 <c>AddNode&lt;SaveBootstrapNode&gt;(options)</c> 传入
        /// <see cref="SaveOptions"/>；默认的 <c>ServiceInitializerNode</c> 不传参数，
        /// 此时使用默认值（存档格式版本 1）。</para>
        /// </summary>
        /// <param name="arg"><see cref="SaveOptions"/> 实例，或 <c>null</c>。</param>
        protected override void OnInit(object arg)
        {
            base.OnInit(arg);
            _options = arg as SaveOptions;
        }

        protected override void OnDestroy()
        {
            SaveManager.Shutdown();
            base.OnDestroy();
        }

        #endregion

        #region IPhaseStage

        /// <summary>Phase = 4。晚于 Data(3)，确保快照能力已就绪。</summary>
        public int Phase => 4;

        public string Name => GetType().Name;

        public float Weight => 1f;

        public async UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
        {
            context.SetDescription("Initializing Save Manager...");

            SaveManager.Initialize(null, _options);

            // 恢复扫描不切回主线程（见 SaveManager.RecoverAsync），因此这一步不会要求 PlayerLoop 泵
            await SaveManager.RecoverAsync(cancellationToken);

            // PipelineStageContext 有越线程写入检测（编辑器下会报 LogError），
            // 恢复扫描结束后必须切回主线程再写
            await UniTask.SwitchToMainThread(cancellationToken);

            context.SetProgress(1f);
            context.SetState(PipelineStageState.Completed);
        }

        #endregion
    }
}
