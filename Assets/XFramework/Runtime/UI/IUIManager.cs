using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XUI.View;

namespace XFramework.XUI
{
    /// <summary>
    /// UI 管理器公共接口。与节点树无关，可供任何对象直接使用。
    /// <para>通过 <see cref="UIManager"/> 的静态方法直接调用，或注入 <see cref="IUIManager"/> 实例使用。</para>
    /// <para>层级使用 <see cref="int"/> 类型，数值越大越靠前。第三方项目可自由定义常量扩展层级。</para>
    /// <para>所有面板预制体通过 YooAsset（<see cref="XAsset.AssetManager"/>）加载。</para>
    /// </summary>
    public interface IUIManager : IDisposable
    {
        #region Properties

        /// <summary>
        /// 是否已初始化。
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// UI 根节点（场景中的 UIRootNode 的 Transform）。
        /// </summary>
        Transform UIRoot { get; }

        #endregion

        #region Basic Panel Management

        /// <summary>
        /// 打开指定类型的 UI 面板。
        /// <para>如果面板已打开则聚焦（BringToFront），不会重复创建。</para>
        /// </summary>
        /// <typeparam name="T">面板类型，需继承 <see cref="UIPanelBase"/>。</typeparam>
        /// <param name="assetPath">面板预制体的 YooAsset 地址。</param>
        /// <param name="layer">面板层级，数值越大越靠前。建议使用常量管理，默认为 100。</param>
        /// <param name="userData">传递给面板 <see cref="UIPanelBase.OnOpen"/> 的自定义数据。</param>
        /// <returns>打开的面板实例，支持 await。</returns>
        UniTask<T> OpenAsync<T>(string assetPath, int layer = 100, object userData = null,
            CancellationToken cancellationToken = default) where T : UIPanelBase;

        /// <summary>
        /// 关闭指定类型的 UI 面板。
        /// </summary>
        /// <param name="immediate">是否跳过关闭动画，直接销毁。</param>
        UniTask CloseAsync<T>(bool immediate = false, CancellationToken cancellationToken = default)
            where T : UIPanelBase;

        /// <summary>
        /// 关闭指定的面板实例。
        /// </summary>
        /// <param name="immediate">是否跳过关闭动画，直接销毁。</param>
        UniTask CloseAsync(UIPanelBase panel, bool immediate = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// 判断指定类型的面板是否已打开。
        /// </summary>
        bool IsOpen<T>() where T : UIPanelBase;

        /// <summary>
        /// 获取已打开的指定类型面板实例。未打开时返回 null。
        /// </summary>
        T GetPanel<T>() where T : UIPanelBase;

        /// <summary>
        /// 关闭指定层级的所有面板。
        /// </summary>
        /// <param name="immediate">是否跳过关闭动画，直接销毁。</param>
        UniTask CloseLayerAsync(int layer, bool immediate = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// 关闭所有面板。
        /// </summary>
        /// <param name="immediate">是否跳过关闭动画，直接销毁。</param>
        UniTask CloseAllAsync(bool immediate = false, CancellationToken cancellationToken = default);

        #endregion

        #region Stack Navigation

        /// <summary>
        /// 打开面板并压入显示栈。当前栈顶面板失焦（OnBlur），新面板获得焦点（OnOpen）。
        /// <para>与 <see cref="OpenAsync{T}"/> 的区别只在语义：两者都会入栈，故
        /// <see cref="PopAsync"/> 能退回到任何先打开的面板。「先 Open 开主界面、再 Push 开二级页」
        /// 是最常见的用法组合。</para>
        /// </summary>
        UniTask<T> PushAsync<T>(string assetPath, int layer = 100, object userData = null,
            CancellationToken cancellationToken = default) where T : UIPanelBase;

        /// <summary>
        /// 弹出显示栈顶部的面板，返回上一个面板（恢复焦点 OnFocus）。
        /// <para>栈底面板不参与弹出——栈深为 1 时本方法不做任何事，用 <see cref="CloseAsync(UIPanelBase, bool)"/>
        /// 关闭最后一个面板。</para>
        /// </summary>
        /// <param name="immediate">是否跳过关闭动画，直接回池。</param>
        UniTask PopAsync(bool immediate = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// 依次弹出栈顶面板，直到指定类型的面板成为栈顶。
        /// </summary>
        /// <param name="immediate">是否跳过关闭动画，直接回池。</param>
        UniTask PopToAsync<T>(bool immediate = false, CancellationToken cancellationToken = default)
            where T : UIPanelBase;

        /// <summary>
        /// 依次弹出栈顶面板，只保留最早打开的那一个（栈底）。
        /// </summary>
        /// <param name="immediate">是否跳过关闭动画，直接回池。</param>
        UniTask PopToRootAsync(bool immediate = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// 返回上一个面板。等价于 <see cref="PopAsync"/>，语义化命名，供返回键处理调用。
        /// </summary>
        /// <param name="immediate">是否跳过关闭动画，直接回池。</param>
        UniTask GoBackAsync(bool immediate = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// 显示栈中是否还有可退回的上一个面板（即栈深 &gt; 1）。
        /// </summary>
        bool CanGoBack { get; }

        #endregion

        #region Modal Mask

        /// <summary>
        /// 显示模态遮罩，阻止下方 UI 交互，并返回一个引用计数句柄。
        /// <para>遮罩按引用计数：只有全部句柄都释放后才真正隐藏，多个系统各自需要遮罩时不再互相踩。</para>
        /// </summary>
        /// <param name="style">遮罩样式（层级、颜色、点击是否关闭）。</param>
        /// <param name="owner">持有者面板（可选）。面板关闭时其持有会被自动释放。</param>
        /// <returns>遮罩句柄。不关心时可直接丢弃，但那样就退化成「谁都能关」。</returns>
        UIMaskHandle ShowMask(UIMaskStyle style, UIPanelBase owner = null);

        /// <summary>
        /// 显示模态遮罩（简写形式），并返回引用计数句柄。
        /// </summary>
        /// <param name="maskLayer">遮罩所在层级。</param>
        /// <param name="alpha">遮罩透明度 (0-1)。</param>
        /// <param name="clickToClose">点击遮罩是否关闭显示栈顶部的面板。</param>
        UIMaskHandle ShowMask(int maskLayer = UILayers.Mask, float alpha = 0.5f, bool clickToClose = false);

        /// <summary>
        /// 隐藏模态遮罩：清掉全部持有引用并隐藏。
        /// <para>不使用句柄的调用方走这条路；用了句柄的应当 Dispose 自己的那一份。</para>
        /// </summary>
        void HideMask();

        /// <summary>
        /// 设置点击遮罩是否关闭栈顶面板。对已存在的遮罩同样生效。
        /// </summary>
        /// <param name="clickToClose">是否开启。</param>
        void SetMaskClickToClose(bool clickToClose);

        /// <summary>
        /// 遮罩是否正在显示。
        /// </summary>
        bool IsMaskShowing { get; }

        #endregion

        #region Preload & Cache

        /// <summary>
        /// 预加载面板资源到缓存，后续 <see cref="OpenAsync{T}"/> 或 <see cref="PushAsync{T}"/> 时直接从缓存实例化。
        /// </summary>
        UniTask PreloadAsync<T>(string assetPath, CancellationToken cancellationToken = default)
            where T : UIPanelBase;

        /// <summary>
        /// 从缓存中移除指定面板的预制体资源，释放内存。
        /// </summary>
        void UnloadAsset<T>() where T : UIPanelBase;

        /// <summary>
        /// 清空所有缓存的面板预制体资源。
        /// </summary>
        void ClearAssetCache();

        #endregion

        #region Layer

        /// <summary>
        /// 显示 / 隐藏整个层级（该层级容器的所有面板一并生效）。
        /// </summary>
        /// <param name="layer">目标层级。</param>
        /// <param name="visible">是否显示。</param>
        void SetLayerVisibility(int layer, bool visible);

        /// <summary>
        /// 启用 / 禁用整个层级的交互。
        /// <para>层的整体开关优先于单个面板的焦点状态：禁用后，后续的焦点变化与重新打开都不会把
        /// 面板的射线重新打开。</para>
        /// </summary>
        /// <param name="layer">目标层级。</param>
        /// <param name="interactive">是否允许交互。</param>
        void SetLayerInteractive(int layer, bool interactive);

        #endregion

        #region Sort Order

        /// <summary>
        /// 将指定面板置于当前层级的最顶层（移到显示栈尾并按栈位重排）。
        /// </summary>
        void BringToFront(UIPanelBase panel);

        #endregion

        #region Per-Frame Update

        /// <summary>
        /// 每帧更新。内部遍历所有 IsOpen 的面板调用 <see cref="UIPanelBase.OnUpdate"/>。
        /// <para>借鉴 GameFramework UIFormLogic.OnUpdate 的设计，由管理器统一驱动而非每个面板独立 Update。</para>
        /// <para><b>调用方不需要自行驱动</b>：<see cref="UIManager.Initialize(Transform, IUIController)"/> 会把
        /// 每帧驱动注册进 <see cref="XUpdate.UpdateManager"/> 的统一调度——因此它受 LOD 降频与
        /// <see cref="XUpdate.UpdateManager.Pause"/> 的统一约束，也不再要求场景里存在 <c>UIRootNode</c>。</para>
        /// </summary>
        /// <param name="deltaTime">距上次派发的时间差。</param>
        /// <param name="time">当前时刻。</param>
        void Update(float deltaTime, float time);

        #endregion

    }
}
