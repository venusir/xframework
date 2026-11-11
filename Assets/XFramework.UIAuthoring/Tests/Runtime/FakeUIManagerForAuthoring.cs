using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XUI.View;

namespace XFramework.XUI.Authoring.Tests
{
    /// <summary>
    /// <see cref="IUIManager"/> 的替身，用于直接测试生产实现 <see cref="UIManagerActionRuntime"/>。
    /// <para><b>未实现的成员一律抛</b>：静默返回默认值会让「用例其实调到了没实现的成员」变成绿灯。
    /// 接口新增成员时本文件会在编译期被点名。</para>
    /// <para>通过公开的 <c>UIManager.SetInstance</c> 注入；fixture 负责在 <c>TearDown</c> 里
    /// <c>UIManager.Destroy()</c> 复位共享的静态门面。</para>
    /// </summary>
    internal sealed class FakeUIManagerForAuthoring : IUIManager
    {
        #region 可配置的观测点

        /// <summary>当前视为已打开的面板类型。</summary>
        internal readonly HashSet<Type> OpenTypes = new HashSet<Type>();

        /// <summary><see cref="OpenAsync{T}"/> 返回的面板（成功后）。</summary>
        internal UIPanelBase PanelToReturn;

        /// <summary>非空时 <see cref="OpenAsync{T}"/> 抛出它（如取消）。</summary>
        internal Exception OpenException;

        internal int OpenCalls;
        internal int CloseCalls;
        internal int ShowMaskCalls;
        internal bool Disposed;

        public void Dispose()
        {
            Disposed = true;
        }

        public void Update(float deltaTime, float time)
        {
        }

        #endregion

        #region 被生产实现用到的成员

        public bool IsOpen<T>() where T : UIPanelBase => OpenTypes.Contains(typeof(T));

        public T GetPanel<T>() where T : UIPanelBase => PanelToReturn as T;

        public UniTask<T> OpenAsync<T>(string assetPath, int layer = 100, object userData = null,
            CancellationToken cancellationToken = default) where T : UIPanelBase
        {
            OpenCalls++;
            if (OpenException != null)
                throw OpenException;
            return UniTask.FromResult(PanelToReturn as T);
        }

        public UniTask CloseAsync(UIPanelBase panel, bool immediate = false,
            CancellationToken cancellationToken = default)
        {
            CloseCalls++;
            return UniTask.CompletedTask;
        }

        public UniTask CloseAsync<T>(bool immediate = false, CancellationToken cancellationToken = default)
            where T : UIPanelBase
        {
            CloseCalls++;
            return UniTask.CompletedTask;
        }

        public UIMaskHandle ShowMask(UIMaskStyle style, UIPanelBase owner = null)
        {
            ShowMaskCalls++;
            return default;
        }

        #endregion

        #region 未实现（见类文档）

        private static NotSupportedException NotSupported()
            => new NotSupportedException(
                "[FakeUIManagerForAuthoring] 本替身只实现被断言的成员，其余一律抛——静默返回默认值会让" +
                "「用例其实调到了没实现的成员」变成绿灯。需要新成员时在这里补实现。");

        public bool IsInitialized => throw NotSupported();

        public Transform UIRoot => throw NotSupported();

        public bool IsAnyOpen => throw NotSupported();

        public int OpenCount => throw NotSupported();

        public UIStateSnapshot GetState() => throw NotSupported();

        public string DumpState() => throw NotSupported();

        public UIPanelBase GetTopPanel() => throw NotSupported();

        public IReadOnlyList<UIPanelBase> Panels => throw NotSupported();

        public int CopyPanels(List<UIPanelBase> buffer) => throw NotSupported();

        public int CopyPanelsInLayer(int layer, List<UIPanelBase> buffer) => throw NotSupported();

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

        public void SetHudProvider(IUIHudProvider provider) => throw NotSupported();

        public void SetLayerVisibility(int layer, bool visible) => throw NotSupported();

        public void SetLayerInteractive(int layer, bool interactive) => throw NotSupported();

        public bool IsLayerVisible(int layer) => throw NotSupported();

        public bool IsLayerInteractive(int layer) => throw NotSupported();

        public void BringToFront(UIPanelBase panel) => throw NotSupported();

        #endregion
    }
}
