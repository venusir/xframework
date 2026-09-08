using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
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

        /// <summary>Configure 预热生效用例专用类型。</summary>
        private sealed class ConfigureItem
        {
            public ConfigureItem()
            {
            }
        }

        /// <summary>Configure 带生成器用例专用类型。</summary>
        private sealed class ConfigureGenItem
        {
            public ConfigureGenItem()
            {
            }
        }

        /// <summary>Configure 一次性消费用例专用类型。</summary>
        private sealed class ConsumedItem
        {
            public ConsumedItem()
            {
            }
        }

        /// <summary>显式生成器优先用例专用类型。</summary>
        private sealed class ExplicitGenItem
        {
            public ExplicitGenItem()
            {
            }
        }

        /// <summary>GetPooled 消费配置用例专用类型。</summary>
        private sealed class UsingGenItem
        {
            public UsingGenItem()
            {
            }
        }

        /// <summary>Configure 池已存在告警用例专用类型。</summary>
        private sealed class ReconfigureItem
        {
            public ReconfigureItem()
            {
            }
        }

        /// <summary>类型单池（第二生成器忽略）用例专用类型。</summary>
        private sealed class DualGenItem
        {
            public DualGenItem()
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
            PoolManager.RemovePool<ConfigureItem>();
            PoolManager.RemovePool<ConfigureGenItem>();
            PoolManager.RemovePool<ConsumedItem>();
            PoolManager.RemovePool<ExplicitGenItem>();
            PoolManager.RemovePool<UsingGenItem>();
            PoolManager.RemovePool<ReconfigureItem>();
            PoolManager.RemovePool<DualGenItem>();
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

        [Test]
        public void Configure_Prewarm_AppliedOnFirstPoolCreation()
        {
            PoolManager.Configure<ConfigureItem>(new PoolConfig { PrewarmSize = 3 });

            var pool = PoolManager.GetPool<ConfigureItem>();

            Assert.That(pool.CountInactive, Is.EqualTo(3), "配置的预热应在首次建池时生效");
            Assert.That(pool.CountAll, Is.EqualTo(3));
        }

        [Test]
        public void Configure_WithGenerator_ParameterlessGetUsesIt()
        {
            var configuredCalls = 0;
            PoolManager.Configure<ConfigureGenItem>(new PoolConfig { MaxSize = 10 },
                () => { configuredCalls++; return new ConfigureGenItem(); });

            var item = PoolManager.Get<ConfigureGenItem>();

            Assert.That(configuredCalls, Is.EqualTo(1), "无参入口建池应使用 Configure 的生成器");
            PoolManager.Return(item);
            Assert.AreSame(item, PoolManager.Get<ConfigureGenItem>());
        }

        [Test]
        public void Configure_ConsumedOnce_AfterRemovePool_DefaultApplies()
        {
            PoolManager.Configure<ConsumedItem>(new PoolConfig { PrewarmSize = 2 });
            var first = PoolManager.Get<ConsumedItem>(); // 消费配置：预热 2 并取出 1
            PoolManager.RemovePool<ConsumedItem>();

            var second = PoolManager.Get<ConsumedItem>();

            Assert.That(PoolManager.GetPool<ConsumedItem>().CountAll, Is.EqualTo(1),
                "配置一次性消费，池移除后重建不再生效");
            Assert.AreNotSame(first, second);
            PoolManager.Return(second);
        }

        [Test]
        public void Configure_ThenExplicitGenerator_ConfigAppliesAndExplicitWins()
        {
            var configuredCalls = 0;
            var explicitCalls = 0;
            PoolManager.Configure<ExplicitGenItem>(new PoolConfig { PrewarmSize = 2 },
                () => { configuredCalls++; return new ExplicitGenItem(); });

            var item = PoolManager.Get<ExplicitGenItem>(() =>
            {
                explicitCalls++;
                return new ExplicitGenItem();
            });

            Assert.That(explicitCalls, Is.EqualTo(2), "预热实例应产自显式生成器");
            Assert.That(configuredCalls, Is.EqualTo(0), "显式生成器应优先于 Configure 的生成器");
            Assert.NotNull(item);
            PoolManager.Return(item);
            Assert.AreSame(item, PoolManager.Get<ExplicitGenItem>());
        }

        [Test]
        public void GetPooled_WithGenerator_ConsumesConfiguredEntry()
        {
            var configuredCalls = 0;
            var explicitCalls = 0;
            PoolManager.Configure<UsingGenItem>(new PoolConfig { PrewarmSize = 1 },
                () => { configuredCalls++; return new UsingGenItem(); });

            UsingGenItem item;
            using (PoolManager.GetPooled<UsingGenItem>(() =>
            {
                explicitCalls++;
                return new UsingGenItem();
            }, out item))
            {
                Assert.That(explicitCalls, Is.EqualTo(1), "预热的实例应产自显式生成器");
                Assert.That(configuredCalls, Is.EqualTo(0));
            }

            Assert.That(PoolManager.GetPool<UsingGenItem>().CountInactive, Is.EqualTo(1),
                "using 结束应自动归还");
            Assert.AreSame(item, PoolManager.Get<UsingGenItem>());
        }

        [Test]
        public void Configure_PoolAlreadyExists_WarnsAndIgnores()
        {
            var item = PoolManager.Get<ReconfigureItem>(); // 池已创建

            LogAssert.Expect(LogType.Warning,
                "[PoolManager] 类型 ReconfigureItem 的池已创建，Configure 已忽略。如需重新配置，请先调用 RemovePool<ReconfigureItem>()。");
            PoolManager.Configure<ReconfigureItem>(new PoolConfig { PrewarmSize = 5 });

            PoolManager.Return(item);
            PoolManager.RemovePool<ReconfigureItem>();
            var fresh = PoolManager.Get<ReconfigureItem>();
            Assert.That(PoolManager.GetPool<ReconfigureItem>().CountAll, Is.EqualTo(1),
                "告警后配置不应写入（重建池无预热）");
            PoolManager.Return(fresh);
        }

        [Test]
        public void Get_SecondGenerator_Ignored_TypeIsSinglePool()
        {
            var callsA = 0;
            var callsB = 0;
            var a = PoolManager.Get<DualGenItem>(() => { callsA++; return new DualGenItem(); });
            var b = PoolManager.Get<DualGenItem>(() => { callsB++; return new DualGenItem(); });

            Assert.That(callsA, Is.EqualTo(2), "池空时第二次 Get 应继续使用首个生成器");
            Assert.That(callsB, Is.EqualTo(0), "池已存在时传入的生成器应被忽略");
            Assert.AreNotSame(a, b);
            PoolManager.Return(b);
        }
    }
}
