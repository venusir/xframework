using System;
using System.Collections.Generic;
using UnityEngine;

namespace XFramework.XUI.Authoring
{
    /// <summary>
    /// 声明式 UI 定义资产：文本、按钮动作、页签。面板与对话框共用。
    /// </summary>
    [CreateAssetMenu(menuName = "XFramework/UI Authoring/Panel Definition", fileName = "UIPanelDefinition")]
    public sealed class UIPanelDefinition : ScriptableObject
    {
        /// <summary>定义种类；必须与预制体根组件一致。</summary>
        public UIPanelDefinitionKind Kind;

        /// <summary>序列化版本；只在末尾追加字段。</summary>
        public int Version = 1;

        /// <summary>文本绑定。</summary>
        public List<UITextBinding> TextBindings = new List<UITextBinding>();

        /// <summary>按钮动作绑定。</summary>
        public List<UIActionBinding> ActionBindings = new List<UIActionBinding>();

        /// <summary>页签组。</summary>
        public List<UITabGroup> TabGroups = new List<UITabGroup>();

        /// <summary>数据绑定（阶段 3）：节点 ← <see cref="IUIDataProvider"/> 的数据键。</summary>
        public List<UIDataBinding> DataBindings = new List<UIDataBinding>();
    }
}
