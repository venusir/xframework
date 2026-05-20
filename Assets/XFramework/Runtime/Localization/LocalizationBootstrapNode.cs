using XFramework.XLoader;
using System.Threading;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using XFramework.XCore;

namespace XFramework.XLocalization
{
    /// <summary>
    /// <see cref="LocalizationManager"/> 的引导节点。
    /// <para>负责在启动管线中初始化本地化数据。</para>
    /// <para>使用前请先通过 <see cref="SetInitData"/> 注入数据。</para>
    /// </summary>
    internal sealed class LocalizationBootstrapNode : EntityNode, ILoadable
    {
        #region Private Fields

        private string _defaultLanguage = "zh_Hans";
        private Dictionary<string, string> _initData;

        #endregion

        #region Public Methods

        /// <summary>
        /// 设置初始化数据。需要在加载前调用。
        /// </summary>
        /// <param name="defaultLanguage">默认语言标识，如 <c>"zh_Hans"</c>, <c>"en"</c></param>
        /// <param name="data">键值对数据</param>
        public void SetInitData(string defaultLanguage, Dictionary<string, string> data)
        {
            _defaultLanguage = defaultLanguage;
            _initData = data;
        }

        #endregion

        #region ILoadable Implementation

        public int Phase => 90;

        public UniTask LoadAsync(LoadProgress progress, CancellationToken cancellationToken)
        {
            if (_initData == null)
            {
                Debug.LogWarning("[LocalizationBootstrapNode] LoadAsync called but _initData is null. Skipping initialization.");
                return UniTask.CompletedTask;
            }

            LocalizationManager.Initialize(_defaultLanguage, _initData);
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