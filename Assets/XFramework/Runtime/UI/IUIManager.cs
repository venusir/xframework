using System;
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
        UniTask<T> OpenAsync<T>(string assetPath, int layer = 100, object userData = null)
            where T : UIPanelBase;

        /// <summary>
        /// 关闭指定类型的 UI 面板。
        /// </summary>
        /// <param name="immediate">是否跳过关闭动画，直接销毁。</param>
        UniTask CloseAsync<T>(bool immediate = false) where T : UIPanelBase;

        /// <summary>
        /// 关闭指定的面板实例。
        /// </summary>
        /// <param name="immediate">是否跳过关闭动画，直接销毁。</param>
        UniTask CloseAsync(UIPanelBase panel, bool immediate = false);

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
        UniTask CloseLayerAsync(int layer, bool immediate = false);

        /// <summary>
        /// 关闭所有面板。
        /// </summary>
        /// <param name="immediate">是否跳过关闭动画，直接销毁。</param>
        UniTask CloseAllAsync(bool immediate = false);

        #endregion

        #region Stack Navigation

        /// <summary>
        /// 压入新面板到导航堆栈。当前面板失焦（OnBlur），新面板获得焦点（OnOpen）。
        /// <para>调用 <see cref="PopAsync"/> 或 <see cref="GoBackAsync"/> 可返回上一个面板。</para>
        /// </summary>
        UniTask<T> PushAsync<T>(string assetPath, int layer = 100, object userData = null)
            where T : UIPanelBase;

        /// <summary>
        /// 弹出导航堆栈顶部的面板，返回上一个面板（恢复焦点 OnFocus）。
        /// </summary>
        /// <param name="immediate">是否跳过关闭动画，直接销毁。</param>
        UniTask PopAsync(bool immediate = false);

        /// <summary>
        /// 返回指定类型的面板（往回查找，中间的面板会依次关闭）。
        /// </summary>
        /// <param name="immediate">是否跳过关闭动画，直接销毁。</param>
        UniTask BackToAsync<T>(bool immediate = false) where T : UIPanelBase;

        /// <summary>
        /// 返回上一个面板（Pop 的便捷方法）。
        /// </summary>
        /// <param name="immediate">是否跳过关闭动画，直接销毁。</param>
        UniTask GoBackAsync(bool immediate = false);

        /// <summary>
        /// 导航堆栈中是否还有上一个面板。
        /// </summary>
        bool HasPrevious { get; }

        #endregion

        #region Modal Mask

        /// <summary>
        /// 显示模态遮罩，阻止下方 UI 交互。
        /// <para>遮罩位于 <paramref name="maskLayer"/> 层级，默认为 500。</para>
        /// </summary>
        /// <param name="maskLayer">遮罩所在层级。</param>
        /// <param name="alpha">遮罩透明度 (0-1)。</param>
        /// <param name="clickToClose">点击遮罩是否自动关闭堆栈顶层面板。</param>
        void ShowMask(int maskLayer = 500, float alpha = 0.5f, bool clickToClose = false);

        /// <summary>
        /// 隐藏模态遮罩。
        /// </summary>
        void HideMask();

        /// <summary>
        /// 遮罩是否正在显示。
        /// </summary>
        bool IsMaskShowing { get; }

        #endregion

        #region Preload & Cache

        /// <summary>
        /// 预加载面板资源到缓存，后续 <see cref="OpenAsync{T}"/> 或 <see cref="PushAsync{T}"/> 时直接从缓存实例化。
        /// </summary>
        UniTask PreloadAsync<T>(string assetPath) where T : UIPanelBase;

        /// <summary>
        /// 从缓存中移除指定面板的预制体资源，释放内存。
        /// </summary>
        void UnloadAsset<T>() where T : UIPanelBase;

        /// <summary>
        /// 清空所有缓存的面板预制体资源。
        /// </summary>
        void ClearAssetCache();

        #endregion

        #region Sort Order

        /// <summary>
        /// 获取指定层级的当前最高 sorting order。
        /// </summary>
        int GetTopSortingOrder(int layer);

        /// <summary>
        /// 将指定面板置于当前层级的最顶层。
        /// </summary>
        void BringToFront(UIPanelBase panel);

        #endregion

        #region Per-Frame Update

        /// <summary>
        /// 每帧更新。应在 UIRootNode 或场景中驱动此方法，内部遍历所有 IsOpen 的面板调用 <see cref="UIPanelBase.OnUpdate"/>。
        /// <para>借鉴 GameFramework UIFormLogic.OnUpdate 的设计，由管理器统一驱动而非每个面板独立 Update。</para>
        /// </summary>
        void Update();

        #endregion

        #region Events

        /// <summary>
        /// 面板打开事件。参数为面板类型。
        /// </summary>
        event Action<Type> OnPanelOpened;

        /// <summary>
        /// 面板关闭事件。参数为面板类型。
        /// </summary>
        event Action<Type> OnPanelClosed;

        /// <summary>
        /// 所有面板关闭事件（<see cref="CloseAllAsync"/> 后触发）。
        /// </summary>
        event Action OnAllPanelsClosed;

        #endregion
    }
}
