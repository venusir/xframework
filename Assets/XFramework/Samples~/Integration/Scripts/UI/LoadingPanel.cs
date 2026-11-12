using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using XFramework.XUI.View;

namespace XFramework.Samples.Integration
{
    /// <summary>加载面板：显示 <see cref="GameLoadingPipeline"/> 的聚合进度与当前阶段描述。</summary>
    public sealed class LoadingPanel : UIPanelBase
    {
        #region Fields

        private Text _status;
        private Image _progress;

        #endregion

        #region Lifecycle

        /// <inheritdoc/>
        protected override UniTask OnOpen(object userData)
        {
            _status = Find<Text>("txt_status");
            _progress = Find<Image>("img_progress");
            if (_progress != null)
            {
                _progress.type = Image.Type.Filled;
                _progress.fillMethod = Image.FillMethod.Horizontal;
                _progress.fillAmount = 0f;
            }

            return UniTask.CompletedTask;
        }

        /// <inheritdoc/>
        protected override UniTask OnClose()
        {
            return UniTask.CompletedTask;
        }

        #endregion

        /// <summary>更新进度与阶段描述（由管线 <c>OnProgressUpdate</c> 驱动）。</summary>
        public void SetProgress(float progress, string description)
        {
            if (_progress != null)
                _progress.fillAmount = Mathf.Clamp01(progress);

            if (_status != null)
                _status.text = description ?? string.Empty;
        }

        private T Find<T>(string childName) where T : Component
        {
            var child = transform.Find(childName);
            if (child == null)
            {
                Debug.LogError($"[Integration] LoadingPanel 缺少子节点 '{childName}'。");
                return null;
            }

            return child.GetComponent<T>();
        }
    }
}
