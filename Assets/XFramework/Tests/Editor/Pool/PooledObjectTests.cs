using NUnit.Framework;
using XFramework.XPool;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// <see cref="PooledObject{T}"/> using 包装器测试。
    /// <para>覆盖：default 构造的 Dispose 空操作、using 块结束自动归还、重复 Dispose 幂等、
    /// <see cref="PooledObject{T}.Value"/> 取回租出实例。</para>
    /// </summary>
    class PooledObjectTests
    {
        /// <summary>测试用裸类型。</summary>
        private sealed class WrapItem
        {
        }

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
        public void Value_ReturnsRentedInstance()
        {
            var pool = new Pool<WrapItem>(() => new WrapItem());

            var handle = pool.GetPooled(out var item);

            Assert.AreSame(item, handle.Value, "Value 应就是取出的那个实例");
            handle.Dispose();
        }

        [Test]
        public void DisposeTwice_SecondIsSilentNoOp()
        {
            // 加幂等位之前，第二次 Dispose 会一路走到 Return：CollectionCheck 开启时
            // 报一条「重复归还」错误并被拒绝入池
            var pool = new Pool<WrapItem>(() => new WrapItem(), PoolConfig.Default);

            var handle = pool.GetPooled(out var item);
            handle.Dispose();
            handle.Dispose();

            Assert.That(pool.CountInactive, Is.EqualTo(1), "重复归还不应把同一实例入池两次");
            Assert.AreSame(item, pool.Get(), "实例应原样留在池中");
        }
    }
}
