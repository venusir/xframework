using System.Collections.Generic;

namespace XFramework
{
    /// <summary>
    /// 节点系统工具类。提供节点树的诊断、遍历等通用辅助方法。
    /// </summary>
    public static class NodeUtility
    {
        /// <summary>
        /// 打印当前节点下的整个子树结构到控制台，便于调试。
        /// <para>逐行输出，避免大树的字符串拼接开销。</para>
        /// </summary>
        /// <param name="root">要打印的子树的根节点。</param>
        public static void PrintTree(this ParentNode root)
        {
            if (root == null)
            {
                UnityEngine.Debug.LogWarning("NodeUtility.PrintTree: root is null.");
                return;
            }

            UnityEngine.Debug.Log("=== Node Tree ===");
            PrintNode(root, 0);
            UnityEngine.Debug.Log("=================");
        }

        /// <summary>
        /// 递归打印节点及其子节点。
        /// </summary>
        static void PrintNode(ParentNode parent, int depth)
        {
            string indent = new string(' ', depth * 2);

            for (int i = 0; i < parent.ChildCount; i++)
            {
                var child = parent[i];
                UnityEngine.Debug.Log($"{indent}|- {child.GetType().Name} (Depth: {depth})");

                if (child is ParentNode childParent)
                {
                    PrintNode(childParent, depth + 1);
                }
            }
        }

        /// <summary>
        /// 从指定根节点开始，递归收集所有实现了 <see cref="ILoadable"/> 接口的节点。
        /// </summary>
        /// <param name="root">搜索的起始节点。</param>
        /// <param name="results">用于存储结果的列表。不能为 null。</param>
        public static void CollectLoadables(this ParentNode root, List<ILoadable> results)
        {
            if (root == null || results == null)
                return;

            for (int i = 0; i < root.ChildCount; i++)
            {
                var child = root[i];

                if (child is ILoadable loadable)
                {
                    results.Add(loadable);
                }

                if (child is ParentNode childParent)
                {
                    CollectLoadables(childParent, results);
                }
            }
        }
    }
}
