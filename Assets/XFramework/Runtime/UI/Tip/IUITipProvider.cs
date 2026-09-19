using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework.XUI
{
    /// <summary>
    /// Tip 提供者接口。第三方可实现此接口来替换 Tip 的展现方式（如使用 TextMeshPro、自定义特效等）。
    /// <para>默认实现为 <see cref="UITipManagerImpl"/>，通过 <see cref="UIManager.Tip.SetProvider"/> 注入。</para>
    /// </summary>
    public interface IUITipProvider
    {
        /// <summary>
        /// 设置 UI 根节点。在 <see cref="UIManager.Initialize"/> 时自动调用。
        /// </summary>
        /// <param name="uiRoot">UIRoot Transform。</param>
        void SetUIRoot(Transform uiRoot);

        /// <summary>
        /// 显示一个临时提示文本（Tip）。
        /// <para><b>返回的 UniTask 在「已创建并开始播放」时完成</b>，不等播放结束——播放由
        /// <see cref="Update"/> 逐帧推进，那样 Tip 才与面板一同受统一调度与暂停约束。
        /// 需要知道何时播完，请自行按 <see cref="TipConfig.Duration"/> 计时。</para>
        /// </summary>
        /// <param name="text">显示文字。</param>
        /// <param name="config">显示配置。</param>
        /// <param name="cancellationToken">取消令牌，覆盖实例化阶段。</param>
        UniTask ShowTipAsync(string text, TipConfig config = default, CancellationToken cancellationToken = default);

        /// <summary>
        /// 由 <see cref="UIManager.Update"/> 调用，驱动所有在播 Tip 的每帧推进。
        /// <para>于是 Tip 与面板、HUD 共用同一条帧通路：可被档位降频、被
        /// <c>UpdateManager.Pause</c> 统一暂停。</para>
        /// </summary>
        /// <param name="deltaTime">距上次派发的间隔。</param>
        /// <param name="time">当前时刻。</param>
        void Update(float deltaTime, float time);

        /// <summary>
        /// 回收全部在播 Tip（含在途实例化）。
        /// <para>销毁或更换 UIRoot 前由管理器调用；在途实例化完成后会看到该状态并立即回池，
        /// 不会在管理器拆除后凭空冒出一个无人回收的 Tip。</para>
        /// </summary>
        void DetachAll();
    }
}