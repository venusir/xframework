using NUnit.Framework;
using XFramework.XPool;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// <see cref="PoolManager"/> 静态门面测试。
    /// <para>覆盖：惰性建池、生成器建池与复用、GetPooled、未注册类型归还静默、RemovePool、
    /// ClearAll 新语义（回归：清闲置但保留注册与配置，归还照常，幂等）。</para>
    /// <para>隔离：PoolManager 为静态全局状态，每个用例使用独占类型作键，TearDown 统一
    /// <c>RemovePool</c> 清理（幂等），保证无域重载重跑时互不串扰。</para>
    /// </summary>
    class PoolManagerTests
    {
        /// <summary>惰性建池用例专用类型（public 构造以满足 <c>new()</c> 约束）。</summary>
        private sealed class LazilyItem
        {
            public LazilyItem()
            {
            }
        }

        /// <summary>生成器建池用例专用类型。</summary>
        private sealed class GeneratorItem
        {
            public GeneratorItem()
            {
            }
        }

        /// <summary>GetPooled 用例专用类型。</summary>
        private sealed class UsingItem
        {
            public UsingItem()
            {
            }
        }

        /// <summary>未注册归还用例专用类型。</summary>
        private sealed class UnregisteredItem
        {
            public UnregisteredItem()
            {
            }
        }

        /// <summary>RemovePool 用例专用类型。</summary>
        private sealed class RemovedItem
        {
            public RemovedItem()
            {
            }
        }

        /// <summary>ClearAll 保留注册用例专用类型。</summary>
        private sealed class ClearItem
        {
            public ClearItem()
            {
            }
        }

        /// <summary>ClearAll 后归还活跃实例用例专用类型。</summary>
        private sealed class ClearReturnItem
        {
            public ClearReturnItem()
            {
            }
        }

        [TearDown]
        public void TearDown()
        {
            // 幂等清理：每个用例的独占类型若建过池则移除，防止跨用例/跨重跑串扰
            PoolManager.RemovePool<LazilyItem>();
            PoolManager.RemovePool<GeneratorItem>();
            PoolManager.RemovePool<UsingItem>();
            PoolManager.RemovePool<UnregisteredItem>();
            PoolManager.RemovePool<RemovedItem>();
            PoolManager.RemovePool<ClearItem>();
            PoolManager.RemovePool<ClearReturnItem>();
        }

        [Test]
        public void Get_OnDemand_CreatesPoolLazily()
        {
            Assert.That(PoolManager.HasPool<LazilyItem>(), Is.False, "未触碰的类型不应建池");

            var item = PoolManager.Get<LazilyItem>();

            Assert.That(PoolManager.HasPool<LazilyItem>(), Is.True, "首次 Get 应惰性建池");
            PoolManager.Return(item);
            Assert.That(PoolManager.GetPool<LazilyItem>().CountInactive, Is.EqualTo(1));
            Assert.AreSame(item, PoolManager.Get<LazilyItem>());
            Assert.That(PoolManager.GetPool<LazilyItem>().CountAll, Is.EqualTo(1),
                "复用不应新建实例");
        }

        [Test]
        public void Get_WithGenerator_CreatesPoolAndKeepsGenerator()
        {
            var genCalls = 0;
            var first = PoolManager.Get<GeneratorItem>(() =>
            {
                genCalls++;
                return new GeneratorItem();
            });

            Assert.That(genCalls, Is.EqualTo(1));
            var second = PoolManager.GetPool<GeneratorItem>().Get();
            Assert.That(genCalls, Is.EqualTo(2), "池空时后续 Get 应继续使用建池时的生成器");
            Assert.AreNotSame(first, second);

            PoolManager.Return(second);
            Assert.AreSame(second, PoolManager.Get<GeneratorItem>(), "应复用闲置实例");
        }

        [Test]
        public void GetPooled_ThroughFacade_AutoReturnsOnDispose()
        {
            UsingItem item;
            using (PoolManager.GetPooled<UsingItem>(out item))
            {
                Assert.That(PoolManager.GetPool<UsingItem>().CountInactive, Is.EqualTo(0),
                    "using 块内实例应处于租出状态");
            }

            Assert.That(PoolManager.GetPool<UsingItem>().CountInactive, Is.EqualTo(1),
                "using 块结束应自动归还");
            Assert.AreSame(item, PoolManager.Get<UsingItem>());
        }

        [Test]
        public void Return_UnregisteredType_IsSilentNoOp()
        {
            var item = new UnregisteredItem();

            Assert.DoesNotThrow(() => PoolManager.Return(item));
            Assert.That(PoolManager.HasPool<UnregisteredItem>(), Is.False,
                "归还未注册类型不应建池");
        }

        [Test]
        public void RemovePool_ThenReturnOldInstance_SilentAndNextGetIsFresh()
        {
            var item = PoolManager.Get<RemovedItem>();
            PoolManager.RemovePool<RemovedItem>();
            Assert.That(PoolManager.HasPool<RemovedItem>(), Is.False);

            PoolManager.Return(item); // 池已移除：静默忽略
            var fresh = PoolManager.Get<RemovedItem>();
            Assert.AreNotSame(item, fresh, "移除后再次 Get 应得到全新池的新实例");
            Assert.That(PoolManager.GetPool<RemovedItem>().CountAll, Is.EqualTo(1));
            PoolManager.Return(fresh);
        }

        [Test]
        public void ClearAll_KeepsRegistrations_ClearsIdleAndIdempotent()
        {
            // 回归：旧实现置 _destroyed 后 Return 永久失效
            var item = PoolManager.Get<ClearItem>();
            PoolManager.Return(item); // 闲置 1
            Assert.That(PoolManager.GetPool<ClearItem>().CountInactive, Is.EqualTo(1));

            PoolManager.ClearAll();

            Assert.That(PoolManager.HasPool<ClearItem>(), Is.True, "ClearAll 后池注册应保留");
            Assert.That(PoolManager.GetPool<ClearItem>().CountAll, Is.EqualTo(1));
            Assert.That(PoolManager.GetPool<ClearItem>().CountInactive, Is.EqualTo(0),
                "闲置实例应被清空");
            PoolManager.ClearAll(); // 幂等

            var again = PoolManager.Get<ClearItem>();
            PoolManager.Return(again);
            Assert.That(PoolManager.GetPool<ClearItem>().CountInactive, Is.EqualTo(1),
                "ClearAll 后 Get / Return 照常工作");
        }

        [Test]
        public void ClearAll_ThenReturnOutstanding_RepoolsWithoutError()
        {
            // 回归（①+② 联动）：ClearAll 不应影响租出实例的后续归还
            var item = PoolManager.Get<ClearReturnItem>(); // 租出
            PoolManager.ClearAll();

            PoolManager.Return(item);

            Assert.That(PoolManager.GetPool<ClearReturnItem>().CountInactive, Is.EqualTo(1),
                "ClearAll 后归还租出实例应重新入池");
            Assert.AreSame(item, PoolManager.Get<ClearReturnItem>());
        }
    }
}
