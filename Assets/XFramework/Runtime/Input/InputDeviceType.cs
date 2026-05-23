namespace XFramework.XInput
{
    /// <summary>
    /// 当前活跃的输入设备类型，用于 UI 自动切换键盘/手柄提示图标。
    /// </summary>
    public enum InputDeviceType : byte
    {
        /// <summary>未检测到输入设备</summary>
        None = 0,

        /// <summary>键盘或鼠标</summary>
        KeyboardMouse = 1,

        /// <summary>游戏手柄</summary>
        Gamepad = 2,

        /// <summary>触摸屏</summary>
        Touch = 3
    }
}