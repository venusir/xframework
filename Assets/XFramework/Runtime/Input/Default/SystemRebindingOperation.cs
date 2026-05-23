using System;
using UnityEngine.InputSystem;

namespace XFramework.XInput.Default
{
    /// <summary>
    /// Unity Input System 的 <see cref="IRebindingOperation"/> 实现。
    /// <para>包装 <c>UnityEngine.InputSystem.InputActionRebindingExtensions.RebindingOperation</c>。</para>
    /// </summary>
    internal sealed class SystemRebindingOperation : IRebindingOperation
    {
        #region Private Fields

        private readonly InputActionRebindingExtensions.RebindingOperation _rebinding;

        #endregion

        #region Events

        public event Action<InputBindingInfo> OnCompleted;
        public event Action OnCancelled;
        public event Action<string> OnPotentialMatch;

        #endregion

        #region Constructor

        /// <summary>
        /// 创建一个 SystemRebindingOperation 实例。
        /// <para>传入一个已经 <c>.Start()</c> 的 RebindingOperation 和当前 Action 引用。</para>
        /// </summary>
        /// <param name="rebinding">已启动的 Unity 重绑定操作</param>
        /// <param name="action">对应的 InputAction（用于在完成时构建 InputBindingInfo）</param>
        /// <param name="bindingIndex">要被覆盖的绑定索引</param>
        /// <param name="bindingId">要被覆盖的绑定唯一标识</param>
        public SystemRebindingOperation(
            InputActionRebindingExtensions.RebindingOperation rebinding,
            InputAction action,
            int bindingIndex,
            string bindingId)
        {
            _rebinding = rebinding ?? throw new ArgumentNullException(nameof(rebinding));

            // ---- 完成回调 ----
            _rebinding.OnComplete(op =>
            {
                op.Dispose();

                var newBindingInfo = new InputBindingInfo
                {
                    Id = bindingId ?? string.Empty,
                    DisplayName = action.GetBindingDisplayString(bindingIndex) ?? string.Empty,
                    Group = action.bindings[bindingIndex].groups,
                    IsComposite = action.bindings[bindingIndex].isComposite,
                    IsPartOfComposite = action.bindings[bindingIndex].isPartOfComposite,
                    IsOverridden = true
                };

                OnCompleted?.Invoke(newBindingInfo);
            });

            // ---- 取消回调 ----
            _rebinding.OnCancel(op =>
            {
                op.Dispose();
                OnCancelled?.Invoke();
            });

            // ---- 可能匹配回调（实时预览按键名）----
            _rebinding.OnPotentialMatch(op =>
            {
                if (op.selectedControl != null)
                {
                    OnPotentialMatch?.Invoke(op.selectedControl.displayName);
                }
            });
        }

        #endregion

        #region Properties

        public bool IsActive => _rebinding != null && _rebinding.started;

        #endregion

        #region Cancel

        public void Cancel()
        {
            _rebinding?.Cancel();
        }

        #endregion
    }
}