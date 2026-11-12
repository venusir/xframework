using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XReactive;

namespace XFramework.Samples.Integration
{
    /// <summary>点星命中事件（EventStream 载荷）。</summary>
    public readonly struct TapHitEvent
    {
        /// <summary>命中的目标下标。</summary>
        public readonly int Index;

        /// <summary>命中后的总分。</summary>
        public readonly int Score;

        /// <summary>构造事件。</summary>
        public TapHitEvent(int index, int score)
        {
            Index = index;
            Score = score;
        }
    }

    /// <summary>
    /// TapRush（点星）：随机目标亮起，点中得分；时间到结束。
    /// <para>用到的模块：Config（时长/目标数）、Timer（倒计时与刷新）、Pool（目标状态对象）、
    /// Event（命中流）、Reactive（得分/剩余时间）、Audio（点击/判定音）。</para>
    /// </summary>
    public sealed class TapRushSession : IIntegrationSession
    {
        #region Constants

        /// <summary>模式 id（与配置表主键一致）。</summary>
        public const string ModeIdValue = "tap";

        private const float TickSeconds = 0.1f;
        private const float SpawnSeconds = 0.55f;

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
        private readonly XFramework.XEvent.IEventStream<TapHitEvent> _hits =
            XFramework.XEvent.EventStream.Create<TapHitEvent>();

        private GameBoardView _board;
        private XFramework.XTimer.TimerHandle _countdown;
        private XFramework.XTimer.TimerHandle _spawn;
        private GameModeRow _mode;
        private IntegrationRules _rules;
        private System.Threading.CancellationToken _token;
        private IntegrationResult _result;
        private bool _finished;
        private bool _paused;
        private int _activeIndex = -1;
        private readonly System.Collections.Generic.List<TapTargetState> _live =
            new System.Collections.Generic.List<TapTargetState>(8);

        #endregion

        /// <summary>单个目标的池化状态。</summary>
        private sealed class TapTargetState
        {
            public int Index;
            public float SpawnedAt;

            // PoolManager.Get<T> 的 new() 约束要求公开无参构造；私有嵌套类的隐式构造不可见。
            public TapTargetState()
            {
            }
        }

        #region Start / Stop

        /// <inheritdoc/>
        public UniTask StartAsync(GameBoardView board, System.Threading.CancellationToken cancellationToken = default)
        {
            _board = board ?? throw new ArgumentNullException(nameof(board));
            _token = cancellationToken;
            _finished = false;
            _paused = false;
            _activeIndex = -1;

            _mode = XFramework.XConfig.ConfigManager.Get<GameModeRow, string>(ModeIdValue);
            _rules = XFramework.XConfig.ConfigManager.GetGlobal<IntegrationRules>();

            var duration = _mode.Duration > 0f ? _mode.Duration : _rules.DefaultDuration;
            _timeLeft.Value = duration;
            _score.Value = 0;

            XFramework.XPool.PoolManager.Configure<TapTargetState>(
                new XFramework.XPool.PoolConfig { PrewarmSize = 4, MaxSize = 16 });

            _board.HideAll();
            _board.SetTargetLabel(0, "★");

            _spawn = XFramework.XTimer.TimerManager.Every(
                SpawnSeconds, SpawnNext, XFramework.XUpdate.UpdateTimeMode.Scaled, cancellationToken);
            _countdown = XFramework.XTimer.TimerManager.Every(
                TickSeconds, Tick, XFramework.XUpdate.UpdateTimeMode.Scaled, cancellationToken);

            SpawnNext();
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
            _countdown.Dispose();
            _spawn.Dispose();
            ReleaseLiveStates();
            _hits.Dispose();
            _score.Dispose();
            _timeLeft.Dispose();
        }

        #endregion

        #region Game Loop

        private void Tick()
        {
            if (_finished || _paused)
                return;

            _timeLeft.Value = Mathf.Max(0f, _timeLeft.Value - TickSeconds);
            if (_timeLeft.Value <= 0f)
                Finish(completed: true);
        }

        private void SpawnNext()
        {
            if (_finished || _paused || _board == null)
                return;

            ReleaseLiveStates();
            _activeIndex = UnityEngine.Random.Range(0, _board.TargetCount);
            if (_activeIndex >= _board.TargetCount)
                _activeIndex = 0;

            _board.HideAll();
            _board.SetTargetVisible(_activeIndex, true);
            _board.SetTargetLabel(_activeIndex, "★");

            var state = XFramework.XPool.PoolManager.Get<TapTargetState>();
            state.Index = _activeIndex;
            state.SpawnedAt = Time.time;
            _live.Add(state);
        }

        /// <summary>面板把点击转给会话；只有当前亮起的目标得分。</summary>
        public void OnTargetClicked(int index)
        {
            if (_finished || _paused || index != _activeIndex)
                return;

            _score.Value += _rules.ScorePerHit > 0 ? _rules.ScorePerHit : 1;
            _hits.Emit(new TapHitEvent(index, _score.Value));
            PlaySound(IntegrationAddresses.Correct);
            SpawnNext();
        }

        private void Finish(bool completed)
        {
            if (_finished)
                return;

            _finished = true;
            _countdown.Dispose();
            _spawn.Dispose();
            _board?.HideAll();
            ReleaseLiveStates();

            _result = new IntegrationResult(ModeIdValue, _score.Value, completed);
            PlaySound(IntegrationAddresses.Correct);
            Finished?.Invoke(_result);
        }

        private void ReleaseLiveStates()
        {
            for (int i = 0; i < _live.Count; i++)
                XFramework.XPool.PoolManager.Return(_live[i]);

            _live.Clear();
        }

        private static void PlaySound(string address)
        {
            if (XFramework.XAudio.AudioManager.IsInitialized)
            {
                XFramework.XAudio.AudioManager.Play(address,
                    new XFramework.XAudio.AudioPlayOptions(XFramework.XAudio.AudioChannels.SE));
            }
        }

        #endregion
    }
}
