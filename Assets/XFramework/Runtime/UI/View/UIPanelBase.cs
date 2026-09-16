using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XReactive;
using XFramework.XUI.Data;

namespace XFramework.XUI.View
{
    /// <summary>
    /// UI 面板基类。继承自 <see cref="UIViewBase"/>，所有面板需继承此类。
    /// <para>面板生命周期由 <see cref="UIManager"/> 驱动：OnOpen → OnFocus/OnBlur → OnClose。</para>
    /// <para>支持打开/关闭动画：重写 <see cref="PlayOpenAnimation"/> 和 <see cref="PlayCloseAnimation"/>。</para>
    /// <para>多语言刷新：重写 <see cref="OnLanguageChanged"/>，与 <see cref="XLocalization.LocalizationManager"/> 联动。</para>
    /// <para>MVVM 绑定：通过 <see cref="Binding"/> 组件与 <see cref="IViewModel"/> 绑定，详情参见 <see cref="UIPanelBinding"/>。</para>
    /// </summary>
    public abstract class UIPanelBase : UIViewBase
    {
        #region ViewModel Support

        /// <summary>
        /// 面板上的 UIPanelBinding 组件。懒加载，在 Awake 时自动获取。
        /// </summary>
        private UIPanelBinding _binding;

        /// <summary>
        /// 面板的 ViewModel 绑定组件。面板预制体上需挂载 <see cref="UIPanelBinding"/>。
        /// <para>如果预制体未挂载此组件，则返回 null。</para>
        /// </summary>
        public UIPanelBinding Binding
        {
            get
            {
                if (_binding == null)
                    _binding = GetComponent<UIPanelBinding>();
                return _binding;
            }
        }

        /// <summary>
        /// 绑定 ViewModel 到此面板。
        /// <para>通常在 <see cref="OnOpen"/> 中调用。内部调用 <see cref="UIPanelBinding.Bind"/>。</para>
        /// </summary>
        protected void BindViewModel(IViewModel viewModel)
        {
            if (Binding == null)
            {
                Debug.LogWarning($"[UIPanelBase] Cannot bind ViewModel: UIPanelBinding component not found on '{gameObject.name}'.");
                return;
            }
            Binding.Bind(viewModel);
        }

        /// <summary>
        /// 按命名约定绑定 ViewModel 的 ReactiveProperty 到 UI 组件。
        /// <para>约简化写法，内部转发到 <see cref="UIPanelBinding.BindByConvention{T}"/>。</para>
        /// </summary>
        protected void BindByConvention<T>(string propertyName, IReactiveProperty<T> source)
        {
            if (Binding == null)
            {
                Debug.LogWarning($"[UIPanelBase] Cannot bind by convention: UIPanelBinding component not found on '{gameObject.name}'.");
                return;
            }
            Binding.BindByConvention(propertyName, source);
        }

        #endregion

        #region Properties

        /// <summary>
        /// 面板是否处于焦点（在显示栈顶部、可交互）。
        /// <para>这是<strong>交互维度</strong>：失焦的面板仍会收到语言切换等生命周期回调。</para>
        /// </summary>
        public bool IsFocused { get; private set; }

        /// <summary>
        /// 面板是否被覆盖而暂停每帧更新。
        /// <para>这是<strong>更新维度</strong>：暂停只影响 <see cref="UIViewBase.OnUpdate"/> 的派发，
        /// 不影响 <c>IsOpen</c>、也不影响语言切换等回调。被 <see cref="XUpdate.UpdateManager"/>
        /// 的 LOD 派发路径消费。</para>
        /// <para>与 <see cref="IsFocused"/> 分开是因为二者并非总是同步：面板可能在失焦的同时
        /// 仍需按低频更新（倒计时），也可能在获得焦点时被层级的整体禁交互挡住。</para>
        /// </summary>
        public bool IsPaused { get; private set; }

        #endregion

        #region Lifecycle Methods (Overridden by Subclass)

        /// <summary>
        /// 面板打开时调用。子类必须实现此方法处理初始化逻辑。
        /// <para>此时面板已实例化完成，Canvas 已设置好 sorting order，打开动画已播放完毕。</para>
        /// </summary>
        /// <param name="userData">调用 OpenAsync/PushAsync 时传入的自定义数据。</param>
        /// <returns>支持 await。</returns>
        protected abstract UniTask OnOpen(object userData);

        /// <summary>
        /// 面板关闭时调用。子类必须实现此方法处理清理逻辑。
        /// <para>关闭动画完成后、实例回池前调用。</para>
        /// </summary>
        /// <returns>支持 await。</returns>
        protected abstract UniTask OnClose();

        /// <summary>
        /// 面板获得焦点时调用（回到显示栈顶部时，含首次打开）。
        /// <para>只管交互维度；更新维度的启停见 <see cref="OnResume"/> / <see cref="OnPause"/>。</para>
        /// <para><b>注意</b>：若所在层级被 <c>UIManager.Layer.SetInteractive(layer, false)</c> 整体禁用，
        /// 管理器会在本回调之后把射线重新关掉——层的整体开关优先于单个面板。</para>
        /// </summary>
        protected internal virtual void OnFocus()
        {
            IsFocused = true;
            Raycaster.enabled = true;
        }

        /// <summary>
        /// 面板失去焦点时调用（被 Push 的新面板覆盖时）。
        /// <para>只管交互维度；更新维度的启停见 <see cref="OnPause"/>。</para>
        /// </summary>
        protected internal virtual void OnBlur()
        {
            IsFocused = false;
            Raycaster.enabled = false;
        }

        /// <summary>
        /// 面板被覆盖、暂停每帧更新时调用。
        /// <para>只管更新维度：<c>IsOpen</c>、语言切换等都不受影响。</para>
        /// </summary>
        protected internal virtual void OnPause()
        {
            IsPaused = true;
        }

        /// <summary>
        /// 面板重新成为栈顶、恢复每帧更新时调用。
        /// </summary>
        protected internal virtual void OnResume()
        {
            IsPaused = false;
        }

        /// <summary>
        /// 语言切换时调用。子类可重写以刷新 UI 文本。
        /// <para>与 <see cref="XLocalization.LocalizationManager"/> 联动。</para>
        /// </summary>
        protected internal virtual void OnLanguageChanged(string lang) { }

        #endregion

        #region Animation Methods

        /// <summary>
        /// 打开动画。默认无动画。子类可重写以实现自定义打开动画（如 FadeIn、Scale）。
        /// </summary>
        /// <returns>一个 UniTask，动画结束后完成。</returns>
        protected internal virtual UniTask PlayOpenAnimation()
        {
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 关闭动画。默认无动画。子类可重写以实现自定义关闭动画（如 FadeOut、Scale）。
        /// </summary>
        /// <returns>一个 UniTask，动画结束后完成。</returns>
        protected internal virtual UniTask PlayCloseAnimation()
        {
            return UniTask.CompletedTask;
        }

        #endregion

        #region UIViewBase Implementation — Pool Recycle

        /// <summary>
        /// 面板即将回池：释放随视图登记的订阅，并解绑 ViewModel。
        /// <para>解绑必须挂在回池点而非关闭点——关闭是逻辑操作，可被
        /// <see cref="Controller.IUIController.OnBeforeCloseAsync"/> 返回 false 拦下，
        /// 此时面板仍然开着，解绑是误伤。</para>
        /// </summary>
        protected internal override void OnPoolRecycle()
        {
            base.OnPoolRecycle();

            // 优先用已缓存的引用，兜底 GetComponent：调用方可能绕过 Binding 属性直接
            // GetComponent<UIPanelBinding>().Bind(vm)，那时 _binding 仍是 null。
            // 回池不是每帧路径，这一次 GetComponent 可以接受。
            var binding = _binding != null ? _binding : GetComponent<UIPanelBinding>();
            binding?.Unbind();
        }

        #endregion

        #region Convenience Methods

        /// <summary>
        /// 关闭自身面板。便捷方法，内部调用 <see cref="UIManager.Panel.CloseAsync(UIPanelBase, bool)"/>。
        /// </summary>
        /// <param name="immediate">是否跳过关闭动画，直接回池。</param>
        public UniTask CloseSelfAsync(bool immediate = false)
        {
            if (!IsOpen)
                return UniTask.CompletedTask;

            return UIManager.Panel.CloseAsync(this, immediate);
        }

        #endregion

        #region UIViewBase Implementation — Bridge

        /// <summary>
        /// 框架内部打开入口。播放打开动画 → 调用 <see cref="OnOpen"/> → 恢复交互状态。
        /// </summary>
        protected sealed override async UniTask OnOpenImpl(object userData)
        {
            await PlayOpenAnimation();
            await OnOpen(userData);

            // 新打开的面板就在栈顶：不妨暂停。这一维直接置位而不走 OnResume——
            // 对一个从未暂停过的面板调「恢复」是语义错位，子类也会收到莫名其妙的回调。
            IsPaused = false;

            // 交互维度走 OnFocus，让「首次打开」与「Push/Pop 恢复焦点」是同一条路径。
            // 早先这里直接设字段，子类重写的 OnFocus 在首次打开时不会被调用。
            OnFocus();
        }

        /// <summary>
        /// 框架内部关闭入口。播放关闭动画（非 immediate 时）→ 调用 <see cref="OnClose"/>。
        /// </summary>
        protected sealed override async UniTask OnCloseImpl(bool immediate)
        {
            if (!immediate)
            {
                await PlayCloseAnimation();
            }
            await OnClose();
        }

        #endregion
    }
}