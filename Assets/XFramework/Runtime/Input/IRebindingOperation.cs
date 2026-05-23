using System;

namespace XFramework.XInput
{
    /// <summary>
    /// 交互式按键重绑定操作的句柄。
    /// <para>由 <see cref="IInputProvider.StartRebinding"/> 返回，用于等待用户按下物理按键/按钮完成绑定或取消。</para>
    /// <para>绑定过程中不阻塞调用线程，通过事件通知结果。</para>
    /// </summary>
    public interface IRebindingOperation
    {
        /// <summary>
        /// 绑定操作是否正在进行中。
        /// </summary>
        bool IsActive { get; }

        /// <summary>
        /// 绑定完成时触发（参数为新的绑定信息，包含新按键的显示名）。
        /// </summary>
        event Action<InputBindingInfo> OnCompleted;

        /// <summary>
        /// 绑定取消时触发。
        /// </summary>
        event Action OnCancelled;

        /// <summary>
        /// 可选：当用户在绑定过程中尝试按下某个键时预览触发（用于实时预览按键名）。
        /// <para>注意：Rewired 适配器可能不支持此功能，调用方需做空判断。</para>
        /// </summary>
        event Action<string> OnPotentialMatch;

        /// <summary>
        /// 取消当前绑定操作。触发 <see cref="OnCancelled"/> 事件。
        /// </summary>
        void Cancel();
    }
}