using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XBootstrap;
using XFramework.XPipeline;

namespace XFramework.XLocalization
{

    /// <summary>
    /// <see cref="LocalizationManager"/> 的引导阶段。Phase = 90，晚于框架内置相位(0/3/4)。
    /// <para><b>不在 <see cref="Bootstrap.RegisterDefaults"/> 的默认组合内</b>——它需要语言数据，
    /// 由使用方构造并显式登记：</para>
    /// <code>
    /// Bootstrap.Register(new LocalizationBootstrapStage("en", myLanguageTable));
    /// </code>
    /// <para>与框架内置的另外三个阶段一样，本类型是 <c>public</c> 的：使用方需要能构造、替换、
    /// 或自行组合这些阶段，而不是只能接受框架预设的那一套。</para>
    /// </summary>
    public sealed class LocalizationBootstrapStage : IBootstrapStage
    {
        #region Private Fields

        private readonly string _defaultLanguage;
        private readonly Dictionary<string, string> _initData;

        #endregion

        #region Construction

        /// <summary>构造引导阶段。</summary>
        /// <param name="defaultLanguage">默认语言标识，如 <c>"zh_Hans"</c>、<c>"en"</c>。</param>
        /// <param name="data">键值对语言数据。<c>null</c> 时执行期打警告并跳过初始化（不失败、不阻塞启动）。</param>
        public LocalizationBootstrapStage(string defaultLanguage = "zh_Hans", Dictionary<string, string> data = null)
        {
            _defaultLanguage = defaultLanguage;
            _initData = data;
        }

        #endregion

        #region IBootstrapStage

        /// <summary>Phase = 90。晚于框架内置相位(0/3/4)，供业务在 90+ 区间自定义（内置相位约定见 Pipeline 模块 README）。</summary>
        public int Phase => 90;

        /// <inheritdoc/>
        public string Name => GetType().Name;

        /// <inheritdoc/>
        public float Weight => 1f;

        /// <summary>
        /// 初始化本地化数据。
        /// <para>未提供数据时<b>静默降级为「已完成」</b>——本地化是可选模块，
        /// 缺数据不该让整个启动失败。同步瞬时阶段，忽略 <c>cancellationToken</c>。</para>
        /// </summary>
        public UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
        {
            context.SetDescription("Initializing localization...");

            if (_initData == null)
            {
                Debug.LogWarning("[LocalizationBootstrapStage] ExecuteAsync called but _initData is null. Skipping initialization.");
                context.SetProgress(1f);
                context.SetState(PipelineStageState.Completed);
                return UniTask.CompletedTask;
            }

            LocalizationManager.Initialize(_defaultLanguage, _initData);

            context.SetProgress(1f);
            context.SetState(PipelineStageState.Completed);
            return UniTask.CompletedTask;
        }

        /// <inheritdoc/>
        public void Shutdown()
        {
            LocalizationManager.Destroy();
        }

        #endregion
    }
}
