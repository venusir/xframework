using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using XFramework.XLocalization;
using XFramework.XUI;
using XFramework.XUI.View;

namespace XFramework.Samples.Integration
{
    /// <summary>
    /// 确认对话框：<c>UIManager.ShowDialogAsync&lt;ConfirmDialog, bool&gt;</c> 的结果通道示例。
    /// <para>按钮只负责 <see cref="UIDialogPanel{TResult}.SetResult"/> 并关闭自己；框架在回池点兜底兑现，
    /// 因此被 <c>CloseAllAsync</c> 等路径强行关掉时等待方也不会永远挂着。</para>
    /// </summary>
    public sealed class ConfirmDialog : UIDialogPanel<bool>
    {
        #region Fields

        private Text _message;
        private Button _confirm;
        private Button _cancel;
        private string _messageKey = string.Empty;

        #endregion

        #region Lifecycle

        /// <inheritdoc/>
        protected override UniTask OnOpen(object userData)
        {
            _message = Find<Text>("txt_message");
            _confirm = Find<Button>("btn_confirm");
            _cancel = Find<Button>("btn_cancel");

            _messageKey = userData as string ?? string.Empty;
            _message.text = string.IsNullOrEmpty(_messageKey)
                ? string.Empty
                : LocalizationManager.Get(_messageKey);

            _confirm.onClick.AddListener(OnConfirm);
            _cancel.onClick.AddListener(OnCancel);
            return UniTask.CompletedTask;
        }

        /// <inheritdoc/>
        protected override UniTask OnClose()
        {
            if (_confirm != null) _confirm.onClick.RemoveListener(OnConfirm);
            if (_cancel != null) _cancel.onClick.RemoveListener(OnCancel);
            return UniTask.CompletedTask;
        }

        /// <inheritdoc/>
        protected override void OnLanguageChanged(string lang)
        {
            if (_message != null && !string.IsNullOrEmpty(_messageKey))
                _message.text = LocalizationManager.Get(_messageKey);
        }

        #endregion

        #region Actions

        private void OnConfirm()
        {
            SetResult(true);
            CloseSelfAsync().Forget();
        }

        private void OnCancel()
        {
            SetResult(false);
            CloseSelfAsync().Forget();
        }

        private async UniTaskVoid CloseSelfAsync()
        {
            await UIManager.CloseAsync(this);
        }

        #endregion

        #region Helpers

        private T Find<T>(string childName) where T : Component
        {
            var child = transform.Find(childName);
            if (child == null)
            {
                Debug.LogError($"[Integration] ConfirmDialog 缺少子节点 '{childName}'。");
                return null;
            }

            return child.GetComponent<T>();
        }

        #endregion
    }
}
