using System;

namespace XFramework.XUI.Data
{
    /// <summary>
    /// 所有面板关闭消息。由 <see cref="UIManagerImpl"/> 在执行 <see cref="IUIManager.CloseAllAsync"/> 完成后通过 <see cref="XMessage.MessageManager.Publish{TMessage}(TMessage)"/> 发送。
    /// <para><b>只在「本次确实发起过关闭、且结束时 <c>OpenCount == 0</c>」时发布</b>：没有面板、
    /// 或 <see cref="Controller.IUIController.OnBeforeCloseAsync"/> 拦下全部关闭时都不发布
    /// （R3-ADJ-13 / R4-UIB-003，与 <see cref="Controller.IUIController.OnAllPanelsClosedAsync"/> 同门控）。</para>
    /// <para>任意模块可通过 <c>MessageManager.Subscribe&lt;AllPanelsClosedMessage&gt;()</c> 监听。</para>
    /// <para>使用 <see langword="readonly struct"/> 避免 GC 分配。</para>
    /// </summary>
    public readonly struct AllPanelsClosedMessage
    {
    }
}