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
        private readonly bool _restoreEnabled;

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
        /// <para>重绑定要求 action 处于禁用态,StartRebinding 会临时禁用;restoreEnabled 为 true 时,
        /// 操作完成/取消后恢复 action 启用态,保证游戏重绑定 UI 场景下输入不中断。</para>
        /// </summary>
        /// <param name="rebinding">已启动的 Unity 重绑定操作</param>
        /// <param name="action">对应的 InputAction（用于在完成时构建 InputBindingInfo）</param>
        /// <param name="bindingIndex">要被覆盖的绑定索引</param>
        /// <param name="bindingId">要被覆盖的绑定唯一标识</param>
        /// <param name="restoreEnabled">重绑定结束后是否恢复 action 的启用态</param>
        public SystemRebindingOperation(
            InputActionRebindingExtensions.RebindingOperation rebinding,
            InputAction action,
            int bindingIndex,
            string bindingId,
            bool restoreEnabled)
        {
            _rebinding = rebinding ?? throw new ArgumentNullException(nameof(rebinding));
            _restoreEnabled = restoreEnabled;

            // ---- 完成回调 ----
            _rebinding.OnComplete(op =>
            {
                op.Dispose();
                if (_restoreEnabled) action.Enable();

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
                if (_restoreEnabled) action.Enable();
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