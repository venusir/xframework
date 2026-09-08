using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XPool;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// <see cref="PooledObject{T}"/> using 包装器测试。
    /// <para>覆盖：default 构造的 Dispose 空操作、using 块结束自动归还、手动 Dispose 两次在两种
    /// CollectionCheck 下的行为差异。</para>
    /// </summary>
    class PooledObjectTests
    {
        /// <summary>测试用裸类型。</summary>
        private sealed class WrapItem
        {
        }

        /// <summary>与 Pool.cs 错误消息全文一致（全角标点）。</summary>
        private static string ForeignReturnError() =>
            "[Pool<WrapItem>] Return() 传入的对象并非从本池租出，或已被重复归还。已忽略此操作。";

        [Test]
        public void DefaultConstructed_Dispose_IsSafeNoOp()
        {
            var wrapper = default(PooledObject<WrapItem>);

            Assert.DoesNotThrow(() => wrapper.Dispose(), "default 包装器（_pool 为 null）应安全空操作");
        }

        [Test]
        public void UsingBlock_AutoReturns_NextGetReuses()
        {
            var pool = new Pool<WrapItem>(() => new WrapItem());

            WrapItem item;
            using (pool.GetPooled(out item))
            {
                Assert.That(pool.CountInactive, Is.EqualTo(0), "using 块内实例应处于租出状态");
            }

            Assert.That(pool.CountInactive, Is.EqualTo(1), "using 块结束应自动归还");
            Assert.AreSame(item, pool.Get());
        }

        [Test]
        public void DisposeTwice_CollectionCheckOn_SecondRejected()
        {
            var pool = new Pool<WrapItem>(() => new WrapItem(), PoolConfig.Default);

            var handle = pool.GetPooled(out _);
            handle.Dispose();

            LogAssert.Expect(LogType.Error, ForeignReturnError());
            handle.Dispose(); // 第二次 Dispose = 重复归还

            Assert.That(pool.CountInactive, Is.EqualTo(1), "重复归还应被拒绝，闲置数不变");
        }

        [Test]
        public void DisposeTwice_CollectionCheckOff_AcceptsBoth()
        {
            var pool = new Pool<WrapItem>(() => new WrapItem()); // 默认配置 CollectionCheck = false

            var handle = pool.GetPooled(out _);
            handle.Dispose();
            handle.Dispose();

            Assert.That(pool.CountInactive, Is.EqualTo(2), "无检测时两次归还都入池");
        }
    }
}
