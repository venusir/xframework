using XFramework.XPipeline;
using System.Threading;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using XFramework.XNode;

namespace XFramework.XLocalization
{
    /// <summary>
    /// <see cref="LocalizationManager"/> 的引导阶段节点。
    /// <para>作为 <see cref="IPhaseStage"/>（Phase = 90）在启动管线的相位分组中初始化本地化数据。</para>
    /// <para>使用前请先通过 <see cref="SetInitData"/> 注入数据。</para>
    /// </summary>
    internal sealed class LocalizationBootstrapNode : EntityNode, IPhaseStage
    {
        #region Private Fields

        private string _defaultLanguage = "zh_Hans";
        private Dictionary<string, string> _initData;

        #endregion

        #region Public Methods

        /// <summary>
        /// 设置初始化数据。需要在执行前调用。
        /// </summary>
        /// <param name="defaultLanguage">默认语言标识，如 <c>"zh_Hans"</c>, <c>"en"</c></param>
        /// <param name="data">键值对数据</param>
        public void SetInitData(string defaultLanguage, Dictionary<string, string> data)
        {
            _defaultLanguage = defaultLanguage;
            _initData = data;
        }

        #endregion

        #region IPhaseStage

        /// <summary>Phase = 90。晚于框架内置相位(0/3/4)，供业务在 90+ 区间自定义（内置相位约定见 Pipeline 模块 README）。</summary>
        public int Phase => 90;

        public string Name => GetType().Name;

        public float Weight => 1f;

        public UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
        {
            context.SetDescription("Initializing localization...");

            if (_initData == null)
            {
                Debug.LogWarning("[LocalizationBootstrapNode] ExecuteAsync called but _initData is null. Skipping initialization.");
                context.SetProgress(1f);
                context.SetState(PipelineStageState.Completed);
                return UniTask.CompletedTask;
            }

            LocalizationManager.Initialize(_defaultLanguage, _initData);
            context.SetProgress(1f);
            context.SetState(PipelineStageState.Completed);
            return UniTask.CompletedTask;
        }

        #endregion

        #region Lifecycle

        protected override void OnDestroy()
        {
            LocalizationManager.Destroy();
            base.OnDestroy();
        }

        #endregion
    }
}
