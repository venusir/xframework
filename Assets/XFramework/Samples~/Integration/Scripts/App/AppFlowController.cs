using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XAudio;
using XFramework.XBootstrap;
using XFramework.XLock;
using XFramework.XLog;
using XFramework.XLocalization;
using XFramework.XMessage;
using XFramework.XPool;
using XFramework.XSave;
using XFramework.XSettings;
using XFramework.XTimer;
using XFramework.XUI;
using XFramework.XUI.View;

namespace XFramework.Samples.Integration
{
    /// <summary>
    /// 示例的业务流程控制器：等启动完成 → 打开主菜单 → 串起设置 / 语言 / 退出。
    /// <para><b>它不是框架的一部分</b>：只是示例里把各模块接起来的那层胶水。第三方可以照抄调用顺序，
    /// 也可以换成自己的状态机 / MVVM / DI——框架不规定业务流程的形状。</para>
    /// <para>本类刻意只做「薄路由」：具体面板的 UI 逻辑在各 Panel 里，玩法在会话类里。</para>
    /// </summary>
    public sealed class AppFlowController : MonoBehaviour
    {
        #region Constants

        private const int ExitLockType = 1;

        #endregion

        #region Public API

        /// <summary>当前场景里的流程控制器（示例胶水；生产项目可用自己的注入方式）。</summary>
        public static AppFlowController Current { get; private set; }

        /// <summary>由 <see cref="AppReadyBootstrapStage"/> 在全部启动阶段完成后调用。</summary>
        public void NotifyBootstrapped()
        {
            _booted.TrySetResult();
        }

        /// <summary>打开设置面板（压入显示栈）。</summary>
        public UniTask OpenSettingsAsync()
        {
            return UIManager.PushAsync<SettingsPanel>(IntegrationAddresses.Settings, UILayers.Popup);
        }

        /// <summary>打开语言面板（压入显示栈）。</summary>
        public UniTask OpenLanguageAsync()
        {
            return UIManager.PushAsync<LanguagePanel>(IntegrationAddresses.Language, UILayers.Popup);
        }

        /// <summary>切换语言：经 Localization 模块加载语言表，并写回设置。</summary>
        public async UniTask SwitchLanguageAsync(string language)
        {
            if (string.IsNullOrEmpty(language) || !LocalizationManager.HasLanguage(language))
            {
                LogManager.Warning(IntegrationLogCategories.Flow, "语言 '{0}' 未加载，切换已忽略。", language);
                return;
            }

            await LocalizationManager.SwitchLanguageAsync(language, destroyCancellationToken);
            IntegrationSettingRefs.Language.Value = language;
        }

        /// <summary>
        /// 退出流程：防重复 → 二次确认 → 关面板 → 存档 → 存档设置 → 写摘要 → 逆序清理 → 退出。
        /// <para>编辑器里 <c>Application.Quit()</c> 无效，只显示提示；不用 <c>#if UNITY_EDITOR</c>，
        /// 而是运行期判断 <see cref="Application.isEditor"/>（示例代码不依赖编辑器符号）。</para>
        /// </summary>
        public async UniTask RequestExitAsync()
        {
            if (LockManager.Global.IsLocked(ExitLockType))
                return;

            var handle = LockManager.Global.AddLock(ExitLockType, this);
            try
            {
                var confirmed = await UIManager.ShowDialogAsync<ConfirmDialog, bool>(
                    IntegrationAddresses.Confirm, "exit.confirm");
                if (!confirmed)
                    return;

                await UIManager.CloseAllAsync();

                if (SaveManager.IsInitialized)
                    await SaveManager.SaveAsync(0, destroyCancellationToken);

                if (SettingsManager.IsInitialized)
                    await SettingsManager.SaveAsync<IntegrationSettings>(destroyCancellationToken);

                await LastRunStore.WriteAsync(_lastRun, destroyCancellationToken);

                MessageManager.Clear();
                TimerManager.CancelAll();
                PoolManager.ClearAll();

                // 先释放本次退出锁，再销毁锁管理器——反过来会让 handle.Dispose() 撞上已销毁的门面。
                handle.Dispose();

                Bootstrap.Shutdown();

                if (Application.isEditor)
                    _bootScreen?.ShowExited();
                else
                    Application.Quit();
            }
            finally
            {
                // 发生异常时也要把锁还回去；重复 Dispose 是幂等的。
                handle.Dispose();
            }
        }

        #endregion

        #region Private Fields

        private readonly UniTaskCompletionSource _booted = new UniTaskCompletionSource();
        private BootScreen _bootScreen;
        private LastRunSummary _lastRun = new LastRunSummary();
        private IDisposable _exitSubscription;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            Current = this;
        }

        private async void Start()
        {
            try
            {
                // 等 AppReadyBootstrapStage 的信号；对象销毁时随令牌中止。
                await _booted.Task.AttachExternalCancellation(destroyCancellationToken);
                await OnBootstrappedAsync(destroyCancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 对象在启动途中销毁：正常路径。
            }
            catch (Exception e)
            {
                LogManager.Exception(IntegrationLogCategories.Flow, e, "业务流程启动失败");
                _bootScreen?.ShowError(e.Message);
            }
        }

        private void OnDestroy()
        {
            _exitSubscription?.Dispose();
            _exitSubscription = null;

            if (ReferenceEquals(Current, this))
                Current = null;
        }

        #endregion

        #region Boot

        private async UniTask OnBootstrappedAsync(System.Threading.CancellationToken cancellationToken)
        {
            _bootScreen = FindAnyObjectByType<BootScreen>();

            _exitSubscription = MessageManager.Subscribe<ExitRequestedMessage>(
                _ => RequestExitAsync().Forget());

            // 语言表地址模板：切换语言时框架按 {0} 拼出 Integration.Lang_en 这样的地址。
            LocalizationManager.LanguageAssetPath = IntegrationAddresses.LanguagePathFormat;

            var language = IntegrationSettingRefs.Language.Value;
            if (LocalizationManager.HasLanguage(language))
                await LocalizationManager.SwitchLanguageAsync(language, cancellationToken);

            _lastRun = LastRunStore.Read();
            _bootScreen?.MarkReady();

            await UIManager.OpenAsync<MainMenuPanel>(IntegrationAddresses.MainMenu,
                UILayers.Default, _lastRun, cancellationToken);

            LogManager.Info(IntegrationLogCategories.Flow, "启动完成：language={0}", language);
        }

        #endregion
    }
}
