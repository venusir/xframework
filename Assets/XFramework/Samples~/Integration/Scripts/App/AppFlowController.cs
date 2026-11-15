using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XAsset;
using XFramework.XAudio;
using XFramework.XBootstrap;
using XFramework.XData;
using XFramework.XDiagnostics;
using XFramework.XInput;
using XFramework.XLock;
using XFramework.XLog;
using XFramework.XLocalization;
using XFramework.XMessage;
using XFramework.XPool;
using XFramework.XSave;
using XFramework.XScene;
using XFramework.XSettings;
using XFramework.XTimer;
using XFramework.XUI;
using XFramework.XUI.View;
using AssetSceneHandle = XFramework.XAsset.AssetSceneHandle;

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
        private const int EnterLockType = 2;

        private static readonly IntegrationDiagnosticPanel DiagnosticPanel = new IntegrationDiagnosticPanel();

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

        /// <summary>打开模式选择面板。</summary>
        public UniTask OpenModeSelectAsync()
        {
            return UIManager.PushAsync<ModeSelectPanel>(IntegrationAddresses.ModeSelect, UILayers.Popup);
        }

        /// <summary>
        /// 进入游戏：加载面板 + Pipeline（预载 → 场景 → 玩法面板）。
        /// <para>进入锁防止双击；游戏场景 Additive 加载——UIRoot 在 Boot 场景里，单场景加载会把它一起卸掉。</para>
        /// </summary>
        public async UniTask EnterGameAsync(string modeId)
        {
            if (LockManager.Global.IsLocked(EnterLockType))
                return;

            var handle = LockManager.Global.AddLock(EnterLockType, this);
            try
            {
                // Push 只让栈顶失焦、不隐藏；不关主菜单会让它留在游戏 UI 后面。
                await UIManager.CloseAllAsync();
                await UnloadGameSceneAsync();

                _currentMode = modeId;
                var loading = await UIManager.OpenAsync<LoadingPanel>(
                    IntegrationAddresses.Loading, UILayers.Top, null, destroyCancellationToken);

                try
                {
                    _gameScene = await GameLoadingPipeline.RunAsync(
                        modeId, loading, destroyCancellationToken);
                }
                finally
                {
                    await UIManager.CloseAsync<LoadingPanel>();
                }
            }
            finally
            {
                handle.Dispose();
            }
        }

        /// <summary>重开一局：关掉现有面板后重新进入。</summary>
        public async UniTask RestartGameAsync(string modeId)
        {
            await UIManager.CloseAllAsync();
            await EnterGameAsync(modeId);
        }

        /// <summary>返回主菜单：卸场景并重新聚焦主菜单面板。</summary>
        public async UniTask OpenMainMenuAsync()
        {
            await UnloadGameSceneAsync();

            // 用内存里的最新摘要，不要回读文件：结算刚更新过 _lastRun，文件要等退出才写。
            await UIManager.OpenAsync<MainMenuPanel>(IntegrationAddresses.MainMenu,
                UILayers.Default, _lastRun, destroyCancellationToken);
        }

        /// <summary>结算：更新数据块 → 落盘 → 卸场景 → 打开结算面板。</summary>
        public async UniTask ShowResultAsync(IntegrationResult result)
        {
            await UIManager.CloseAsync<GamePanel>();
            await UnloadGameSceneAsync();
            await AssetManager.UnloadUnusedAssetsAsync(cancellationToken: destroyCancellationToken);

            if (_data != null)
            {
                _data.Sessions++;
                _data.LastMode = result.ModeId;
                if (result.ModeId == TapRushSession.ModeIdValue)
                    _data.BestTapScore = Mathf.Max(_data.BestTapScore, result.Score);
                else
                    _data.BestOddScore = Mathf.Max(_data.BestOddScore, result.Score);

                DataManager.MarkDirty<IntegrationData>();

                // 同步内存摘要：主菜单显示与退出落盘都读它，否则永远停在上一会话。
                _lastRun.Sessions = _data.Sessions;
                _lastRun.LastMode = _data.LastMode;
                _lastRun.BestScore = Mathf.Max(_data.BestTapScore, _data.BestOddScore);
            }

            if (SaveManager.IsInitialized)
                await SaveManager.SaveAsync(0, destroyCancellationToken);

            await UIManager.PushAsync<ResultPanel>(IntegrationAddresses.Result,
                UILayers.Popup, result, destroyCancellationToken);
        }

        /// <summary>切换语言：经 Localization 模块按需加载语言表，并写回设置。</summary>
        public async UniTask SwitchLanguageAsync(string language)
        {
            if (string.IsNullOrEmpty(language))
            {
                LogManager.Warning(IntegrationLogCategories.Flow, "语言标识为空，切换已忽略。");
                return;
            }

            try
            {
                // 不预判 HasLanguage：它只看缓存，首次切到未缓存的语言会被错误拦截；
                // SwitchLanguageAsync 自己会「已缓存则同步切换，否则按地址加载」。
                await LocalizationManager.SwitchLanguageAsync(language, destroyCancellationToken);
                IntegrationSettingRefs.Language.Value = language;
            }
            catch (OperationCanceledException)
            {
                // 对象销毁或调用方取消：切换中止，不是错误。
            }
            catch (Exception e)
            {
                LogManager.Exception(IntegrationLogCategories.Flow, e, "切换语言失败");
            }
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
                await UnloadGameSceneAsync();

                if (SaveManager.IsInitialized)
                    await SaveManager.SaveAsync(0, destroyCancellationToken);

                if (SettingsManager.IsInitialized)
                    await SettingsManager.SaveAsync<IntegrationSettings>(destroyCancellationToken);

                await LastRunStore.WriteAsync(_lastRun, destroyCancellationToken);

                AudioManager.Stop(_bgm);
                AudioManager.Destroy();

                MessageManager.Clear();
                TimerManager.CancelAll();
                PoolManager.ClearAll();
                DiagnosticsManager.Unregister(DiagnosticPanel);

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
        private IDisposable _settingsSubscription;
        private IDisposable _pauseSubscription;
        private IntegrationData _data;
        private AssetSceneHandle _gameScene;
        private AudioHandle _bgm;
        private string _currentMode = TapRushSession.ModeIdValue;

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
            _settingsSubscription?.Dispose();
            _settingsSubscription = null;
            _pauseSubscription?.Dispose();
            _pauseSubscription = null;

            DiagnosticsManager.Unregister(DiagnosticPanel);

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
            _settingsSubscription = MessageManager.Subscribe<SettingsAppliedMessage>(OnSettingsApplied);

            AudioManager.Initialize(new AudioInitOptions { MaxVoices = 16 });
            ApplyAudioSettings();
            _bgm = await AudioManager.PlayAsync(IntegrationAddresses.Bgm,
                new AudioPlayOptions(AudioChannels.Bgm, loop: true), cancellationToken);

            // 有存档就先恢复数据块，再让主菜单显示“上次游玩”。
            if (SaveManager.IsInitialized)
                await SaveManager.TryLoadAsync(0, cancellationToken);

            _data = DataManager.GetOrCreateBlock<IntegrationData>();

            DiagnosticsManager.Register(DiagnosticPanel);
            if (InputManager.IsInitialized)
                _pauseSubscription = InputManager.ObservePressed("Pause", OnPausePressed);

            // 语言表地址模板：切换语言时框架按 {0} 拼出 Integration.Lang_en 这样的地址。
            LocalizationManager.LanguageAssetPath = IntegrationAddresses.LanguagePathFormat;

            // 已保存的语言可能尚未加载（首次启动只注入默认语言），交给同一个按需加载入口。
            var language = IntegrationSettingRefs.Language.Value;
            await SwitchLanguageAsync(language);

            _lastRun = LastRunStore.Read();
            _bootScreen?.MarkReady();

            await UIManager.OpenAsync<MainMenuPanel>(IntegrationAddresses.MainMenu,
                UILayers.Default, _lastRun, cancellationToken);

            LogManager.Info(IntegrationLogCategories.Flow, "启动完成：language={0}", language);
        }

        private void OnSettingsApplied(SettingsAppliedMessage message)
        {
            ApplyAudioSettings();
            AudioManager.Play(IntegrationAddresses.Click,
                new AudioPlayOptions(AudioChannels.UI));
        }

        private void OnPausePressed()
        {
            if (!UIManager.IsInitialized)
                return;

            // 只在游戏面板开着、且没有暂停面板时响应，避免重复压栈。
            if (UIManager.IsOpen<GamePanel>() && !UIManager.IsOpen<PausePanel>())
                UIManager.PushAsync<PausePanel>(IntegrationAddresses.Pause, UILayers.Popup, _currentMode).Forget();
        }

        private static void ApplyAudioSettings()
        {
            AudioManager.MasterVolume = IntegrationSettingRefs.MasterVolume.Value;
            AudioManager.SetChannelVolume(AudioChannels.Bgm, IntegrationSettingRefs.BgmVolume.Value);
            AudioManager.SetChannelVolume(AudioChannels.SE, IntegrationSettingRefs.SfxVolume.Value);
            AudioManager.SetChannelVolume(AudioChannels.UI, IntegrationSettingRefs.SfxVolume.Value);
        }

        private async UniTask UnloadGameSceneAsync()
        {
            if (!_gameScene.IsValid)
                return;

            await _gameScene.UnloadAsync(destroyCancellationToken);
            _gameScene = default;
        }

        #endregion
    }
}
