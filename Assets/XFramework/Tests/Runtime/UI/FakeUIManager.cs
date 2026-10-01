using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XUI.View;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// <see cref="IUIManager"/> 的替身：覆盖「注入自定义实现」这条路径——<c>SetInstance</c> 的降级分支、
    /// 每帧驱动对注入实现的转发、以及所有权语义（换实例/销毁时门面碰不碰它）。
    /// <para><b>未实现的成员一律抛</b>，不静默返回默认值：静默会让「用例其实调到了一个没实现的成员」
    /// 变成绿灯。接口新增成员时本文件会在编译期被点名——这正是我们要的，替身必须跟着接口走。</para>
    /// <para>它也是仓内唯一实现 <see cref="IUIManager"/> 的非 <c>UIManagerImpl</c> 类，故
    /// 「门面探测到非自带实现时如何降级」这条承诺首次有了可测的落点。</para>
    /// </summary>
    internal sealed class FakeUIManager : IUIManager
    {
        #region 观测点（被断言的成员）

        /// <summary>被每帧驱动器转发调用的次数。</summary>
        public int UpdateCount { get; private set; }

        /// <summary>置 true 时 <see cref="Update"/> 抛异常，用于验证门面驱动器的整层兜底。</summary>
        public bool ThrowOnUpdate { get; set; }

        /// <summary>是否被调用过 <see cref="Dispose"/>（验证所有权语义：注入的实例归调用方管）。</summary>
        public bool Disposed { get; private set; }

        public void Update(float deltaTime, float time)
        {
            UpdateCount++;

            if (ThrowOnUpdate)
                throw new InvalidOperationException("[FakeUIManager] 故意在 Update 里抛异常");
        }

        public void Dispose()
        {
            Disposed = true;
        }

        #endregion

        #region 未实现（见类文档）

        private static NotSupportedException NotSupported()
            => new NotSupportedException(
                "[FakeUIManager] 本替身只实现被断言的成员，其余一律抛——静默返回默认值会让" +
                "「用例其实调到了没实现的成员」变成绿灯。需要新成员时在这里补实现。");

        public bool IsInitialized => throw NotSupported();

        public Transform UIRoot => throw NotSupported();

        public UniTask<T> OpenAsync<T>(string assetPath, int layer = 100, object userData = null,
            CancellationToken cancellationToken = default) where T : UIPanelBase => throw NotSupported();

        public UniTask CloseAsync<T>(bool immediate = false, CancellationToken cancellationToken = default)
            where T : UIPanelBase => throw NotSupported();

        public UniTask CloseAsync(UIPanelBase panel, bool immediate = false,
            CancellationToken cancellationToken = default) => throw NotSupported();

        public bool IsOpen<T>() where T : UIPanelBase => throw NotSupported();

        public T GetPanel<T>() where T : UIPanelBase => throw NotSupported();

        public UniTask CloseLayerAsync(int layer, bool immediate = false,
            CancellationToken cancellationToken = default) => throw NotSupported();

        public UniTask CloseAllAsync(bool immediate = false,
            CancellationToken cancellationToken = default) => throw NotSupported();

        public UniTask<T> PushAsync<T>(string assetPath, int layer = 100, object userData = null,
            CancellationToken cancellationToken = default) where T : UIPanelBase => throw NotSupported();

        public UniTask PopAsync(bool immediate = false,
            CancellationToken cancellationToken = default) => throw NotSupported();

        public UniTask PopToAsync<T>(bool immediate = false,
            CancellationToken cancellationToken = default) where T : UIPanelBase => throw NotSupported();

        public UniTask PopToRootAsync(bool immediate = false,
            CancellationToken cancellationToken = default) => throw NotSupported();

        public UniTask GoBackAsync(bool immediate = false,
            CancellationToken cancellationToken = default) => throw NotSupported();

        public bool CanGoBack => throw NotSupported();

        public UIMaskHandle ShowMask(UIMaskStyle style, UIPanelBase owner = null) => throw NotSupported();

        public UIMaskHandle ShowMask(int maskLayer = UILayers.Mask, float alpha = 0.5f,
            bool clickToClose = false) => throw NotSupported();

        public void HideMask() => throw NotSupported();

        public void SetMaskClickToClose(bool clickToClose) => throw NotSupported();

        public bool IsMaskShowing => throw NotSupported();

        public UniTask PreloadAsync<T>(string assetPath, CancellationToken cancellationToken = default)
            where T : UIPanelBase => throw NotSupported();

        public void ForgetPreload<T>() where T : UIPanelBase => throw NotSupported();

        public void ClearPreloads() => throw NotSupported();

        public UniTask<bool> UnloadPanelAssetAsync(string assetPath,
            CancellationToken cancellationToken = default) => throw NotSupported();

        public UniTask ShowTipAsync(string text, TipConfig config = default,
            CancellationToken cancellationToken = default) => throw NotSupported();

        public void SetTipProvider(IUITipProvider provider) => throw NotSupported();

        public UniTask<T> ShowHudAsync<T>(Transform target, string assetPath, Vector2? offset = null,
            CancellationToken cancellationToken = default) where T : UIHudItem => throw NotSupported();

        public void HideHud(Transform target) => throw NotSupported();

        public void SetHudProvider(IUiHudProvider provider) => throw NotSupported();

        public UIStateSnapshot GetState() => throw NotSupported();

        public string DumpState() => throw NotSupported();

        public int OpenCount => throw NotSupported();

        public bool IsAnyOpen => throw NotSupported();

        public UIPanelBase GetTopPanel() => throw NotSupported();

        public IReadOnlyList<UIPanelBase> Panels => throw NotSupported();

        public int CopyPanels(List<UIPanelBase> buffer) => throw NotSupported();

        public int CopyPanelsInLayer(int layer, List<UIPanelBase> buffer) => throw NotSupported();

        public void SetLayerVisibility(int layer, bool visible) => throw NotSupported();

        public void SetLayerInteractive(int layer, bool interactive) => throw NotSupported();

        public void BringToFront(UIPanelBase panel) => throw NotSupported();

        #endregion
    }
}
