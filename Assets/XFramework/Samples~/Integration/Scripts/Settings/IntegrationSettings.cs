using System;

namespace XFramework.Samples.Integration
{
    /// <summary>
    /// 示例设置对象（纯 POCO）。
    /// <para><b>必须是字段而不是属性</b>：<c>JsonUtility</c> 只序列化字段；用属性会让「改了但没存」
    /// 静默发生。<c>SettingsManager.Ref</c> 也据此拒绝属性选择器。</para>
    /// <para>落盘为 JSON；启用版本化（<c>SettingsOptions.CurrentVersion = 1</c>）后是
    /// <c>{"Version":1,"Data":{...}}</c> 信封。</para>
    /// </summary>
    [Serializable]
    public sealed class IntegrationSettings
    {
        /// <summary>主音量，0..1。</summary>
        public float MasterVolume = 1f;

        /// <summary>BGM 音量，0..1。</summary>
        public float BgmVolume = 0.8f;

        /// <summary>音效音量，0..1。</summary>
        public float SfxVolume = 1f;

        /// <summary>是否显示操作提示。</summary>
        public bool ShowHints = true;

        /// <summary>当前语言标识。</summary>
        public string Language = "zh_Hans";

        /// <summary>玩家名（示例占位，用来演示字符串设置的落盘）。</summary>
        public string PlayerName = "Player";
    }
}
