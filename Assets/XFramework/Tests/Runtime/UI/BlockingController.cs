using System;
using Cysharp.Threading.Tasks;
using XFramework.XUI.Controller;
using XFramework.XUI.View;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 测试用控制器：可按开关拦下面板的打开/关闭，并统计后置回调次数。
    /// </summary>
    internal sealed class BlockingController : IUIController
    {
        /// <summary>为 true 时拦截所有打开（<c>OnBeforeOpenAsync</c> 返回 false）。</summary>
        public bool BlockOpen;

        /// <summary>为 true 时拦截所有关闭（<c>OnBeforeCloseAsync</c> 返回 false）。</summary>
        public bool BlockClose;

        /// <summary>打开后回调被调用的次数。</summary>
        public int AfterOpenCount { get; private set; }

        /// <summary>关闭后回调被调用的次数。</summary>
        public int AfterCloseCount { get; private set; }

        public UniTask<bool> OnBeforeOpenAsync(Type panelType, string assetPath, int layer, object userData)
        {
            return UniTask.FromResult(!BlockOpen);
        }

        public UniTask OnAfterOpenAsync(Type panelType, UIPanelBase panel, object userData)
        {
            AfterOpenCount++;
            return UniTask.CompletedTask;
        }

        public UniTask<bool> OnBeforeCloseAsync(Type panelType, UIPanelBase panel, bool immediate)
        {
            return UniTask.FromResult(!BlockClose);
        }

        public UniTask OnAfterCloseAsync(Type panelType)
        {
            AfterCloseCount++;
            return UniTask.CompletedTask;
        }

        public UniTask OnAllPanelsClosedAsync()
        {
            return UniTask.CompletedTask;
        }
    }
}
