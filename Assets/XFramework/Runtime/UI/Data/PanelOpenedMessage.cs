using System;

namespace XFramework.XUI.Data
{
    /// <summary>
    /// 面板打开消息。由 <see cref="UIManagerImpl"/> 在面板打开完成时通过 <see cref="XMessage.MessageManager.Publish{TMessage}(TMessage)"/> 发送。
    /// <para>任意模块可通过 <c>MessageManager.Subscribe&lt;PanelOpenedMessage&gt;()</c> 监听。</para>
    /// <para>使用 <see langword="readonly struct"/> 避免 GC 分配。</para>
    /// </summary>
    public readonly struct PanelOpenedMessage
    {
        /// <summary>
        /// 面板类型。
        /// </summary>
        public readonly Type PanelType;

        /// <summary>
        /// 创建面板打开消息。
        /// </summary>
        /// <param name="panelType">面板类型。</param>
        public PanelOpenedMessage(Type panelType)
        {
            PanelType = panelType;
        }
    }
}