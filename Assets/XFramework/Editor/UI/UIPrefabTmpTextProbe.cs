using System;
using UnityEngine;

namespace XFramework.Editor.UI
{
    /// <summary>
    /// TMP 文本探测。
    /// <para><b>为什么按类型全名而不是引用 <c>TMPro</c> 程序集</b>：Editor 程序集不应为一个只读校验器
    /// 新增程序集引用；而 EditMode 测试里实例化 TMP 组件可能触发 TMP Essentials 的包导入窗口。
    /// 运行时程序集已硬依赖 TMP，类型改名会先在运行时编译时暴露。</para>
    /// </summary>
    internal static class UIPrefabTmpTextProbe
    {
        #region Constants

        private const string TmpTextFullName = "TMPro.TMP_Text";

        #endregion

        #region Public

        /// <summary>
        /// 判断组件是否是 TMP 文本（含 <c>TextMeshProUGUI</c> 等派生类型）。
        /// </summary>
        /// <param name="component">待判断组件；为 null 时返回 false。</param>
        public static bool IsTmpText(Component component)
        {
            return component != null && IsTmpTextType(component.GetType());
        }

        /// <summary>
        /// 判断类型是否继承自 TMP 文本基类。
        /// </summary>
        /// <param name="type">待判断类型；为 null 时返回 false。</param>
        public static bool IsTmpTextType(Type type)
        {
            while (type != null)
            {
                if (string.Equals(type.FullName, TmpTextFullName, StringComparison.Ordinal))
                    return true;

                type = type.BaseType;
            }

            return false;
        }

        #endregion
    }
}
