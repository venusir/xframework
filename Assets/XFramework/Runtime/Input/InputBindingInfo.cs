namespace XFramework.XInput
{
    /// <summary>
    /// 输入绑定信息。用于 UI 展示当前按键提示。
    /// <para>与具体底层实现（Unity Input System / Rewired）解耦的平台无关结构体。</para>
    /// </summary>
    public struct InputBindingInfo
    {
        /// <summary>
        /// 绑定唯一标识（Unity Input System 为 binding id，Rewired 为 element map id）。
        /// </summary>
        public string Id;

        /// <summary>
        /// 人类可读的显示名称，如 "W"、"左摇杆上"、"X 按钮"。
        /// </summary>
        public string DisplayName;

        /// <summary>
        /// 设备分组，如 "Keyboard&Mouse"、"Gamepad"、"Touch"。
        /// </summary>
        public string Group;

        /// <summary>
        /// 是否为复合绑定（如 WASD 组合）。
        /// </summary>
        public bool IsComposite;

        /// <summary>
        /// 是否为复合绑定的子项。
        /// </summary>
        public bool IsPartOfComposite;

        /// <summary>
        /// 该绑定是否已被用户覆盖（非默认值）。UI 可据此显示"重置"按钮。
        /// </summary>
        public bool IsOverridden;
    }
}
