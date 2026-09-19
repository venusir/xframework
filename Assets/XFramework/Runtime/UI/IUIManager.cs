using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XUI.View;

namespace XFramework.XUI
{
    /// <summary>
    /// UI 管理器公共接口。不依赖任何场景对象，可供任何对象直接使用。
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
        /// <para>如果面板已打开则把它重新置顶<b>并恢复焦点与更新</b>（原栈顶随之失焦），
        /// 不会重复创建。只置顶不恢复焦点会让它渲染在最上却收不到输入、也不被每帧派发。</para>
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
        /// 关闭所有面板，并一并回收世界空间 HUD 与在播 Tip（它们共享同一个 UIRoot 与生命周期，
        /// 「全部关闭」对调用方而言就是「界面清空」）。
        /// <para><b>不碰遮罩</b>：遮罩是引用计数句柄，谁持有谁释放——在这里强制清掉会让别的系统
        /// 手里的句柄凭空失效。要连遮罩一起收，显式调用 <see cref="HideMask"/>。</para>
        /// <para>面板自身持有的遮罩会随面板一起释放，无需配对调用。</para>
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
        /// 预热指定面板的资源包，使后续首次打开不卡在加载上。
        /// <para><b>实质</b>：经 <c>AssetManager.PreloadAllAsync</c> 把资源包读进内存缓存；
        /// 面板的<b>实例</b>池由 AssetManager 按地址独立维护，与本方法无关。打开路径也不查本方法的记账，
        /// 故重复预加载只是省了一次包加载。</para>
        /// </summary>
        UniTask PreloadAsync<T>(string assetPath, CancellationToken cancellationToken = default)
            where T : UIPanelBase;

        /// <summary>
        /// 忘掉指定面板的预加载记账，使其可被再次 <see cref="PreloadAsync{T}"/>。
        /// <para><b>只清记账，不卸载资源</b>。要真正释放内存，请用
        /// <c>AssetManager.UnloadUnusedAssetsAsync()</c>，并注意对象池里只要还留着闲置实例，
        /// 该预制体的引用计数就不会归零。</para>
        /// </summary>
        void ForgetPreload<T>() where T : UIPanelBase;

        /// <summary>
        /// 清空全部预加载记账。
        /// <para><b>只清记账，不卸载资源</b>——理由同 <see cref="ForgetPreload{T}"/>。</para>
        /// </summary>
        void ClearPreloads();

        /// <summary>
        /// 真正释放某个面板预制体占用的资源。
        /// <para>与 <see cref="ForgetPreload{T}"/>（只清记账）互补：本方法先清掉该地址的闲置实例池，
        /// 再触发被释放资源的回收，最后清掉指向该地址的预加载记账。</para>
        /// <para><b>为什么必须先清池</b>：回池时实例会保留 <c>AssetHandle</c> 保活资源，池里只要还留着
        /// 一个闲置实例，该预制体的引用计数就不会归零，回收也就带不走它。</para>
        /// </summary>
        /// <param name="assetPath">面板预制体的资源地址（与 OpenAsync 时传入的一致）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>确实执行了释放返回 true；该地址仍有面板打开时返回 false。</returns>
        UniTask<bool> UnloadPanelAssetAsync(string assetPath, CancellationToken cancellationToken = default);

        #endregion

        #region Tip & HUD Providers

        /// <summary>
        /// 显示一个临时提示文本（Tip）。
        /// <para>播放由统一的每帧通路推进，故返回的 UniTask 在「已创建并开始播放」时完成，
        /// 不等播放结束——那样 Tip 才能与面板一同受暂停与档位调度约束。</para>
        /// </summary>
        /// <param name="text">显示文字。</param>
        /// <param name="config">显示配置。</param>
        /// <param name="cancellationToken">取消令牌，覆盖实例化阶段。</param>
        UniTask ShowTipAsync(string text, TipConfig config = default, CancellationToken cancellationToken = default);

        /// <summary>
        /// 设置 Tip 提供者。传 null 恢复默认 <see cref="UITipManagerImpl"/>。
        /// </summary>
        /// <param name="provider">自定义提供者，或 null 恢复默认。</param>
        void SetTipProvider(IUITipProvider provider);

        /// <summary>
        /// 为 3D 目标附加一个 HUD（名字、血条等）。
        /// <para>HUD 每帧跟随目标的屏幕位置；目标丢失时自动回收。同一目标同时只绑定一个 HUD。</para>
        /// </summary>
        /// <typeparam name="T">HUD 类型。</typeparam>
        /// <param name="target">要跟随的 3D 目标。</param>
        /// <param name="assetPath">HUD 预制体的资源地址。</param>
        /// <param name="offset">屏幕坐标偏移（像素）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>附加的 HUD 实例；失败时为 null。</returns>
        UniTask<T> ShowHudAsync<T>(Transform target, string assetPath, Vector2? offset = null,
            CancellationToken cancellationToken = default) where T : UIHudItem;

        /// <summary>
        /// 分离指定目标绑定的 HUD。
        /// </summary>
        /// <param name="target">3D 目标；为 null 时不执行任何操作。</param>
        void HideHud(Transform target);

        /// <summary>
        /// 设置 HUD 提供者。传 null 恢复默认 <see cref="UIHudManagerImpl"/>。
        /// </summary>
        /// <param name="provider">自定义提供者，或 null 恢复默认。</param>
        void SetHudProvider(IUiHudProvider provider);

        #endregion

        #region Diagnostics

        /// <summary>
        /// 取一次状态快照（零分配）。
        /// <para>未初始化时返回全零——这是探测用的接口，不该因为尚未初始化就抛异常。</para>
        /// </summary>
        UIStateSnapshot GetState();

        /// <summary>
        /// 导出可直接阅读的状态快照，含每个面板的类型、层级、排序、档位与焦点/暂停状态。
        /// <para>低频调试接口，<b>允许分配</b>；不要放进每帧路径。</para>
        /// </summary>
        string DumpState();

        #endregion

        #region Query

        /// <summary>
        /// 当前已打开的面板数。
        /// <para>未初始化时返回 0，不抛异常（便于在场景加载早期探测）。</para>
        /// </summary>
        int OpenCount { get; }

        /// <summary>
        /// 是否有任何面板打开。等价于 <c>OpenCount &gt; 0</c>，但不需要分配查询结果。
        /// </summary>
        bool IsAnyOpen { get; }

        /// <summary>
        /// 显示栈顶部的面板（即最靠前的那个）；无面板时为 null。
        /// </summary>
        UIPanelBase GetTopPanel();

        /// <summary>
        /// 已打开的面板，按显示次序（底 → 顶）。
        /// <para>这是<strong>活视图</strong>：面板开合后内容随之变化，不要跨帧缓存它。读取本身不分配。</para>
        /// </summary>
        IReadOnlyList<UIPanelBase> Panels { get; }

        /// <summary>
        /// 把已打开的面板按显示次序写入调用方提供的缓冲区，返回写入数量。
        /// <para>零分配的主入口：由调用方持有缓冲区即可完全避免每帧 GC。缓冲区会先被清空。</para>
        /// </summary>
        /// <param name="buffer">接收结果的缓冲区，不能为 null。</param>
        /// <returns>写入的面板数量。</returns>
        int CopyPanels(List<UIPanelBase> buffer);

        /// <summary>
        /// 把指定层已打开的面板按显示次序写入缓冲区，返回写入数量。
        /// </summary>
        /// <param name="layer">目标层级。</param>
        /// <param name="buffer">接收结果的缓冲区，不能为 null。</param>
        /// <returns>写入的面板数量。</returns>
        int CopyPanelsInLayer(int layer, List<UIPanelBase> buffer);

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
        /// 每帧驱动注册进 <see cref="XUpdate.UpdateManager"/> 的统一调度——因此它受档位降频与
        /// <see cref="XUpdate.UpdateManager.Pause"/> 的统一约束，也不再要求场景里存在 <c>UIRootNode</c>。</para>
        /// </summary>
        /// <param name="deltaTime">距上次派发的时间差。</param>
        /// <param name="time">当前时刻。</param>
        void Update(float deltaTime, float time);

        #endregion

    }
}
