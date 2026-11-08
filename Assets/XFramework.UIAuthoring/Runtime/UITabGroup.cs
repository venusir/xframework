using System;
using System.Collections.Generic;

namespace XFramework.XUI.Authoring
{
    /// <summary>
    /// 一组页签节点；同一时刻只显示其中一个。
    /// </summary>
    [Serializable]
    public sealed class UITabGroup
    {
        /// <summary>组名（动作 <see cref="UIActionKind.SelectTab"/> 使用）。</summary>
        public string GroupName;

        /// <summary>页签节点名列表。</summary>
        public List<string> NodeNames = new List<string>();
    }
}
