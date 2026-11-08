using UnityEngine;

namespace XFramework.XUI.Authoring
{
    /// <summary>
    /// 按名字查找节点：根为第 0 层，最多 5 层（与 <c>UIPanelBinding.CacheComponentsRecursive</c> 对齐）。
    /// </summary>
    internal static class UIDeclarativeNodeFinder
    {
        internal const int MaxDepth = 5;

        internal static Transform Find(Transform root, string nodeName)
        {
            if (root == null || string.IsNullOrEmpty(nodeName))
                return null;

            return FindRecursive(root, nodeName, 0);
        }

        private static Transform FindRecursive(Transform node, string nodeName, int depth)
        {
            if (node == null || depth > MaxDepth)
                return null;

            if (string.Equals(node.name, nodeName, System.StringComparison.Ordinal))
                return node;

            for (int i = 0; i < node.childCount; i++)
            {
                Transform found = FindRecursive(node.GetChild(i), nodeName, depth + 1);
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}
