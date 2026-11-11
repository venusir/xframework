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
        /// <summary>
        /// 按钮节点名（<b>该节点自身</b>必须有 <c>Button</c>）。
        /// <para>绑定与校验器都只查节点上的 <c>GetComponent&lt;Button&gt;()</c>；把 <c>Button</c> 放在父节点上
        /// 会静默不绑定。</para>
        /// </summary>
        public string ButtonNodeName;

        /// <summary>按顺序执行的动作链。</summary>
        public List<UIAction> Actions = new List<UIAction>();
    }
}
