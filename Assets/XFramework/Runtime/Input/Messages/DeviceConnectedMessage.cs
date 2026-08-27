namespace XFramework.XInput.Messages
{
    /// <summary>
    /// 输入设备连接消息。可通过 <c>XReactive.MessageManager.Subscribe<DeviceConnectedMessage>(handler)</c> 监听。
    /// </summary>
    public struct DeviceConnectedMessage
    {
        /// <summary>设备显示名(如 "Xbox Controller")</summary>
        public readonly string DeviceName;

        /// <summary>设备运行时唯一 ID(同一物理设备热插拔后可能变化)</summary>
        public readonly int DeviceId;

        /// <summary>是否为手柄</summary>
        public readonly bool IsGamepad;

        public DeviceConnectedMessage(string deviceName, int deviceId, bool isGamepad)
        {
            DeviceName = deviceName;
            DeviceId = deviceId;
            IsGamepad = isGamepad;
        }
    }
}
