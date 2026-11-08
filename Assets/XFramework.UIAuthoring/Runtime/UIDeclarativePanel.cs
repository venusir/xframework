using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XLog;
using XFramework.XUI.View;

namespace XFramework.XUI.Authoring
{
    /// <summary>
    /// 声明式面板：根节点挂本组件，用 <see cref="UIPanelDefinition"/> 配置文本/动作/页签。
    /// <para>单一具体类型意味着同一时刻只能打开一个声明式面板；多页用子页/页签。</para>
    /// </summary>
    public sealed class UIDeclarativePanel : UIPanelBase
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

        protected override UniTask OnOpen(object userData)
        {
            if (_definition == null)
                return UniTask.CompletedTask;

            if (_definition.Kind != UIPanelDefinitionKind.Panel)
            {
                LogManager.Warning(UIAuthoringLog.Category,
                    "Definition kind '{0}' does not match UIDeclarativePanel; binding skipped.",
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
                LogManager.Error(UIAuthoringLog.Category, "Declarative panel binding failed: {0}", ex);
            }

            return UniTask.CompletedTask;
        }

        protected override UniTask OnClose()
        {
            _session?.Invalidate();
            _session = null;
            return UniTask.CompletedTask;
        }
    }
}
