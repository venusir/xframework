using UnityEngine;

namespace XFramework
{
    /// <summary>
    /// 叶子节点，不包含子节点。
    /// <para>作为节点树的末端节点使用，通常用于存储具体的数据或行为。</para>
    /// </summary>
    public class LeafNode : BaseNode
    {
        internal sealed override void AwakeInternal()
        {
            base.AwakeInternal();
        }

        internal sealed override void StartInternal()
        {
            base.StartInternal();
        }

        internal sealed override void DestroyInternal()
        {
            base.DestroyInternal();
        }
    }
}
