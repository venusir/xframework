using System.Collections.Generic;
using Cysharp.Threading.Tasks;

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
        public static void PrintTree(this IParentNode root)
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
        static void PrintNode(IParentNode parent, int depth)
        {
            string indent = new string(' ', depth * 2);

            for (int i = 0; i < parent.ChildCount; i++)
            {
                var child = parent[i];
                UnityEngine.Debug.Log($"{indent}|- {child.GetType().Name} (Depth: {depth})");

                if (child is IParentNode childParent)
                {
                    PrintNode(childParent, depth + 1);
                }
            }
        }

        /// <summary>
        /// 从指定根节点开始，递归查找所有实现了 <see cref="ILoadableProvider"/> 的节点并注册到 <see cref="ILoadCoordinator"/>。
        /// <para>注册后，<see cref="ILoadCoordinator.LoadAsync"/> 时会统一装载这些 provider 提供的加载任务。</para>
        /// </summary>
        /// <param name="root">搜索的起始节点。</param>
        /// <param name="coordinator">加载协调器实例。</param>
        public static void MountLoadables(this IParentNode root, ILoadCoordinator coordinator)
        {
            if (root == null || coordinator == null)
                return;

            for (int i = 0; i < root.ChildCount; i++)
            {
                var child = root[i];

                if (child is ILoadableProvider loadableProvider)
                {
                    coordinator.AddProvider(loadableProvider);
                }

                if (child is IParentNode childParent)
                {
                    MountLoadables(childParent, coordinator);
                }
            }
        }

        /// <summary>
        /// 启动节点树。依次执行装载、加载、启动、回收四个阶段：
        /// <para>1. 装载：扫描节点树，收集 <see cref="ILoadableProvider"/> 的加载任务。</para>
        /// <para>2. 加载：等待所有加载任务完成。</para>
        /// <para>3. 启动：递归启动所有节点的 <see cref="BaseNode.OnStart"/>。</para>
        /// <para>4. 回收：销毁加载器，清理资源。</para>
        /// </summary>
        /// <param name="root">节点树的根节点。</param>
        public static async UniTask StartupAsync(this IParentNode root)
        {
            if (root == null)
                return;

            ILoadCoordinator loader = new LoadCoordinator();
            root.MountLoadables(loader);
            await loader.LoadAsync();
            if (root is BaseNode baseNode)
                baseNode.Start();
            loader.Destroy();
        }
    }
}
