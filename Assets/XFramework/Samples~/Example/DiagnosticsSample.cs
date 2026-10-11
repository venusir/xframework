using System.Collections.Generic;
using UnityEngine;
using XFramework.XDiagnostics;

namespace XFramework.Example
{
    /// <summary>
    /// 诊断页签示例：给自己游戏的状态加一个页签，出现在
    /// 「Tools/XFramework/Diagnostics」窗口里。
    /// <para><b>三步</b>：实现 <see cref="IDiagnosticPanel"/> → 在初始化路径里
    /// <see cref="DiagnosticsManager.Register"/> → 在 <see cref="Collect"/> 里往报告写内容。</para>
    /// <para>框架自己的页签走的是同一条路径，没有特例；写侧契约见 <see cref="IDiagnosticPanel"/> 的文档
    /// （主线程、可重复调用、无副作用、不得抛）。</para>
    /// </summary>
    public sealed class GameDiagnosticPanel : IDiagnosticPanel
    {
        public string Title => "我的游戏";

        // 排在全部框架页签之后
        public int Order => DiagnosticOrders.User;

        public void Collect(IDiagnosticReport report)
        {
            report.Section("状态");
            report.KeyValue("帧率", Application.targetFrameRate.ToString());
            report.KeyValue("运行时长", Time.realtimeSinceStartup.ToString("F1") + " 秒");

            report.Section("示例列表");
            var table = report.BeginTable("背包", "物品", "数量");
            for (int i = 0; i < 3; i++)
                table.AddRow("示例物品 " + i, (i * 2).ToString());
        }
    }

    /// <summary>把示例页签登记进诊断窗口。</summary>
    public static class GameDiagnosticPanelRegistration
    {
        /// <summary>
        /// 用 <see cref="RuntimeInitializeOnLoadMethodAttribute"/> 在进播放时登记。
        /// <para>想在**未进入播放模式**时也看到页签，改用
        /// <c>#if UNITY_EDITOR [UnityEditor.InitializeOnLoadMethod] #endif</c>（并保证 Collect 能安静地
        /// 处理「模块未初始化」）——登记是幂等的，两条时序都不会产生重复页签。</para>
        /// <para>运行时登记的页签请在退出路径里 <see cref="DiagnosticsManager.Unregister"/>：
        /// 关闭域重载时静态登记表跨播放会话存活。</para>
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Register()
        {
            DiagnosticsManager.Register(new GameDiagnosticPanel());
        }
    }
}
