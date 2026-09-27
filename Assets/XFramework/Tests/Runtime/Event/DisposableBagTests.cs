using System;
using NUnit.Framework;
using XFramework.XEvent;

namespace XFramework.XEvent.Tests
{
    /// <summary>
    /// <see cref="DisposableBag"/> 的契约测试:三条语义(照抄自 Rx 系实现)各锁一条,外加重入与判空。
    /// <para>本 fixture 不触碰静态门面,故无 SetUp/TearDown。</para>
    /// </summary>
    [TestFixture]
    public class DisposableBagTests
    {
        /// <summary>计数式句柄:记录自己被释放的次数。</summary>
        private sealed class Probe : IDisposable
        {
            public int DisposeCount;

            public void Dispose() => DisposeCount++;
        }

        [Test]
        public void Dispose_DisposesEveryRegisteredItem()
        {
            var bag = new DisposableBag();
            var a = new Probe();
            var b = new Probe();

            bag.Add(a);
            bag.Add(b);
            Assert.AreEqual(2, bag.Count, "登记后应计入数量");

            bag.Dispose();

            Assert.AreEqual(1, a.DisposeCount);
            Assert.AreEqual(1, b.DisposeCount);
            Assert.AreEqual(0, bag.Count, "释放后容器应清空");
        }

        [Test]
        public void Clear_DisposesItems_ButBagStaysUsable()
        {
            var bag = new DisposableBag();
            var first = new Probe();
            bag.Add(first);

            bag.Clear();
            Assert.AreEqual(1, first.DisposeCount, "Clear 应释放已登记的句柄");
            Assert.AreEqual(0, bag.Count);

            // 语义 1:Clear 不是终结——之后登记的句柄归下一轮
            var second = new Probe();
            bag.Add(second);
            bag.Dispose();

            Assert.AreEqual(1, second.DisposeCount, "Clear 之后登记、再由 Dispose 释放");
        }

        [Test]
        public void Dispose_ThenAdd_DisposesImmediately()
        {
            // 语义 2(负向):容器终结后 Add 进来的句柄必须**立即**释放,而不是被静默丢弃
            var bag = new DisposableBag();
            bag.Dispose();

            var late = new Probe();
            bag.Add(late);

            Assert.AreEqual(1, late.DisposeCount, "已终结的容器必须立即释放新句柄,不得静默吞掉");
            Assert.AreEqual(0, bag.Count, "立即释放的句柄不登记");
        }

        [Test]
        public void Clear_WhenItemDisposeAddsAnother_DoesNotThrowOrLose()
        {
            // 重入:某个句柄的释放动作里再 Add——新项归新的一轮,不得被本轮丢弃
            var bag = new DisposableBag();
            var inner = new Probe();

            bag.Add(new ActionDisposableProbe(() => bag.Add(inner)));

            Assert.DoesNotThrow(() => bag.Clear());
            Assert.AreEqual(1, bag.Count, "重入 Add 的句柄应留在容器里等下一轮");

            bag.Dispose();
            Assert.AreEqual(1, inner.DisposeCount, "重入登记进来的句柄最终也被释放");
        }

        [Test]
        public void Add_Null_Throws()
        {
            var bag = new DisposableBag();
            Assert.Throws<ArgumentNullException>(() => bag.Add(null));
        }

        [Test]
        public void AddTo_RegistersAndReturnsSameHandle_ForChaining()
        {
            // 语义 3:AddTo 返回句柄本身,便于链式书写
            var bag = new DisposableBag();
            var probe = new Probe();

            var returned = probe.AddTo(bag);

            Assert.AreSame(probe, returned, "AddTo 应返回同一句柄");
            Assert.AreEqual(1, bag.Count);

            bag.Dispose();
            Assert.AreEqual(1, probe.DisposeCount);
        }

        [Test]
        public void AddTo_NullBag_Throws()
        {
            var probe = new Probe();
            Assert.Throws<ArgumentNullException>(() => probe.AddTo(null));
        }

        /// <summary>释放时执行回action 的句柄,用于构造重入场景。</summary>
        private sealed class ActionDisposableProbe : IDisposable
        {
            private readonly Action _onDispose;

            public ActionDisposableProbe(Action onDispose) => _onDispose = onDispose;

            public void Dispose() => _onDispose?.Invoke();
        }
    }
}
