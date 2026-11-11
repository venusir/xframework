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
        /// 设置待兑现的结果并通过运行时关闭；结果在 <see cref="OnClose"/> 中写入。
        /// <para><b>返回是否真的关闭</b>。控制器拦下关闭时面板仍开着，此处立即丢弃待兑现结果并返回
        /// <c>false</c>——否则该结果会在之后的任意一次正常关闭（<c>CloseAllAsync</c>、父面板关闭、
        /// 回池）时被误当成用户本次的选择兑现。</para>
        /// </summary>
        internal async UniTask<bool> CloseWithResultAsync(string resultId)
        {
            var session = _session;
            if (session == null)
                return false;

            _pendingResultId = resultId;
            bool closed = await session.Runtime.CloseAsync(this, false, default);
            if (!closed)
                _pendingResultId = null;
            return closed;
        }

        /// <inheritdoc/>
        protected override UniTask OnOpen(object userData)
        {
            // 回池路径不经过 OnClose：池化实例必须在这里清掉上一代的待兑现结果
            _pendingResultId = null;

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
                var executor = new UIActionExecutor(session, binder);

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

        /// <inheritdoc/>
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
