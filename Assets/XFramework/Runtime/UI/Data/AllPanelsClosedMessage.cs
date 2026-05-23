using System;

namespace XFramework.XUI.Data
{
    /// <summary>
    /// 所有面板关闭消息。由 <see cref="UIManagerImpl"/> 在执行 <see cref="IUIManager.CloseAllAsync"/> 完成后通过 <see cref="XReactive.MessageManager.Publish"/> 发送。
    /// <para>任意模块可通过 <c>MessageManager.Subscribe<AllPanelsClosedMessage>()</c> 监听。</para>
    /// <para>使用 <see langword="readonly struct"/> 避免 GC 分配。</para>
    /// </summary>
    public readonly struct AllPanelsClosedMessage
    {
    }
}