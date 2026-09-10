using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XMessage;

namespace XFramework.Example
{

    /// <summary>
    /// 展示 XFramework Message 模块的消息总线用法。
    /// <para>覆盖:类型化发布/订阅、带 Key 通道、订阅级过滤、缓冲重放、异步订阅与异步发布、
    /// 请求-响应、缓冲淘汰与运行统计。</para>
    /// <para>选型提示:跨模块解耦用本模块;单对象属性变化用 ReactiveProperty;类内私有回调用 C# event。
    /// 详见 Runtime/Message/README.md「适用场景与选型」。</para>
    /// </summary>
    public class MessageSample : MonoBehaviour
    {
        #region Message Types

        /// <summary>金币变化。消息用 readonly struct 定义以避免 GC 分配。</summary>
        private readonly struct CoinChangedMessage
        {
            public readonly int NewAmount;

            public CoinChangedMessage(int newAmount) => NewAmount = newAmount;
        }

        /// <summary>游戏状态变化。</summary>
        private readonly struct GameStateChangedMessage
        {
            public readonly string NewState;

            public GameStateChangedMessage(string newState) => NewState = newState;
        }

        /// <summary>查询玩家分数的请求。</summary>
        private sealed class GetScoreRequest
        {
            public string PlayerId;
        }

        /// <summary>查询玩家分数的响应。</summary>
        private sealed class GetScoreResponse
        {
            public int Score;
        }

        #endregion

        #region Private Fields

        /// <summary>本组件创建的全部订阅句柄，销毁时统一释放。</summary>
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>(8);

        #endregion

        #region Unity Lifecycle

        private void Start()
        {
            SubscribeHandlers();
            RegisterRequestHandler();

            RunSampleAsync().Forget();
        }

        private void OnDestroy()
        {
            foreach (var subscription in _subscriptions)
                subscription?.Dispose();
            _subscriptions.Clear();

            MessageManager.Unregister<GetScoreRequest, GetScoreResponse>();
        }

        #endregion

        #region Subscription

        private void SubscribeHandlers()
        {
            // 1. 类型化订阅:发布方与订阅方互不知晓,适合跨模块解耦
            _subscriptions.Add(MessageManager.Subscribe<CoinChangedMessage>(msg =>
                Debug.Log($"[Coin] 金币变为 {msg.NewAmount}")));

            // 2. 订阅级过滤:只关心大额变化,不过滤则回调内自己判断
            _subscriptions.Add(MessageManager.Subscribe<CoinChangedMessage>(
                filter: msg => msg.NewAmount > 100,
                handler: msg => Debug.Log($"[Coin] 大额变化: {msg.NewAmount}")));

            // 3. 带 Key 的通道:同一消息类型按 Key 分流
            _subscriptions.Add(MessageManager.Subscribe<string, int>("Score",
                score => Debug.Log($"[Keyed] 分数变为 {score}")));

            // 4. 缓冲订阅:订阅时立即收到最近一次发布的值(若发布过),适合「状态」类消息
            _subscriptions.Add(MessageManager.SubscribeBuffered<GameStateChangedMessage>(
                msg => Debug.Log($"[Buffered] 当前状态: {msg.NewState}")));

            // 5. 异步订阅:ct 即订阅自身的令牌,退订会取消在途 await
            _subscriptions.Add(MessageManager.SubscribeAsync<GameStateChangedMessage>(async (msg, ct) =>
            {
                await UniTask.Delay(TimeSpan.FromMilliseconds(200), cancellationToken: ct);
                Debug.Log($"[Async] 状态切换完成: {msg.NewState}");
            }));
        }

        private void RegisterRequestHandler()
        {
            // 请求处理器全局唯一;令牌由 RequestAsync 原样转发,便于透传给下游
            MessageManager.Register<GetScoreRequest, GetScoreResponse>(async (request, ct) =>
            {
                await UniTask.Delay(TimeSpan.FromMilliseconds(100), cancellationToken: ct);
                return new GetScoreResponse { Score = request.PlayerId.GetHashCode() % 1000 };
            });
        }

        #endregion

        #region Publish

        private async UniTaskVoid RunSampleAsync()
        {
            var cancellationToken = this.GetCancellationTokenOnDestroy();

            // 类型化发布:同步投递给同步订阅者,并以 fire-and-forget 触发异步订阅
            MessageManager.Publish(new CoinChangedMessage(50));
            MessageManager.Publish(new CoinChangedMessage(250));

            // 带 Key 发布:只有订阅了相同 Key 的一方会收到
            MessageManager.Publish("Score", 500);

            // 缓冲通道:先发布后订阅也能拿到最近一条
            MessageManager.Publish(new GameStateChangedMessage("Playing"));

            // 异步发布:等全部异步处理器完成(也可指定 MessagePublishStrategy.Sequential)
            await MessageManager.PublishAsync(
                new GameStateChangedMessage("Paused"), MessagePublishStrategy.Parallel, cancellationToken);

            // 请求-响应:需要返回值时使用
            var response = await MessageManager.RequestAsync<GetScoreRequest, GetScoreResponse>(
                new GetScoreRequest { PlayerId = "player_1" }, cancellationToken);
            Debug.Log($"[Request] 玩家分数: {response.Score}");

            LogStats();
        }

        #endregion

        #region Diagnostics

        private static void LogStats()
        {
            // 诊断订阅泄漏与缓冲内存驻留:发布次数、订阅数、持有重放缓存的通道数
            var stats = MessageManager.GetStats();
            Debug.Log($"[Stats] {stats}");

            // 高频 Key 场景应在实体生命周期结束时淘汰其缓冲通道,否则每个 Key 会永久持有一条消息
            var evicted = MessageManager.EvictBufferedChannel<string, int>("Score");
            Debug.Log($"[Stats] 淘汰 Score 通道: {evicted}");
        }

        #endregion
    }
}
