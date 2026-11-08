using UnityEngine;
using XFramework.XUI;
using XFramework.XUI.View;

namespace XFramework.Editor.UI
{
    /// <summary>
    /// 判断一个预制体是否归框架 UI 管线管理。
    /// <para>判据是「层级内任意位置有 <see cref="UIViewBase"/> 派生组件或 <see cref="UITipItem"/>」，
    /// 遍历包含未激活节点，与运行时缓存的行为一致。只有 <c>UIPanelBinding</c> 的普通预制体不算候选——
    /// 绑定组件可以被手工使用，不该被卷进框架管线的校验。</para>
    /// </summary>
    internal static class UIPrefabCandidateDetector
    {
        /// <summary>
        /// 判断预制体根节点是否属于 UI 候选。
        /// </summary>
        /// <param name="root">预制体根节点；为 null 时返回 false。</param>
        /// <returns>层级内存在 <see cref="UIViewBase"/> 派生组件或 <see cref="UITipItem"/> 时为 true。</returns>
        public static bool IsCandidate(GameObject root)
        {
            if (root == null)
                return false;

            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                var target = transforms[i];
                if (target == null)
                    continue;

                if (target.GetComponent<UIViewBase>() != null)
                    return true;

                if (target.GetComponent<UITipItem>() != null)
                    return true;
            }

            return false;
        }
    }
}
