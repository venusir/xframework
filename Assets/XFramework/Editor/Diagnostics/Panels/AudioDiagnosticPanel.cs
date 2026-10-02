using System.Collections.Generic;
using XFramework.XAudio;
using XFramework.XDiagnostics;

namespace XFramework.Editor.Diagnostics.Panels
{
    /// <summary>
    /// Audio 页签：主音量/静音、在播路数与通道表。
    /// <para>回答「声音听不见」的第一问——通道被静音或音量为 0 是最常见的现场。</para>
    /// <para><b>已知限制</b>：只列已创建的通道（通道在首次于其上播放时才惰性创建）；
    /// 逐条 voice 明细（哪一路在播什么）门面没有暴露，本页签只给每通道的在播路数。</para>
    /// </summary>
    internal sealed class AudioDiagnosticPanel : IDiagnosticPanel
    {
        #region Private Fields

        /// <summary>复用缓冲区：本页签是单例，一次分配长期使用。</summary>
        private readonly List<AudioChannelInfo> _channels = new List<AudioChannelInfo>();

        #endregion

        #region IDiagnosticPanel

        /// <inheritdoc />
        public string Title => "Audio";

        /// <inheritdoc />
        public int Order => DiagnosticOrders.Framework + 50;

        /// <inheritdoc />
        public void Collect(IDiagnosticReport report)
        {
            report.Section("概览");
            report.KeyValue("已初始化", AudioManager.IsInitialized ? "是" : "否");

            if (!AudioManager.IsInitialized)
            {
                report.Notice(DiagnosticLevel.Info, "AudioManager 尚未初始化：进入播放模式后这里才有数据。");
                return;
            }

            report.KeyValue("主音量", AudioManager.MasterVolume.ToString("F2"));
            report.KeyValue("主静音", AudioManager.MasterMuted ? "是" : "否");
            report.KeyValue("暂停", AudioManager.IsPaused ? "是" : "否");
            report.KeyValue("在播总路数", AudioManager.ActiveVoiceCount.ToString());

            AudioManager.CopyChannels(_channels);

            report.Section("通道（已创建的）");
            if (_channels.Count == 0)
            {
                report.Text("(还没有任何通道被创建——通道在首次播放时惰性创建)");
                return;
            }

            var table = report.BeginTable(null, "通道", "音量", "静音", "在播");
            for (int i = 0; i < _channels.Count; i++)
            {
                var channel = _channels[i];
                table.AddRow(
                    channel.Channel,
                    channel.Volume.ToString("F2"),
                    channel.IsMuted ? "是" : "-",
                    channel.ActiveVoiceCount.ToString());
            }
        }

        #endregion
    }
}
