using System;
using UnityEngine.InputSystem;
using XFramework.XLog;

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

                NotifyCompleted(newBindingInfo);
            });

            // ---- 取消回调 ----
            _rebinding.OnCancel(op =>
            {
                op.Dispose();
                if (_restoreEnabled) action.Enable();
                NotifyCancelled();
            });

            // ---- 可能匹配回调（实时预览按键名）----
            _rebinding.OnPotentialMatch(op =>
            {
                if (op.selectedControl != null)
                {
                    NotifyPotentialMatch(op.selectedControl.displayName);
                }
            });
        }

        /// <summary>
        /// 逐个调用订阅者并隔离异常（INP-013）：与 <c>Observe*</c> 的处理器隔离对称。
        /// <para>多播委托直接 <c>Invoke</c> 时，第一个抛异常的订阅者会中断其后全部订阅者；
        /// 重绑定事件是低频路径，<c>GetInvocationList</c> 的分配可接受。</para>
        /// </summary>
        private void NotifyCompleted(InputBindingInfo info)
        {
            var handlers = OnCompleted;
            if (handlers == null) return;

            Delegate[] list = handlers.GetInvocationList();
            for (int i = 0; i < list.Length; i++)
            {
                try
                {
                    ((Action<InputBindingInfo>)list[i])(info);
                }
                catch (Exception ex)
                {
                    LogManager.Exception(LogCategories.Input, ex,
                        "Rebinding OnCompleted subscriber threw exception");
                }
            }
        }

        /// <inheritdoc cref="NotifyCompleted"/>
        private void NotifyCancelled()
        {
            var handlers = OnCancelled;
            if (handlers == null) return;

            Delegate[] list = handlers.GetInvocationList();
            for (int i = 0; i < list.Length; i++)
            {
                try
                {
                    ((Action)list[i])();
                }
                catch (Exception ex)
                {
                    LogManager.Exception(LogCategories.Input, ex,
                        "Rebinding OnCancelled subscriber threw exception");
                }
            }
        }

        /// <inheritdoc cref="NotifyCompleted"/>
        private void NotifyPotentialMatch(string displayName)
        {
            var handlers = OnPotentialMatch;
            if (handlers == null) return;

            Delegate[] list = handlers.GetInvocationList();
            for (int i = 0; i < list.Length; i++)
            {
                try
                {
                    ((Action<string>)list[i])(displayName);
                }
                catch (Exception ex)
                {
                    LogManager.Exception(LogCategories.Input, ex,
                        "Rebinding OnPotentialMatch subscriber threw exception");
                }
            }
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