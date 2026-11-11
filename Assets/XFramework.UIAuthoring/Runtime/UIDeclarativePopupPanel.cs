using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XLog;
using XFramework.XUI.View;

namespace XFramework.XUI.Authoring
{
    /// <summary>
    /// 声明式弹窗：根节点挂本组件，用 <see cref="UIPanelDefinition"/>（<see cref="UIPanelDefinitionKind.Popup"/>）
    /// 配置文本、动作与数据绑定。
    /// <para>与 <see cref="UIDeclarativePanel"/> 是不同具体类型，可与普通声明式面板、对话框同时打开；
    /// 同一时刻只能开一个声明式弹窗（核心按具体类型登记）。</para>
    /// <para>由 <see cref="UIAuthoringNavigation.ShowPopupAsync(RectTransform, string, UIPopupDirection, UIPopupAlign, bool, System.Threading.CancellationToken)"/> 或定义里的 <see cref="UIActionKind.ShowPopup"/>
    /// 打开；关闭用 <see cref="UIActionKind.CloseSelf"/>，或点击外部（仅核心内置 <c>UIManagerImpl</c>）。</para>
    /// </summary>
    public sealed class UIDeclarativePopupPanel : UIPanelBase
    {
        [SerializeField] private UIPanelDefinition _definition;

        internal IUIActionRuntime RuntimeOverride;
        private UIActionSession _session;

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

        /// <inheritdoc/>
        protected override UniTask OnOpen(object userData)
        {
            if (_definition == null)
                return UniTask.CompletedTask;

            if (_definition.Kind != UIPanelDefinitionKind.Popup)
            {
                LogManager.Warning(UIAuthoringLog.Category,
                    "Definition kind '{0}' does not match UIDeclarativePopupPanel; binding skipped.",
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
                LogManager.Error(UIAuthoringLog.Category, "Declarative popup binding failed: {0}", ex);
            }

            return UniTask.CompletedTask;
        }

        /// <inheritdoc/>
        protected override UniTask OnClose()
        {
            _session?.Invalidate();
            _session = null;
            return UniTask.CompletedTask;
        }
    }
}
