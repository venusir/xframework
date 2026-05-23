using System;

namespace XFramework.XUI.Data
{
    /// <summary>
    /// 面板关闭消息。由 <see cref="UIManagerImpl"/> 在面板关闭完成时通过 <see cref="XReactive.MessageManager.Publish"/> 发送。
    /// <para>任意模块可通过 <c>MessageManager.Subscribe<PanelClosedMessage>()</c> 监听。</para>
    /// <para>使用 <see langword="readonly struct"/> 避免 GC 分配。</para>
    /// </summary>
    public readonly struct PanelClosedMessage
    {
        /// <summary>
        /// 面板类型。
        /// </summary>
        public readonly Type PanelType;

        public PanelClosedMessage(Type panelType)
        {
            PanelType = panelType;
        }
    }
}