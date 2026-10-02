using System;
using System.Collections.Generic;
using XFramework.XLog;

namespace XFramework.XDiagnostics
{
    /// <summary>
    /// 诊断页签注册表：谁想出现在诊断视图里，就往这里登记。
    /// <para><b>显式登记，不反射发现</b>（与 <c>Bootstrap.Register</c> 同一取向）：零反射、顺序可控、
    /// 可测试，且一眼能看出到底有哪些页签。框架自己的模块页签与第三方页签走的是同一条路径。</para>
    /// <para><b>本类型没有接口</b>——本仓「有接口」的分界线是「是否存在可替换的后端」，注册表没有后端，
    /// 照 <c>MessageManager</c> / <c>PoolManager</c> 那类纯静态服务。</para>
    /// <para><b>线程契约</b>：全部成员只在主线程调用。页签采集读的都是主线程状态，注册面若宣称线程安全
    /// 也无济于事，还要额外处理「注册与采集并发」的一致性，代价不成比例。</para>
    /// <para><b>为什么没有自动初始化挂钩</b>：注册表不订阅任何 Unity 事件、不做会话复位、不注入
    /// PlayerLoop，静态字段随类型首次使用而初始化即可。这不是遗漏，是设计——它也因此不需要进
    /// <c>AutoInitTests</c> 的族清单。</para>
    /// <para><b>生命周期与域重载</b>：注册表是静态的，随程序集一起重建。</para>
    /// <para>· 域重载<b>开</b>：每次进播放都会重载，登记与注册会照常重跑。</para>
    /// <para>· 域重载<b>关</b>：上一会话登记过的页签<b>会跨播放会话存活</b>——<c>[RuntimeInitializeOnLoadMethod]</c>
    /// 每次进播放都会重跑（重复登记被幂等吸收），而 <c>[InitializeOnLoadMethod]</c> 不会（它注册的那批
    /// 本来就该长期在）。因此运行时注册的页签请在自己的退出路径里 <see cref="Unregister"/>。</para>
    /// <para>· <b>刻意不做</b>「进播放时清空注册表」：那会把编辑器侧注册的框架页签一并抹掉。</para>
    /// <para><b>测试</b>：登记表是静态的，fixture 必须在 SetUp/TearDown 里调 <see cref="Clear"/> 复位
    /// （PlayMode 下所有用例共享一个 player 实例）。</para>
    /// </summary>
    public static class DiagnosticsManager
    {
        #region Private Fields

        /// <summary>按展示顺序（Order 升序、同值按注册先后）保存的页签。</summary>
        private static readonly List<IDiagnosticPanel> _panels = new List<IDiagnosticPanel>();

        #endregion

        #region Public Properties

        /// <summary>当前已登记的页签数。</summary>
        public static int PanelCount => _panels.Count;

        #endregion

        #region Registration

        /// <summary>
        /// 登记一个页签。
        /// <para><b>同一实例重复登记</b>：静默忽略（记一条 Warning）——关闭域重载时两条注册路径都会重跑，
        /// 幂等是硬要求。</para>
        /// <para><b>标题重复（不同实例）</b>：两个都保留，记一条 Warning。标题由各实现自己决定，
        /// 框架不替调用方裁定谁该赢。</para>
        /// <para>插入位置按 <see cref="IDiagnosticPanel.Order"/> 升序、同值排在既有同值之后（稳定），
        /// 维护发生在登记时，读取（<see cref="CopyPanels"/>）只做拷贝。</para>
        /// </summary>
        /// <param name="panel">要登记的页签。</param>
        /// <exception cref="ArgumentNullException"><paramref name="panel"/> 为 null。</exception>
        /// <exception cref="ArgumentException"><paramref name="panel"/> 的 <see cref="IDiagnosticPanel.Title"/>
        /// 为 null 或空白（没有标题就无法渲染页签）。</exception>
        public static void Register(IDiagnosticPanel panel)
        {
            if (panel == null)
                throw new ArgumentNullException(nameof(panel));

            string title = panel.Title;
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("诊断页签必须有非空白的 Title。", nameof(panel));

            for (int i = 0; i < _panels.Count; i++)
            {
                if (ReferenceEquals(_panels[i], panel))
                {
                    LogManager.Warning(LogCategories.XFramework,
                        "诊断页签 '{0}' 的同一实例已登记，忽略重复登记。", title);
                    return;
                }
            }

            for (int i = 0; i < _panels.Count; i++)
            {
                if (string.Equals(_panels[i].Title, title, StringComparison.Ordinal))
                {
                    LogManager.Warning(LogCategories.XFramework,
                        "诊断页签标题 '{0}' 已被 {1} 占用；两个页签都会保留。",
                        title, _panels[i].GetType().Name);
                    break;
                }
            }

            int index = _panels.Count;
            for (int i = 0; i < _panels.Count; i++)
            {
                if (_panels[i].Order > panel.Order)
                {
                    index = i;
                    break;
                }
            }

            _panels.Insert(index, panel);
        }

        /// <summary>
        /// 注销一个页签。按实例比较（不是按类型或标题）。
        /// </summary>
        /// <param name="panel">要注销的页签。</param>
        /// <returns>找到并移除返回 true；本来就没登记返回 false（不抛）。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="panel"/> 为 null。</exception>
        public static bool Unregister(IDiagnosticPanel panel)
        {
            if (panel == null)
                throw new ArgumentNullException(nameof(panel));

            for (int i = 0; i < _panels.Count; i++)
            {
                if (ReferenceEquals(_panels[i], panel))
                {
                    _panels.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        /// <summary>该实例是否已登记（按实例比较）。</summary>
        /// <param name="panel">要查询的页签。</param>
        /// <returns>已登记返回 true；<paramref name="panel"/> 为 null 返回 false。</returns>
        public static bool IsRegistered(IDiagnosticPanel panel)
        {
            if (panel == null)
                return false;

            for (int i = 0; i < _panels.Count; i++)
            {
                if (ReferenceEquals(_panels[i], panel))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 取全部页签的展示序快照：先清空 <paramref name="buffer"/> 再写入，返回页签数。
        /// <para>照 <c>MessageManager.CopyTypeStats</c> / <c>LockManager.CopyLockedTypes</c> 的
        /// 「零分配主入口」体例——渲染方持有一个 List 反复复用。</para>
        /// </summary>
        /// <param name="buffer">接收结果的缓冲区；会被先清空。</param>
        /// <returns>写入的页签数。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="buffer"/> 为 null。</exception>
        public static int CopyPanels(List<IDiagnosticPanel> buffer)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            buffer.Clear();
            for (int i = 0; i < _panels.Count; i++)
                buffer.Add(_panels[i]);

            return buffer.Count;
        }

        /// <summary>
        /// 清空全部登记（含框架页签）。<b>主要给测试复位与宿主彻底重启用</b>，
        /// 常规代码不应调用——它会把别处登记的页签一并抹掉。
        /// </summary>
        public static void Clear()
        {
            _panels.Clear();
        }

        #endregion
    }
}
