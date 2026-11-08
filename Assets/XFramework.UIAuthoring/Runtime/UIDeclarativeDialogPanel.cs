using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XLog;
using XFramework.XUI.View;

namespace XFramework.XUI.Authoring
{
    /// <summary>
    /// 声明式对话框：根节点挂本组件，结果类型固定 <see cref="string"/>。
    /// <para>按钮用 <see cref="UIActionKind.CloseWithResult"/> 设置结果并关闭；外部关闭返回 <c>null</c>。</para>
    /// </summary>
    public sealed class UIDeclarativeDialogPanel : UIDialogPanel<string>
    {
        [SerializeField] private UIPanelDefinition _definition;

        internal IUIActionRuntime RuntimeOverride;
        private UIActionSession _session;
        private string _pendingResultId;

        internal void SetDefinitionForTest(UIPanelDefinition definition)
        {
            _definition = definition;
        }

        internal void SetRuntimeForTest(IUIActionRuntime runtime)
        {
            RuntimeOverride = runtime;
        }

        internal UniTask OpenForTestAsync(object userData = null)
        {
            return OnOpen(userData);
        }

        internal UniTask CloseForTestAsync()
        {
            return OnClose();
        }

        /// <summary>
        /// 设置待兑现的结果并通过运行时关闭；结果在 <see cref="OnClose"/> 中写入，控制器拦下关闭时不会误兑现。
        /// </summary>
        internal async UniTask CloseWithResultAsync(string resultId)
        {
            _pendingResultId = resultId;
            if (_session != null)
                await _session.Runtime.CloseAsync(this, false, default);
        }

        protected override UniTask OnOpen(object userData)
        {
            if (_definition == null)
                return UniTask.CompletedTask;

            if (_definition.Kind != UIPanelDefinitionKind.Dialog)
            {
                LogManager.Warning(UIAuthoringLog.Category,
                    "Definition kind '{0}' does not match UIDeclarativeDialogPanel; binding skipped.",
                    _definition.Kind);
                return UniTask.CompletedTask;
            }

            try
            {
                IUIActionRuntime runtime = RuntimeOverride ?? new UIManagerActionRuntime();
                var session = new UIActionSession(runtime, this);
                var binder = new UIDeclarativeBinder(this, _definition);
                var executor = new UIActionExecutor(session, _definition, binder);

                _session = session;
                Track(session);
                binder.Bind(executor);
            }
            catch (Exception ex)
            {
                LogManager.Error(UIAuthoringLog.Category, "Declarative dialog binding failed: {0}", ex);
            }

            return UniTask.CompletedTask;
        }

        protected override UniTask OnClose()
        {
            if (!string.IsNullOrEmpty(_pendingResultId))
            {
                SetResult(_pendingResultId);
                _pendingResultId = null;
            }

            _session?.Invalidate();
            _session = null;
            return UniTask.CompletedTask;
        }
    }
}
