using System;

namespace XFramework.XUI.Authoring
{
    /// <summary>
    /// 一条数据绑定：把节点上的目标组件订阅到 <see cref="IUIDataProvider"/> 的一个数据键。
    /// <para>与 <see cref="UITextBinding"/> 的静态文本互斥：同一节点不要同时出现在
    /// <see cref="UIPanelDefinition.TextBindings"/> 与 <see cref="UIPanelDefinition.DataBindings"/>，
    /// 否则数据绑定会覆盖静态文本（编辑器给 Warning）。</para>
    /// </summary>
    [Serializable]
    public sealed class UIDataBinding
    {
        /// <summary>节点名（与预制体子树中的节点名一致，最多 5 层）。</summary>
        public string NodeName;

        /// <summary>绑定种类；决定目标组件与期望的响应式值类型。</summary>
        public UIDataBindingKind Kind;

        /// <summary>数据键；由项目的 <see cref="IUIDataProvider"/> 解释。</summary>
        public string DataKey;

        /// <summary>
        /// 是否双向。仅 <see cref="UIDataBindingKind.Slider"/> / <see cref="UIDataBindingKind.Toggle"/> 有效；
        /// 源不是 <c>IReactivePropertyWriter&lt;T&gt;</c> 时降级为单向并记 Warning。
        /// </summary>
        public bool TwoWay;

        /// <summary>
        /// 文本格式化串（仅 <see cref="UIDataBindingKind.Text"/> 有效），按 <c>string.Format</c> 规则使用，
        /// 例如 <c>"HP {0}"</c>。为空时直接显示源值。
        /// </summary>
        public string Format;
    }
}
