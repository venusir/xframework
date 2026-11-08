using System;
using XFramework.XUI;

namespace XFramework.XUI.Authoring
{
    /// <summary>
    /// 一条声明式动作。所有参数放在同一个可序列化类型里，按 <see cref="Kind"/> 取用。
    /// </summary>
    [Serializable]
    public sealed class UIAction
    {
        /// <summary>动作种类。</summary>
        public UIActionKind Kind;

        /// <summary>目标地址（Open / Push / Replace / ShowDialog）。</summary>
        public string AssetPath;

        /// <summary>目标层（Open / Push / Replace）。</summary>
        public int Layer = UILayers.Default;

        /// <summary>节点名（SetActive）。</summary>
        public string NodeName;

        /// <summary>布尔值（SetActive / SetLayerVisibility / SetLayerInteractive）。</summary>
        public bool BoolValue;

        /// <summary>页签索引（SelectTab）。</summary>
        public int Index;

        /// <summary>Tip 文本（ShowTip）。</summary>
        public string Text;

        /// <summary>Tip 配置（ShowTip）。</summary>
        public UITipActionConfig TipConfig = new UITipActionConfig();

        /// <summary>自定义动作 ID（Custom / ShowDialog 的结果键）。</summary>
        public string ActionId;

        /// <summary>自定义负载（Custom）。</summary>
        public string Payload;

        /// <summary>对话框结果 ID（CloseWithResult）。</summary>
        public string ResultId;

        /// <summary>页签组名（SelectTab）。</summary>
        public string GroupName;
    }
}
