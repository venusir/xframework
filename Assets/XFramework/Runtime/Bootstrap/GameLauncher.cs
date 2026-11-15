using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XLog;

namespace XFramework.XBootstrap
{

    /// <summary>
    /// 启动器的<b>生命周期底座</b>：把 <see cref="Bootstrap"/> 接到 Unity 的生命周期上。
    /// <para>它<b>不含任何配置字段</b>——登记什么完全由子类的 <see cref="ConfigureStages"/> 决定。
    /// 框架不提供开箱启动器：字段面（Profile 资产、UI 根）与装配代码都由使用方写在子类里，
    /// 模板见本模块 README 的「典型场景」与 Integration 示例的 <c>IntegrationLauncher</c>。</para>
    /// <para><b>它仍是可选件，不是框架的必需入口。</b>不用它的话，在自己的启动流程里调
    /// <see cref="Bootstrap.RunAsync"/> 就行——每帧派发由 <c>UpdateManager</c> 注入的 PlayerLoop 完成，
    /// 与本组件无关，场景里没有它也照常运转。</para>
    /// <para><b>必须挂在根 GameObject 上</b>：<c>DontDestroyOnLoad</c> 只对根对象生效；挂在子对象上时
    /// 切场景会随父对象一起销毁并触发 <c>Shutdown</c>，拆掉全部全局服务（R3-BST-002）。</para>
    /// <para><b>本组件覆盖不到的两样</b>：① <c>LogOptions</c>——日志配置要在<b>任何场景加载之前</b>生效
    /// （<c>SubsystemRegistration</c> 就立默认实现，正是为了不丢启动期日志），场景组件结构性地晚一步；
    /// ② 服务实例（同上，它们本就是代码对象而非数据）。</para>
    /// </summary>
    public abstract class GameLauncher : MonoBehaviour
    {
        #region Private Fields

        /// <summary>
        /// <see cref="StartupTask"/> 的兑现源：<c>Start</c> 成功/失败/取消时落位；组件在 <c>Start</c>
        /// 之前被销毁时由 <c>OnDestroy</c> 取消，避免等待者永远挂起。
        /// </summary>
        private readonly UniTaskCompletionSource _startup = new UniTaskCompletionSource();

        #endregion

        #region Public Properties

        /// <summary>
        /// 本次启动的任务：成功完成；<see cref="Bootstrap.RunAsync"/> 失败时以同一异常收场；
        /// 被取消（含组件在启动途中销毁）时以 <see cref="OperationCanceledException"/> 收场。
        /// <para><b>在 <c>Start</c> 之前访问也有效</b>——它等待的是当前/下一次运行。
        /// 与 <see cref="Bootstrap.WaitUntilReadyAsync"/> 的分工：本属性绑定「这个启动器实例」的启动，
        /// 后者是全局就绪面，不经启动器的静态入口也能用。</para>
        /// </summary>
        public UniTask StartupTask => _startup.Task;

        #endregion

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
                // 传销毁令牌：本组件的生命周期就是这次启动的生命周期——销毁时在途启动随之中止。
                // （OnDestroy 的 Shutdown 也会中止在途运行，这里是纵深防御：令牌是调用方一侧的
                // 显式声明，不依赖 Shutdown 被调到。）
                await Bootstrap.RunAsync(cancellationToken: destroyCancellationToken);
                _startup.TrySetResult();
            }
            catch (OperationCanceledException)
            {
                // 启动被取消属正常路径（例如应用退出），不当作错误
                _startup.TrySetCanceled();
            }
            catch (Exception exception)
            {
                _startup.TrySetException(exception);
                LogManager.Error(LogCategories.GameLauncher, "启动失败：{0}", exception);
            }
        }

        void OnDestroy()
        {
            // 启动尚未落位就销毁：等待者收到取消，而不是永远挂着。
            _startup.TrySetCanceled();
            Bootstrap.Shutdown();
        }

        #endregion

        #region Extension Points

        /// <summary>
        /// 登记启动阶段。<b>抽象（模板方法）——继承者必须自己决定登记什么</b>，不存在「忘了覆写所以
        /// 默认组合悄悄跑了」的中间态：
        /// <list type="bullet">
        /// <item><b>完全自控</b>：只实现本方法即可（如 <c>Bootstrap.Register(new MyServiceBootstrapStage());</c>）
        /// ——登记什么、按什么相位，全由你定。内置阶段的构造入口见 <see cref="BootstrapStages"/>。</item>
        /// <item><b>先装配数据再追加</b>：你自己的 Profile 资产 → <c>BootstrapStages.*</c> 的装配代码就写在本方法里；
        /// 示例见 Integration 示例的 <c>IntegrationLauncher</c>。</item>
        /// </list>
        /// <para>时机：<see cref="Awake"/> 内、<c>Start</c> 跑管线之前。登记先后不影响执行顺序
        /// （执行序由相位决定），但整张表必须在 <see cref="Bootstrap.RunAsync"/> 之前定稿。</para>
        /// </summary>
        protected abstract void ConfigureStages();

        #endregion
    }
}
