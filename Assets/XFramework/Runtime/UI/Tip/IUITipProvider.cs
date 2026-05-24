using UnityEngine;

namespace XFramework.XUI
{
    /// <summary>
    /// Tip 提供者接口。第三方可实现此接口来替换 Tip 的展现方式（如使用 TextMeshPro、自定义特效等）。
    /// <para>默认实现为 <see cref="UITipManagerImpl"/>，通过 <see cref="UIManager.SetTipProvider"/> 注入。</para>
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
        /// <para>默认实现会自动管理实例化和回池。</para>
        /// </summary>
        /// <param name="text">显示文字。</param>
        /// <param name="config">显示配置。</param>
        void ShowTip(string text, TipConfig config = default);
    }
}