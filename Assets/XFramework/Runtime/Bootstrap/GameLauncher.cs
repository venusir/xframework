using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework.XBootstrap
{

    /// <summary>
    /// 可选的启动器：把 <see cref="Bootstrap"/> 接到 Unity 的生命周期上。
    /// <para>挂到场景里即可——<see cref="Awake"/> 登记框架内置的三个引导阶段，
    /// <c>Start</c> 跑启动管线，<see cref="OnDestroy"/> 反向清理。</para>
    /// <para><b>它是可选件，不是框架的必需入口。</b>不用它的话，在自己的启动流程里调
    /// <see cref="Bootstrap.RunAsync"/> 就行——每帧派发由 <c>UpdateManager</c> 注入的 PlayerLoop 完成，
    /// 与本组件无关，场景里没有它也照常运转。</para>
    /// </summary>
    public class GameLauncher : MonoBehaviour
    {
        #region Lifecycle Methods

        void Awake()
        {
            DontDestroyOnLoad(gameObject);

            Bootstrap.RegisterDefaults();
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
                Debug.LogError($"[GameLauncher] 启动失败：{exception}");
            }
        }

        void OnDestroy()
        {
            Bootstrap.Shutdown();
        }

        #endregion
    }
}
