using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XBootstrap;
using XFramework.XLog;
using XFramework.XPipeline;

namespace XFramework.XLocalization
{

    /// <summary>
    /// <see cref="LocalizationManager"/> 的引导阶段。Phase = <see cref="BootstrapPhases.Localization"/>（90），晚于框架内置相位。
    /// <para><b>不在 <see cref="Bootstrap.RegisterDefaults"/> 的默认组合内</b>——它需要语言数据，
    /// 由使用方构造并显式登记：</para>
    /// <code>
    /// Bootstrap.Register(new LocalizationBootstrapStage("en", myLanguageTable));
    /// </code>
    /// <para>与框架内置的另外三个阶段一样，本类型是 <c>public</c> 的：使用方需要能构造、替换、
    /// 或自行组合这些阶段，而不是只能接受框架预设的那一套。</para>
    /// </summary>
    public sealed class LocalizationBootstrapStage : IBootstrapStage
    {
        #region Private Fields

        private readonly string _defaultLanguage;
        private readonly Dictionary<string, string> _initData;
        private readonly TextAsset _table;

        /// <summary>
        /// 本阶段是否**确实**完成了初始化。<see cref="Shutdown"/> 据此决定要不要销毁——
        /// 「本阶段初始化的服务才由本阶段清理」，空转阶段不该把使用方自己
        /// <c>Initialize</c> / <c>SetInstance</c> 的管理器一起销毁掉。
        /// </summary>
        private bool _initializedByThisStage;

        #endregion

        #region Construction

        /// <summary>构造引导阶段。</summary>
        /// <param name="defaultLanguage">默认语言标识，如 <c>"zh_Hans"</c>、<c>"en"</c>。</param>
        /// <param name="data">键值对语言数据。<c>null</c> 时执行期打警告并跳过初始化（不失败、不阻塞启动）。</param>
        public LocalizationBootstrapStage(string defaultLanguage = "zh_Hans", Dictionary<string, string> data = null)
        {
            _defaultLanguage = defaultLanguage;
            _initData = data;
        }

        /// <summary>
        /// 构造引导阶段，语言表用场景/Inspector 里直接引用的 <see cref="TextAsset"/>（扁平 <c>{"key":"value"}</c> JSON，
        /// 与资源地址加载的语言表同一种格式）。
        /// <para><b>解析发生在 <see cref="ExecuteAsync"/> 里</b>而不是构造期：格式错误要沿引导管线的
        /// 标准失败路径报出来（带阶段名与描述），而不是在场景唤醒时抛。</para>
        /// <para><paramref name="table"/> 为 <c>null</c> 与 <c>data == null</c> 同一条静默降级路径
        /// （本地化是可选模块，缺数据不该让整个启动失败）。</para>
        /// </summary>
        /// <param name="defaultLanguage">默认语言标识，如 <c>"zh_Hans"</c>、<c>"en"</c>。</param>
        /// <param name="table">扁平 JSON 语言表；<c>null</c> 时执行期静默跳过初始化。</param>
        public LocalizationBootstrapStage(string defaultLanguage, TextAsset table)
            : this(defaultLanguage, data: null)
        {
            _table = table;
        }

        #endregion

        #region IBootstrapStage

        /// <summary>Phase = <see cref="BootstrapPhases.Localization"/>（90）。与 <see cref="BootstrapPhases.UserStart"/> 同值：业务自定义阶段从该区间起（依赖本地化的阶段请取更大值）。</summary>
        public int Phase => BootstrapPhases.Localization;

        /// <inheritdoc/>
        public string Name => GetType().Name;

        /// <inheritdoc/>
        public float Weight => 1f;

        /// <summary>
        /// 初始化本地化数据。
        /// <para>未提供数据时<b>静默降级为「已完成」</b>——本地化是可选模块，
        /// 缺数据不该让整个启动失败。同步瞬时阶段，忽略 <c>cancellationToken</c>。</para>
        /// </summary>
        public UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
        {
            context.SetDescription("Initializing localization...");

            // 两条数据来源：字典（直接注入）优先，否则解析 TextAsset。解析失败会抛——
            // 那是格式错误，走引导流程的标准失败路径（异常原样上抛，由 StageExecution 置 Failed）
            var data = _initData ?? (_table != null ? LanguageAssetLoader.ParseJson(_table.text) : null);

            if (data == null)
            {
                // 文案保持原样：走到这里必然 _initData 与 _table 都是 null，原句仍然属实，且既有用例正则钉着它
                LogManager.Warning(LogCategories.LocalizationBootstrapStage,
                    "ExecuteAsync called but _initData is null. Skipping initialization.");
                context.SetProgress(1f);
                context.SetState(PipelineStageState.Completed);
                return UniTask.CompletedTask;
            }

            // 先看有没有已经被初始化（使用方手动 Initialize / SetInstance，或上一次运行留下的实例）——
            // 那种情况下 Initialize 会告警并忽略，本阶段就不算「初始化过」，Shutdown 时也不该去销毁它
            var alreadyInitialized = LocalizationManager.IsInitialized;
            LocalizationManager.Initialize(_defaultLanguage, data);
            _initializedByThisStage = !alreadyInitialized;

            context.SetProgress(1f);
            context.SetState(PipelineStageState.Completed);
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 反向清理。<b>只销毁本阶段自己初始化的那份</b>——无数据跳过路径（见
        /// <see cref="ExecuteAsync"/>）什么都没建，照旧实现会在这里把使用方手动
        /// <c>Initialize</c> / <c>SetInstance</c> 的管理器一并销毁。
        /// <para>幂等：重复调用是空操作（<see cref="IBootstrapStage"/> 的要求）。</para>
        /// </summary>
        public void Shutdown()
        {
            if (!_initializedByThisStage)
                return;

            _initializedByThisStage = false;
            LocalizationManager.Destroy();
        }

        #endregion
    }
}
