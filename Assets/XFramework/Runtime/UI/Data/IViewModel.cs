using System;

namespace XFramework.XUI.Data
{
    /// <summary>
    /// ViewModel 接口。所有面板数据模型需实现此接口。
    /// <para>数据层可独立于 UI 模块使用，不依赖 <see cref="UIPanelBase"/>。</para>
    /// <para>面板关闭时调用 <see cref="IDisposable.Dispose"/> 释放所有 ReactiveProperty 订阅。</para>
    /// </summary>
    public interface IViewModel : IDisposable
    {
        /// <summary>
        /// 当 ViewModel 被绑定到面板时调用。
        /// </summary>
        void OnBound();

        /// <summary>
        /// 当面板关闭、ViewModel 解绑时调用。
        /// </summary>
        void OnUnbound();
    }
}