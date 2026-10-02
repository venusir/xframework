using System;

namespace XFramework.XUpdate
{
    /// <summary>
    /// 一个已注册更新节点的状态（诊断用只读快照）。
    /// <para><b>回答什么问题</b>：现在到底有哪些节点在跑、各在哪个档位、谁被禁用了——「谁在每帧跑」
    /// 「为什么这个节点不更新」这类问题此前只能靠计数（<c>GetCount</c> / <c>TotalCount</c>）猜。</para>
    /// <para><b>怎么取</b>：<see cref="UpdateManager.CopyNodes(System.Collections.Generic.List{UpdateNodeInfo})"/>。
    /// 暴露的是节点的**类型**，不是节点实例。</para>
    /// </summary>
    public readonly struct UpdateNodeInfo
    {
        /// <summary>节点类型（注册者实现的类型）。</summary>
        public readonly Type NodeType;

        /// <summary>派发时机（Update / LateUpdate / FixedUpdate 三套调度器之一）。</summary>
        public readonly UpdateTiming Timing;

        /// <summary>
        /// 时间轴（受时间缩放影响 / 不受）。
        /// <para><b>FixedUpdate 时机下无意义</b>：固定步轴自带 Scaled 占位，判断以 <see cref="Timing"/> 为准。</para>
        /// </summary>
        public readonly UpdateTimeMode TimeMode;

        /// <summary>当前档位。</summary>
        public readonly UpdateTier Tier;

        /// <summary>是否启用（false 的节点在禁用表里等待重新启用）。</summary>
        public readonly bool IsEnabled;

        /// <summary>同档位内的派发次序。</summary>
        public readonly int Order;

        internal UpdateNodeInfo(Type nodeType, UpdateTiming timing, UpdateTimeMode timeMode,
            UpdateTier tier, bool isEnabled, int order)
        {
            NodeType = nodeType;
            Timing = timing;
            TimeMode = timeMode;
            Tier = tier;
            IsEnabled = isEnabled;
            Order = order;
        }

        /// <summary>单行摘要，形如 <c>FooNode(Update, Tier0, 启用)</c>。</summary>
        public override string ToString()
            => $"{NodeType?.Name}({Timing}, {Tier}, {(IsEnabled ? "启用" : "禁用")})";
    }
}
