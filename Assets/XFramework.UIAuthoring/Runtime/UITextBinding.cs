using System;

namespace XFramework.XUI.Authoring
{
    /// <summary>
    /// 一条文本绑定：把节点文本设为字面量或本地化 key。
    /// </summary>
    [Serializable]
    public sealed class UITextBinding
    {
        /// <summary>节点名（与预制体子树中的节点名一致）。</summary>
        public string NodeName;

        /// <summary>字面量文本。</summary>
        public string Literal;

        /// <summary>本地化 key。</summary>
        public string LocalizationKey;

        /// <summary>是否按本地化 key 绑定（需要 TMP_Text 或 UGUI Text）。</summary>
        public bool UseLocalization;
    }
}
