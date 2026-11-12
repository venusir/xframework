using System;
using Cysharp.Threading.Tasks;
using XFramework.XReactive;

namespace XFramework.Samples.Integration
{
    /// <summary>一局玩法的结果。</summary>
    public readonly struct IntegrationResult
    {
        /// <summary>模式 id。</summary>
        public readonly string ModeId;

        /// <summary>本局得分。</summary>
        public readonly int Score;

        /// <summary>是否自然结束（时间到 / 回合走完）；手动停止为 false。</summary>
        public readonly bool Completed;

        /// <summary>构造结果。</summary>
        public IntegrationResult(string modeId, int score, bool completed)
        {
            ModeId = modeId;
            Score = score;
            Completed = completed;
        }
    }

    /// <summary>
    /// 玩法会话接口：两种模式共用。<b>这是示例自己的抽象</b>，不是框架的一部分——
    /// 框架不规定游戏逻辑怎么组织，这里只是让两个极简玩法能共用一套面板。
    /// </summary>
    public interface IIntegrationSession : IDisposable
    {
        /// <summary>模式 id。</summary>
        string ModeId { get; }

        /// <summary>当前得分。</summary>
        IReactiveProperty<int> Score { get; }

        /// <summary>剩余时间（秒）。</summary>
        IReactiveProperty<float> TimeLeft { get; }

        /// <summary>本局自然结束时触发（时间到 / 回合走完）。</summary>
        event Action<IntegrationResult> Finished;

        /// <summary>开始一局。</summary>
        UniTask StartAsync(GameBoardView board, System.Threading.CancellationToken cancellationToken = default);

        /// <summary>暂停 / 恢复（示例里只影响会话自身的计时与输入）。</summary>
        void SetPaused(bool paused);

        /// <summary>面板把棋盘点击转给会话。</summary>
        void OnTargetClicked(int index);

        /// <summary>提前结束并返回结果；自然结束后调用返回同一结果。</summary>
        UniTask<IntegrationResult> StopAsync(System.Threading.CancellationToken cancellationToken = default);
    }
}
