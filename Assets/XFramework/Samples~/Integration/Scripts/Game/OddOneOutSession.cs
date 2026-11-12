using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XAudio;
using XFramework.XConfig;
using XFramework.XPool;
using XFramework.XReactive;
using XFramework.XTimer;
using XFramework.XUpdate;

namespace XFramework.Samples.Integration
{
    /// <summary>
    /// OddOneOut（找异色）：每回合展示一组色块，点中异色进入下一回合；超时或回合走完结束。
    /// <para>用到的模块：Update（每帧扣时间，<see cref="IUpdateable"/>）、Pool（<c>ListPool&lt;int&gt;</c> 复用候选列表）、
    /// Config（回合数 / 选项数）、Reactive（得分 / 剩余时间）、Audio。</para>
    /// </summary>
    public sealed class OddOneOutSession : IIntegrationSession, IUpdateable
    {
        #region Constants

        /// <summary>模式 id（与配置表主键一致）。</summary>
        public const string ModeIdValue = "odd";

        private const float WrongPenaltySeconds = 5f;

        #endregion

        #region Public API

        /// <inheritdoc/>
        public string ModeId => ModeIdValue;

        /// <inheritdoc/>
        public IReactiveProperty<int> Score => _score;

        /// <inheritdoc/>
        public IReactiveProperty<float> TimeLeft => _timeLeft;

        /// <inheritdoc/>
        public event Action<IntegrationResult> Finished;

        #endregion

        #region Private Fields

        private readonly ReactiveProperty<int> _score = new ReactiveProperty<int>(0);
        private readonly ReactiveProperty<float> _timeLeft = new ReactiveProperty<float>(0f);

        private GameBoardView _board;
        private GameModeRow _mode;
        private int _optionCount;
        private int _rounds;
        private int _roundIndex;
        private int _oddIndex;
        private float _roundDuration;
        private float _roundTime;
        private bool _finished;
        private bool _paused;
        private IntegrationResult _result;

        #endregion

        #region Start / Stop

        /// <inheritdoc/>
        public UniTask StartAsync(GameBoardView board, System.Threading.CancellationToken cancellationToken = default)
        {
            _board = board ?? throw new ArgumentNullException(nameof(board));
            _mode = ConfigManager.Get<GameModeRow, string>(ModeIdValue);

            _rounds = Mathf.Max(1, _mode.Rounds);
            _optionCount = Mathf.Clamp(_mode.TargetCount, 2, _board.TargetCount);
            _roundDuration = Mathf.Max(3f, _mode.Duration > 0f ? _mode.Duration : 15f);
            _roundIndex = 0;
            _finished = false;
            _paused = false;
            _score.Value = 0;

            UpdateManager.Register(this, 0, UpdateTier.Tier0);
            StartRound();
            return UniTask.CompletedTask;
        }

        /// <inheritdoc/>
        public void SetPaused(bool paused) => _paused = paused;

        /// <inheritdoc/>
        public UniTask<IntegrationResult> StopAsync(System.Threading.CancellationToken cancellationToken = default)
        {
            Finish(completed: false);
            return UniTask.FromResult(_result);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            UpdateManager.Unregister(this);
            _score.Dispose();
            _timeLeft.Dispose();
        }

        #endregion

        #region IUpdateable

        /// <inheritdoc/>
        public void OnEnable()
        {
        }

        /// <inheritdoc/>
        public void OnDisable()
        {
        }

        /// <inheritdoc/>
        public UpdateTier OnUpdate(float deltaTime, float time)
        {
            if (_finished || _paused)
                return UpdateTier.Tier0;

            _roundTime = Mathf.Max(0f, _roundTime - deltaTime);
            _timeLeft.Value = _roundTime;

            if (_roundTime <= 0f)
                Finish(completed: true);

            return UpdateTier.Tier0;
        }

        #endregion

        #region Game Loop

        /// <inheritdoc/>
        public void OnTargetClicked(int index)
        {
            if (_finished || _paused || _board == null)
                return;

            if (index == _oddIndex)
            {
                _score.Value++;
                PlaySound(IntegrationAddresses.Correct);

                if (XFramework.XCamera.CameraManager.IsInitialized)
                    XFramework.XCamera.CameraManager.Shake(0.12f);

                _roundIndex++;

                if (_roundIndex >= _rounds)
                    Finish(completed: true);
                else
                    StartRound();
            }
            else
            {
                PlaySound(IntegrationAddresses.Wrong);
                _roundTime = Mathf.Max(0f, _roundTime - WrongPenaltySeconds);
                _timeLeft.Value = _roundTime;
            }
        }

        private void StartRound()
        {
            // ListPool 复用候选索引列表：本示例只在回合开始时取一次、用完立刻归还。
            var candidates = ListPool<int>.Get();
            try
            {
                for (int i = 0; i < _optionCount; i++)
                    candidates.Add(i);

                // 洗牌，取第一个作为异色位置。
                for (int i = candidates.Count - 1; i > 0; i--)
                {
                    int j = UnityEngine.Random.Range(0, i + 1);
                    (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
                }

                _oddIndex = candidates[0];
            }
            finally
            {
                ListPool<int>.Return(candidates);
            }

            var baseColor = Color.HSVToRGB(UnityEngine.Random.value, 0.65f, 0.85f);
            var oddColor = Color.HSVToRGB(UnityEngine.Random.value, 0.85f, 0.95f);

            for (int i = 0; i < _board.TargetCount; i++)
            {
                bool visible = i < _optionCount;
                _board.SetTargetVisible(i, visible);
                if (!visible)
                    continue;

                _board.SetTargetColor(i, i == _oddIndex ? oddColor : baseColor);
                _board.SetTargetLabel(i, string.Empty);
            }

            _roundTime = _roundDuration;
            _timeLeft.Value = _roundTime;
        }

        private void Finish(bool completed)
        {
            if (_finished)
                return;

            _finished = true;
            _board?.HideAll();
            UpdateManager.Unregister(this);

            _result = new IntegrationResult(ModeIdValue, _score.Value, completed);
            PlaySound(IntegrationAddresses.Correct);
            Finished?.Invoke(_result);
        }

        private static void PlaySound(string address)
        {
            if (AudioManager.IsInitialized)
                AudioManager.Play(address, new AudioPlayOptions(AudioChannels.SE));
        }

        #endregion
    }
}
