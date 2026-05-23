namespace XFramework.XInput.Messages
{
    /// <summary>
    /// 手柄类型变化消息。可通过 <c>XReactive.MessageManager.Subscribe<GamepadTypeChangedMessage>(handler)</c> 监听。
    /// </summary>
    public struct GamepadTypeChangedMessage
    {
        /// <summary>变化前的手柄类型</summary>
        public readonly GamepadType PreviousType;

        /// <summary>变化后的手柄类型</summary>
        public readonly GamepadType CurrentType;

        public GamepadTypeChangedMessage(GamepadType previous, GamepadType current)
        {
            PreviousType = previous;
            CurrentType = current;
        }
    }
}