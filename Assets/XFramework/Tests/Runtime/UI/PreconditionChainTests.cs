using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using XFramework.XUI.Controller;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// <see cref="PreconditionChain"/> 的取消语义（UI-C-007）。
    /// <para>链只在**条件之间**检查取消；条件内部是否响应取消由该条件自己的实现决定。</para>
    /// </summary>
    [TestFixture]
    public class PreconditionChainTests
    {
        private static PreconditionChain Create() =>
            new PreconditionChain(typeof(PreconditionChainTests), "ui/test", 0, null);

        [Test]
        public async Task ExecuteAsync_NoConditions_ReturnsTrue()
        {
            Assert.IsTrue(await Create().ExecuteAsync());
        }

        [Test]
        public async Task ExecuteAsync_StopsAtFirstFalse()
        {
            int secondCalls = 0;
            var chain = Create()
                .Add((_, __, ___, ____) => UniTask.FromResult(false))
                .Add((_, __, ___, ____) =>
                {
                    secondCalls++;
                    return UniTask.FromResult(true);
                });

            Assert.IsFalse(await chain.ExecuteAsync());
            Assert.AreEqual(0, secondCalls, "false 之后的条件不得执行");
        }

        [Test]
        public void ExecuteAsync_PreCancelled_ThrowsBeforeAnyCondition()
        {
            int calls = 0;
            var chain = Create().Add((_, __, ___, ____) =>
            {
                calls++;
                return UniTask.FromResult(true);
            });

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
                chain.ExecuteAsync(cts.Token).GetAwaiter().GetResult());
            Assert.AreEqual(0, calls, "预取消不得执行任何条件");
        }

        [Test]
        public void ExecuteAsync_CancelledBetweenConditions_StopsRemaining()
        {
            using var cts = new CancellationTokenSource();
            int secondCalls = 0;
            var chain = Create()
                .Add((_, __, ___, ____) =>
                {
                    cts.Cancel(); // 第一个条件内部取消：链应在下一个检查点停下
                    return UniTask.FromResult(true);
                })
                .Add((_, __, ___, ____) =>
                {
                    secondCalls++;
                    return UniTask.FromResult(true);
                });

            Assert.Throws<OperationCanceledException>(() =>
                chain.ExecuteAsync(cts.Token).GetAwaiter().GetResult());
            Assert.AreEqual(0, secondCalls, "条件之间检查到取消后不得执行后续条件");
        }

        [Test]
        public async Task ExecuteAsync_AllTrueWithToken_ReturnsTrue()
        {
            int calls = 0;
            var chain = Create()
                .Add((_, __, ___, ____) =>
                {
                    calls++;
                    return UniTask.FromResult(true);
                })
                .Add((_, __, ___, ____) =>
                {
                    calls++;
                    return UniTask.FromResult(true);
                });

            Assert.IsTrue(await chain.ExecuteAsync(CancellationToken.None));
            Assert.AreEqual(2, calls);
        }
    }
}
