using System;
using System.Threading;
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

        /// <summary>
        /// 设置后，<c>OnBeforeCloseAsync</c> 会在返回「放行」的同时取消该令牌。
        /// <para>用于验证「提交点之后的取消被忽略」——半关状态比取消失效难排查得多。</para>
        /// </summary>
        public CancellationTokenSource CancelOnBeforeClose;

        /// <summary>打开后回调被调用的次数。</summary>
        public int AfterOpenCount { get; private set; }

        /// <summary>关闭后回调被调用的次数。</summary>
        public int AfterCloseCount { get; private set; }

        public UniTask<bool> OnBeforeOpenAsync(Type panelType, string assetPath, int layer, object userData,
            CancellationToken cancellationToken = default)
        {
            return UniTask.FromResult(!BlockOpen);
        }

        public UniTask OnAfterOpenAsync(Type panelType, UIPanelBase panel, object userData,
            CancellationToken cancellationToken = default)
        {
            AfterOpenCount++;
            return UniTask.CompletedTask;
        }

        public UniTask<bool> OnBeforeCloseAsync(Type panelType, UIPanelBase panel, bool immediate,
            CancellationToken cancellationToken = default)
        {
            CancelOnBeforeClose?.Cancel();
            return UniTask.FromResult(!BlockClose);
        }

        public UniTask OnAfterCloseAsync(Type panelType, CancellationToken cancellationToken = default)
        {
            AfterCloseCount++;
            return UniTask.CompletedTask;
        }

        public UniTask OnAllPanelsClosedAsync(CancellationToken cancellationToken = default)
        {
            return UniTask.CompletedTask;
        }
    }
}
