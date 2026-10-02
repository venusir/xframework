using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XLog;

namespace XFramework.XBootstrap
{

    /// <summary>
    /// 可选的启动器：把 <see cref="Bootstrap"/> 接到 Unity 的生命周期上。
    /// <para>挂到场景里即可——<see cref="Awake"/> 经 <see cref="ConfigureStages"/> 登记引导阶段，
    /// <c>Start</c> 跑启动管线，<see cref="OnDestroy"/> 反向清理。</para>
    /// <para><b>它是可选件，不是框架的必需入口。</b>不用它的话，在自己的启动流程里调
    /// <see cref="Bootstrap.RunAsync"/> 就行——每帧派发由 <c>UpdateManager</c> 注入的 PlayerLoop 完成，
    /// 与本组件无关，场景里没有它也照常运转。</para>
    /// <para><b>要带配置启动就继承并覆写 <see cref="ConfigureStages"/></b>（示例见模块 README）。
    /// 但注意：配置里若是<em>服务实例</em>（<c>IAssetRemoteServices</c> / <c>ICryptoProvider</c> 那类），
    /// 或必须在<b>任何场景加载之前</b>生效（<c>LogOptions</c>、Asset 的远端地址），语义上属于
    /// 「框架自身的配置」而不是「场景的配置」——那种情况请自建启动流程，不要勉强套进本组件。</para>
    /// </summary>
    public class GameLauncher : MonoBehaviour
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
        /// 登记启动阶段。默认实现就是 <see cref="Bootstrap.RegisterDefaults"/>。
        /// <para><b>覆写即可带配置启动</b>：调 <c>base.ConfigureStages()</c> 之后用
        /// <see cref="Bootstrap.Unregister{T}()"/> + <see cref="Bootstrap.Register"/> 替换内置阶段；
        /// 不调 <c>base</c> 则完全不登记默认组合，登记什么自己说了算。</para>
        /// <para>时机：<see cref="Awake"/> 内、<c>Start</c> 跑管线之前。登记先后不影响执行顺序
        /// （执行序由相位决定），但整张表必须在 <see cref="Bootstrap.RunAsync"/> 之前定稿。</para>
        /// </summary>
        protected virtual void ConfigureStages()
        {
            Bootstrap.RegisterDefaults();
        }

        #endregion
    }
}
