using System;
using NUnit.Framework;
using Unity.PerformanceTesting;
using XFramework.XMessage;
using XFramework.XPool;
using XFramework.XReactive;
using XFramework.XSerialize;
using XFramework.XUpdate;

namespace XFramework.Performance.Tests
{
    /// <summary>
    /// 性能基准套件。**不属于门禁**：用 <see cref="ExplicitAttribute"/> 隔离，只在显式指定 fixture 时运行。
    /// <para>只测量、不做零 GC 断言——本环境的分配尺子不可用，它不产出吞吐 / 耗时基准。</para>
    /// <para>运行：<c>pwsh -File Tools/run-tests.ps1 -Fixture XFrameworkPerformanceBenchmarks -Platform EditMode</c>。</para>
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    [Explicit("性能基准不是门禁的一部分，显式运行才执行。")]
    public class XFrameworkPerformanceBenchmarks
    {
        #region Constants

        /// <summary>每个测量样本内的操作次数。固定值，便于跨版本比较。</summary>
        private const int OperationsPerSample = 1000;

        #endregion

        #region Lifecycle

        /// <summary>确保序列化器的内置实现已注册（EditMode 下不经过 RuntimeInitializeOnLoad）。</summary>
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            Serializer.Initialize();
        }

        /// <summary>复位本 fixture 触碰的静态门面，避免显式运行后污染同进程的其它用例。</summary>
        [TearDown]
        public void TearDown()
        {
            MessageManager.Clear();
            UpdateManager.Clear();
            PoolManager.RemovePool<BenchItem>();
        }

        #endregion

        #region Benchmarks

        [Test, Performance]
        public void Message_Publish_1000()
        {
            using (MessageManager.Subscribe<BenchMessage>(_ => { }))
            {
                Measure.Method(() =>
                {
                    for (int i = 0; i < OperationsPerSample; i++)
                        MessageManager.Publish(new BenchMessage(i));
                })
                .WarmupCount(3)
                .MeasurementCount(5)
                .Run();
            }
        }

        [Test, Performance]
        public void ReactiveProperty_Set_1000()
        {
            using (var property = new ReactiveProperty<int>(0))
            using (property.Subscribe(_ => { }))
            {
                int value = 0;
                Measure.Method(() =>
                {
                    for (int i = 0; i < OperationsPerSample; i++)
                        property.Value = ++value;
                })
                .WarmupCount(3)
                .MeasurementCount(5)
                .Run();
            }
        }

        [Test, Performance]
        public void ReactiveList_Add_1000()
        {
            var list = new ReactiveList<int>();
            using (list.Subscribe(_ => { }))
            {
                Measure.Method(() =>
                {
                    list.Clear();
                    for (int i = 0; i < OperationsPerSample; i++)
                        list.Add(i);
                })
                .WarmupCount(3)
                .MeasurementCount(5)
                .Run();
            }
        }

        [Test, Performance]
        public void Pool_GetReturn_1000()
        {
            Measure.Method(() =>
            {
                for (int i = 0; i < OperationsPerSample; i++)
                {
                    var item = PoolManager.Get<BenchItem>();
                    PoolManager.Return(item);
                }
            })
            .WarmupCount(3)
            .MeasurementCount(5)
            .Run();
        }

        [Test, Performance]
        public void Update_Tick_1000Nodes()
        {
            var nodes = new IUpdateable[OperationsPerSample];
            for (int i = 0; i < nodes.Length; i++)
            {
                nodes[i] = new BenchUpdateNode();
                UpdateManager.Register(nodes[i], order: i);
            }

            float time = 0f;
            Measure.Method(() =>
            {
                time += 1f / 60f;
                UpdateManager.Tick(time);
            })
            .WarmupCount(3)
            .MeasurementCount(5)
            .Run();

            for (int i = 0; i < nodes.Length; i++)
                UpdateManager.Unregister(nodes[i]);
        }

        [Test, Performance]
        public void Serializer_RoundTrip()
        {
            var payload = new BenchPayload { Id = 42, Name = "xframework" };
            var type = typeof(BenchPayload);
            var serializer = Serializer.Default;

            Measure.Method(() =>
            {
                byte[] bytes = serializer.Serialize(payload, type);
                serializer.Deserialize(bytes, type);
            })
            .WarmupCount(3)
            .MeasurementCount(5)
            .Run();
        }

        #endregion

        #region Benchmark Types

        private readonly struct BenchMessage
        {
            public readonly int Value;

            public BenchMessage(int value)
            {
                Value = value;
            }
        }

        private sealed class BenchItem
        {
        }

        private sealed class BenchUpdateNode : IUpdateable
        {
            public void OnEnable()
            {
            }

            public void OnDisable()
            {
            }

            public UpdateTier OnUpdate(float deltaTime, float time)
            {
                return UpdateTier.Tier0;
            }
        }

        private sealed class BenchPayload
        {
            public int Id { get; set; }

            public string Name { get; set; }
        }

        #endregion
    }
}
