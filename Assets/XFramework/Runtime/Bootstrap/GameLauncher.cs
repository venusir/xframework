using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XLog;

namespace XFramework.XBootstrap
{

    /// <summary>
    /// 启动器的<b>生命周期底座</b>：把 <see cref="Bootstrap"/> 接到 Unity 的生命周期上。
    /// <para>它<b>不含任何配置字段</b>——登记什么完全由子类的 <see cref="ConfigureStages"/> 决定。
    /// 要「挂上就能在 Inspector 里配」请直接用 <see cref="DefaultGameLauncher"/>（本类的开箱默认实现）；
    /// 要完全自控（服务实例、自定义阶段、换输入后端…）就继承本类：Inspector 里不会有任何无关字段。</para>
    /// <para><b>它仍是可选件，不是框架的必需入口。</b>不用它的话，在自己的启动流程里调
    /// <see cref="Bootstrap.RunAsync"/> 就行——每帧派发由 <c>UpdateManager</c> 注入的 PlayerLoop 完成，
    /// 与本组件无关，场景里没有它也照常运转。</para>
    /// <para><b>本组件覆盖不到的两样</b>：① <c>LogOptions</c>——日志配置要在<b>任何场景加载之前</b>生效
    /// （<c>SubsystemRegistration</c> 就立默认实现，正是为了不丢启动期日志），场景组件结构性地晚一步；
    /// ② 服务实例（同上，它们本就是代码对象而非数据）。</para>
    /// </summary>
    public abstract class GameLauncher : MonoBehaviour
    {
        #region Lifecycle Methods

        void Awake()
        {
            DontDestroyOnLoad(gameObject);

            ConfigureStages();
        }

        async void Start()
        {
            // async void 是 Unity 生命周期入口（CLAUDE.md 允许的唯一场景）。
            // Bootstrap.RunAsync 在失败/取消时会抛出，而这里没有调用方可承接，
            // 因此必须自行收敛——否则会成为未处理异常。
            try
            {
                await Bootstrap.RunAsync();
            }
            catch (OperationCanceledException)
            {
                // 启动被取消属正常路径（例如应用退出），不当作错误
            }
            catch (Exception exception)
            {
                LogManager.Error(LogCategories.GameLauncher, "启动失败：{0}", exception);
            }
        }

        void OnDestroy()
        {
            Bootstrap.Shutdown();
        }

        #endregion

        #region Extension Points

        /// <summary>
        /// 登记启动阶段。<b>抽象（模板方法）——继承者必须自己决定登记什么</b>，不存在「忘了覆写所以
        /// 默认组合悄悄跑了」的中间态：
        /// <list type="bullet">
        /// <item><b>完全自控</b>：只实现本方法即可（如 <c>Bootstrap.Register(new MyServiceBootstrapStage());</c>）
        /// ——默认组合要不要、按什么相位，全由你定；要默认组合就调 <see cref="Bootstrap.RegisterDefaults"/>。</item>
        /// <item><b>在默认实现上微调</b>：继承 <see cref="DefaultGameLauncher"/> 覆写它更省事——那边已经把
        /// 「字段面 → 各模块 options」那套装配写好了。</item>
        /// </list>
        /// <para>时机：<see cref="Awake"/> 内、<c>Start</c> 跑管线之前。登记先后不影响执行顺序
        /// （执行序由相位决定），但整张表必须在 <see cref="Bootstrap.RunAsync"/> 之前定稿。</para>
        /// </summary>
        protected abstract void ConfigureStages();

        #endregion
    }
}
