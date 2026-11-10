using System;
using System.Collections.Generic;
using UnityEngine;
using XFramework.XLog;
using XFramework.XReactive;

namespace XFramework.XUI.Authoring
{
    /// <summary>
    /// 声明式 UI 的全局数据源注册表。
    /// <para>项目在启动时注册自己的 <see cref="IUIDataProvider"/>；面板 / HUD / 对话框打开时按
    /// <see cref="UIPanelDefinition.DataBindings"/> 向这里解析数据键。多个 provider 允许共存，
    /// 解析按注册顺序<b>倒序</b>进行——后注册者优先（便于测试或场景级覆盖）。</para>
    /// <para><b>线程契约</b>：只在 Unity 主线程调用。</para>
    /// <para><b>所有权</b>：注册表不 Dispose provider；注销由项目负责，顺序与注册相反。</para>
    /// </summary>
    public static class UIDataProviderRegistry
    {
        #region Private Fields

        private static readonly List<IUIDataProvider> _providers = new List<IUIDataProvider>();

        #endregion

        #region Public API

        /// <summary>
        /// 注册一个数据源；同一实例重复注册是无操作。
        /// </summary>
        /// <param name="provider">数据源，不可为 <c>null</c>。</param>
        /// <exception cref="ArgumentNullException"><paramref name="provider"/> 为 <c>null</c> 时抛出。</exception>
        public static void Register(IUIDataProvider provider)
        {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));

            if (_providers.Contains(provider))
                return;

            _providers.Add(provider);
        }

        /// <summary>
        /// 注销数据源；<paramref name="provider"/> 为 <c>null</c> 或未注册时是无操作。
        /// <para>不 Dispose provider——所有权归项目。</para>
        /// </summary>
        /// <param name="provider">要注销的数据源。</param>
        public static void Unregister(IUIDataProvider provider)
        {
            if (provider == null)
                return;

            _providers.Remove(provider);
        }

        #endregion

        #region Internal — Resolution

        /// <summary>当前注册数；供绑定层决定「无 provider 时只告警一次」。</summary>
        internal static int Count => _providers.Count;

        /// <summary>
        /// 按键解析响应式属性；后注册的 provider 优先。
        /// <para>provider 抛出的异常在这里捕获并记 Warning，随后继续尝试更早注册的 provider——
        /// 绑定链路不接受因 provider 实现缺陷而中断。</para>
        /// </summary>
        internal static bool TryGet<T>(string key, out IReactiveProperty<T> source)
        {
            source = null;
            if (string.IsNullOrEmpty(key))
                return false;

            for (int i = _providers.Count - 1; i >= 0; i--)
            {
                IUIDataProvider provider = _providers[i];
                if (provider == null)
                    continue;

                try
                {
                    if (provider.TryGet(key, out source) && source != null)
                        return true;
                }
                catch (Exception ex)
                {
                    LogManager.Warning(UIAuthoringLog.Category,
                        "Data provider threw while resolving key '{0}': {1}", key, ex.Message);
                }
            }

            source = null;
            return false;
        }

        #endregion

        #region Lifecycle

        /// <summary>
        /// 会话开始时清空注册表。
        /// <para><b>为什么挂在 <c>SubsystemRegistration</c></b>：它早于首场景 <c>Awake</c>，因而早于任何
        /// provider 注册与面板打开；关闭域重载时上一会话的静态列表会存活到这一会话，必须在这里清掉。</para>
        /// <para>只清列表、不 Dispose provider——上一会话的实例可能已随域销毁，清理归项目。</para>
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetForNewSession()
        {
            _providers.Clear();
        }

        #endregion
    }
}
