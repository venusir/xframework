using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XPool;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// <see cref="Pool{T}"/> 泛型对象池核心测试。
    /// <para>覆盖：生成与复用、预热与容量、Clear 语义（回归：活跃实例 Clear 后仍可归还复用）、
    /// CollectionCheck 重复归还检测、回调优先级（委托 > IPoolable）、GetPooled 手动归还。</para>
    /// <para>全部直接实例化 <see cref="Pool{T}"/>，不经静态门面，无跨测试静态状态。</para>
    /// </summary>
    class PoolTests
    {
        /// <summary>无回调裸类型。</summary>
        private sealed class TestItem
        {
        }

        /// <summary>实现 <see cref="IPoolable"/> 的计数类型。</summary>
        private sealed class PoolableItem : IPoolable
        {
            public int RentCount;
            public int ReturnCount;

            void IPoolable.OnRent() => RentCount++;
            void IPoolable.OnReturn() => ReturnCount++;
        }

        /// <summary>与 Pool.cs 错误消息全文一致（全角标点）。</summary>
        private static string ForeignReturnError<T>() =>
            $"[Pool<{typeof(T).Name}>] Return() 传入的对象并非从本池租出，或已被重复归还。已忽略此操作。";

        [Test]
        public void Ctor_NullGenerator_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new Pool<TestItem>(null));
        }

        [Test]
        public void Get_OnEmpty_CreatesViaGeneratorAndTracksCount()
        {
            var created = 0;
            var pool = new Pool<TestItem>(() => { created++; return new TestItem(); });

            var item = pool.Get();

            Assert.NotNull(item);
            Assert.That(created, Is.EqualTo(1), "池空时应调用生成器");
            Assert.That(pool.CountAll, Is.EqualTo(1));
            Assert.That(pool.CountInactive, Is.EqualTo(0));
        }

        [Test]
        public void Return_ThenGet_ReusesSameInstance()
        {
            var pool = new Pool<TestItem>(() => new TestItem());

            var item = pool.Get();
            pool.Return(item);

            Assert.That(pool.CountInactive, Is.EqualTo(1));
            Assert.That(pool.CountAll, Is.EqualTo(1), "归还不应新建实例");
            Assert.AreSame(item, pool.Get(), "闲置实例应被原样复用");
        }

        [Test]
        public void Get_WithIdle_DoesNotInvokeGenerator()
        {
            var created = 0;
            var pool = new Pool<TestItem>(() => { created++; return new TestItem(); });

            var a = pool.Get();
            var b = pool.Get();
            pool.Return(b);
            var c = pool.Get();

            Assert.AreSame(b, c);
            Assert.That(created, Is.EqualTo(2), "有闲置实例时不应调用生成器");
        }

        [Test]
        public void Prewarm_CreatesAllUpfront()
        {
            var created = 0;
            var pool = new Pool<TestItem>(
                () => { created++; return new TestItem(); },
                new PoolConfig { PrewarmSize = 3 });

            Assert.That(created, Is.EqualTo(3), "预热应在构造时一次创建");
            Assert.That(pool.CountInactive, Is.EqualTo(3));
            Assert.That(pool.CountAll, Is.EqualTo(3));
        }

        [Test]
        public void MaxSize_ExceededIdle_IsDiscarded()
        {
            var pool = new Pool<TestItem>(() => new TestItem(), new PoolConfig { MaxSize = 2 });
            var items = new TestItem[4];
            for (int i = 0; i < items.Length; i++)
                items[i] = pool.Get();
            for (int i = 0; i < items.Length; i++)
                pool.Return(items[i]);

            Assert.That(pool.CountInactive, Is.EqualTo(2), "超出 MaxSize 的闲置实例应被丢弃");
            Assert.That(pool.CountAll, Is.EqualTo(4));
        }

        [Test]
        public void Clear_ThenReturnOutstanding_RepoolsWithoutError()
        {
            // 回归：修复前 Clear 同时清空 Editor 活跃追踪集，租出实例归还时误报并拒绝入池
            var pool = new Pool<TestItem>(() => new TestItem(), PoolConfig.Default);

            var item = pool.Get();
            pool.Clear();

            Assert.That(pool.CountInactive, Is.EqualTo(0));
            pool.Return(item);
            Assert.That(pool.CountInactive, Is.EqualTo(1), "Clear 后活跃实例归还应重新入池");
            Assert.AreSame(item, pool.Get());
        }

        [Test]
        public void Clear_OnlyDropsIdle_ActiveInstancesKeepReturnable()
        {
            var pool = new Pool<TestItem>(() => new TestItem(), PoolConfig.Default);

            var a = pool.Get();
            var b = pool.Get();
            pool.Return(b); // b 闲置、a 活跃
            pool.Clear();

            Assert.That(pool.CountInactive, Is.EqualTo(0), "Clear 只应清闲置实例");
            pool.Return(a);
            Assert.That(pool.CountInactive, Is.EqualTo(1));
            Assert.AreSame(a, pool.Get());
        }

        [Test]
        public void Return_ForeignInstance_LogsErrorAndRejects()
        {
            var pool = new Pool<TestItem>(() => new TestItem(), PoolConfig.Default);
            var foreign = new TestItem();

            LogAssert.Expect(LogType.Error, ForeignReturnError<TestItem>());
            pool.Return(foreign);

            Assert.That(pool.CountInactive, Is.EqualTo(0), "非本池租出的实例应被拒绝");
            Assert.That(pool.CountAll, Is.EqualTo(0));
        }

        [Test]
        public void Return_Twice_SecondLogsErrorAndRejected()
        {
            var pool = new Pool<TestItem>(() => new TestItem(), PoolConfig.Default);

            var item = pool.Get();
            pool.Return(item);
            LogAssert.Expect(LogType.Error, ForeignReturnError<TestItem>());
            pool.Return(item); // 第二次归还是重复归还

            Assert.That(pool.CountInactive, Is.EqualTo(1));
        }

        [Test]
        public void CollectionCheck_Disabled_DuplicateReturnAllowed()
        {
            var pool = new Pool<TestItem>(() => new TestItem()); // 默认配置 CollectionCheck = false

            var item = pool.Get();
            pool.Return(item);
            pool.Return(item); // 无检测时重复归还放行

            Assert.That(pool.CountInactive, Is.EqualTo(2));
        }

        [Test]
        public void Return_Null_IsSilentNoOp()
        {
            var pool = new Pool<TestItem>(() => new TestItem());

            pool.Return(null);

            Assert.That(pool.CountInactive, Is.EqualTo(0));
            Assert.That(pool.CountAll, Is.EqualTo(0));
        }

        [Test]
        public void Delegates_Only_InvokedPerRentAndReturn()
        {
            var rent = 0;
            var returned = 0;
            var pool = new Pool<TestItem>(() => new TestItem(), PoolConfig.Default,
                onRent: _ => rent++, onReturn: _ => returned++);

            var item = pool.Get();
            pool.Return(item);

            Assert.That(rent, Is.EqualTo(1));
            Assert.That(returned, Is.EqualTo(1));
        }

        [Test]
        public void Delegates_TakePrecedenceOverInterface()
        {
            var rentDelegate = 0;
            var pool = new Pool<PoolableItem>(() => new PoolableItem(), PoolConfig.Default,
                onRent: _ => rentDelegate++);

            var item = pool.Get();

            Assert.That(rentDelegate, Is.EqualTo(1), "委托应触发");
            Assert.That(item.RentCount, Is.EqualTo(0), "存在委托时接口 OnRent 不应触发");

            // 未传 onReturn 委托，归还侧应回落接口回调
            pool.Return(item);
            Assert.That(item.ReturnCount, Is.EqualTo(1));
        }

        [Test]
        public void Interface_Only_BothCallbacksTriggered()
        {
            var pool = new Pool<PoolableItem>(() => new PoolableItem(), PoolConfig.Default);

            var item = pool.Get();
            Assert.That(item.RentCount, Is.EqualTo(1));

            pool.Return(item);
            Assert.That(item.ReturnCount, Is.EqualTo(1));
            Assert.That(pool.CountInactive, Is.EqualTo(1));
        }

        [Test]
        public void GetPooled_ManualDispose_ReturnsInstance()
        {
            var pool = new Pool<TestItem>(() => new TestItem());

            var handle = pool.GetPooled(out var item);
            handle.Dispose();

            Assert.That(pool.CountInactive, Is.EqualTo(1));
            Assert.AreSame(item, pool.Get());
        }
    }
}
