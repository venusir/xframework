using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using XFramework.XConfig;
using XFramework.XLocalization;
using XFramework.XUI;
using XFramework.XUI.View;

namespace XFramework.Samples.Integration
{
    /// <summary>
    /// 玩法面板：把会话的得分/剩余时间绑到 UI，并把棋盘点击转给会话。
    /// <para>它只认识 <see cref="IIntegrationSession"/>；具体是哪种玩法由 <see cref="GameSessionFactory"/> 决定。</para>
    /// </summary>
    public sealed class GamePanel : UIPanelBase
    {
        #region Constants

        private const int TargetCount = 6;

        #endregion

        #region Fields

        private Text _mode;
        private Text _score;
        private Text _time;
        private Image _timeFill;
        private Button[] _targets;
        private Text[] _labels;
        private GameBoardView _board;
        private IIntegrationSession _session;
        private string _modeId;
        private float _duration = 1f;

        #endregion

        #region Lifecycle

        /// <inheritdoc/>
        protected override async UniTask OnOpen(object userData)
        {
            _modeId = userData as string ?? TapRushSession.ModeIdValue;

            _mode = Find<Text>("txt_mode");
            _score = Find<Text>("txt_score");
            _time = Find<Text>("txt_time");
            _timeFill = Find<Image>("img_time_fill");

            _targets = new Button[TargetCount];
            _labels = new Text[TargetCount];
            for (int i = 0; i < TargetCount; i++)
            {
                _targets[i] = Find<Button>("btn_target_" + i);
                _labels[i] = _targets[i] != null
                    ? _targets[i].GetComponentInChildren<Text>(true)
                    : null;
            }

            _session = GameSessionFactory.Create(_modeId);
            if (_session == null)
            {
                Debug.LogError($"[Integration] 未注册的模式 '{_modeId}'。");
                await UIManager.CloseAsync(this);
                return;
            }

            var row = ConfigManager.Get<GameModeRow, string>(_modeId);
            _duration = row.Duration > 0f
                ? row.Duration
                : ConfigManager.GetGlobal<IntegrationRules>().DefaultDuration;
            _mode.text = LocalizationManager.Get(row.DisplayKey);
            _score.text = "0";
            _timeFill.fillAmount = 1f;

            _board = new GameBoardView(_targets, _labels, _session.OnTargetClicked);

            Track(_session.Score.Subscribe(v => _score.text = v.ToString()));
            Track(_session.TimeLeft.Subscribe(OnTimeChanged));
            _session.Finished += OnSessionFinished;

            await _session.StartAsync(_board, destroyCancellationToken);
        }

        /// <inheritdoc/>
        protected override UniTask OnClose()
        {
            if (_session != null)
            {
                _session.Finished -= OnSessionFinished;
                _session.Dispose();
                _session = null;
            }

            _board?.Dispose();
            _board = null;
            return UniTask.CompletedTask;
        }

        /// <inheritdoc/>
        protected override void OnPause()
        {
            _session?.SetPaused(true);
        }

        /// <inheritdoc/>
        protected override void OnResume()
        {
            _session?.SetPaused(false);
        }

        /// <inheritdoc/>
        protected override void OnLanguageChanged(string lang)
        {
            if (_session != null && !string.IsNullOrEmpty(_modeId))
                _mode.text = LocalizationManager.Get(ConfigManager.Get<GameModeRow, string>(_modeId).DisplayKey);
        }

        #endregion

        #region Session Callbacks

        private void OnTimeChanged(float value)
        {
            _time.text = string.Format("{0:0.0}s", value);
            _timeFill.fillAmount = _duration > 0f ? Mathf.Clamp01(value / _duration) : 0f;
        }

        private void OnSessionFinished(IntegrationResult result)
        {
            AppFlowController.Current?.ShowResultAsync(result).Forget();
        }

        #endregion

        #region Helpers

        private T Find<T>(string childName) where T : Component
        {
            var child = transform.Find(childName);
            if (child == null)
            {
                Debug.LogError($"[Integration] GamePanel 缺少子节点 '{childName}'。");
                return null;
            }

            return child.GetComponent<T>();
        }

        #endregion
    }
}
