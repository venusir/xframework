namespace XFramework.Samples.Integration
{
    /// <summary>设置页点击「应用」后发布的消息（示例自定义消息类型）。</summary>
    public readonly struct SettingsAppliedMessage
    {
        /// <summary>主音量（0..1）。</summary>
        public readonly float MasterVolume;

        /// <summary>是否显示提示。</summary>
        public readonly bool ShowHints;

        /// <summary>构造消息。</summary>
        public SettingsAppliedMessage(float masterVolume, bool showHints)
        {
            MasterVolume = masterVolume;
            ShowHints = showHints;
        }
    }

    /// <summary>请求退出游戏。由任意面板发布，<see cref="AppFlowController"/> 统一收敛退出流程。</summary>
    public readonly struct ExitRequestedMessage
    {
    }
}
