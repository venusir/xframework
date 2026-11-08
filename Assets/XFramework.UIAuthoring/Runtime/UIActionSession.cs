using System;
using XFramework.XUI.View;

namespace XFramework.XUI.Authoring
{
    /// <summary>
    /// 一次面板/对话框打开期间的动作会话；关闭或回池后失效，异步续体不得再触碰面板。
    /// <para>会话本身作为 <c>Track</c> 项登记：回池/销毁时由基类统一 Dispose，避免定义
    /// <c>OnDestroy</c> 遮住 <c>UIViewBase</c> 的 private 兜底。</para>
    /// </summary>
    internal sealed class UIActionSession : IDisposable
    {
        internal UIActionSession(IUIActionRuntime runtime, UIPanelBase panel)
        {
            Runtime = runtime;
            Panel = panel;
        }

        internal IUIActionRuntime Runtime { get; }

        internal UIPanelBase Panel { get; }

        internal bool IsValid { get; private set; } = true;

        internal void Invalidate()
        {
            IsValid = false;
        }

        public void Dispose()
        {
            IsValid = false;
        }
    }
}
