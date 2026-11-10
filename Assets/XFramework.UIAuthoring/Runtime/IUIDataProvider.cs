using XFramework.XReactive;

namespace XFramework.XUI.Authoring
{
    /// <summary>
    /// 声明式 UI 的数据源接缝：按数据键提供强类型响应式属性。
    /// <para>项目只写一个实现并注册到 <see cref="UIDataProviderRegistry"/>；面板 / HUD / 对话框
    /// 在打开时按 <see cref="UIPanelDefinition.DataBindings"/> 解析键并订阅。值变化由
    /// <see cref="IReactiveProperty{T}"/> 的通知机制推送到目标组件，本接缝不轮询、不持有视图。</para>
    /// <para><b>线程契约</b>：只在 Unity 主线程调用。</para>
    /// <para><b>失败语义</b>：键不存在或类型不匹配时返回 <c>false</c>；实现不应为了「找不到」
    /// 抛异常。实现抛出的异常由注册表捕获并记 Warning，不会反向打崩绑定链路。</para>
    /// </summary>
    public interface IUIDataProvider
    {
        /// <summary>
        /// 尝试按键取响应式属性。
        /// </summary>
        /// <typeparam name="T">期望的值类型（文本 <c>string</c>、图片 <c>Sprite</c>、
        /// 颜色 <c>Color</c>、填充/滑条 <c>float</c>、开关/显隐 <c>bool</c>）。</typeparam>
        /// <param name="key">数据键，来自定义资产的 <see cref="UIDataBinding.DataKey"/>。</param>
        /// <param name="source">取到的属性；返回 <c>false</c> 时为 <c>null</c>。</param>
        /// <returns>键存在且类型匹配返回 <c>true</c>。</returns>
        bool TryGet<T>(string key, out IReactiveProperty<T> source);
    }
}
