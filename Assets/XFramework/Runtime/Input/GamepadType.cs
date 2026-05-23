namespace XFramework.XInput
{
    /// <summary>
    /// 手柄类型枚举。由 <see cref="IInputProvider"/> 实现自动检测。
    /// <para>用于 UI 显示对应的按键图标（如 PS5 的 □ △ ○ × vs Xbox 的 A B X Y）。</para>
    /// </summary>
    public enum GamepadType : byte
    {
        /// <summary>未检测到手柄</summary>
        None = 0,

        /// <summary>Xbox 系列手柄</summary>
        Xbox = 1,

        /// <summary>PlayStation 4 (DualShock 4)</summary>
        PlayStation4 = 2,

        /// <summary>PlayStation 5 (DualSense)</summary>
        PlayStation5 = 3,

        /// <summary>Nintendo Switch Pro 手柄</summary>
        SwitchPro = 4,

        /// <summary>无法识别的通用手柄</summary>
        Generic = 5
    }
}