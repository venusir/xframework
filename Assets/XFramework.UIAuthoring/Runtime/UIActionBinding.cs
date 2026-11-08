using System;
using System.Collections.Generic;

namespace XFramework.XUI.Authoring
{
    /// <summary>
    /// 一个按钮节点与其动作链。
    /// </summary>
    [Serializable]
    public sealed class UIActionBinding
    {
        /// <summary>按钮节点名（该节点或其根上必须有 <c>Button</c>）。</summary>
        public string ButtonNodeName;

        /// <summary>按顺序执行的动作链。</summary>
        public List<UIAction> Actions = new List<UIAction>();
    }
}
