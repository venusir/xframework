using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using XFramework.XAsset;

namespace XFramework.XAsset.Tests
{
    /// <summary>
    /// <see cref="AssetManagerImpl"/> 对象池语义测试。
    /// <para><b>为什么不依赖 YooAsset（因此可测）</b>：<c>DestroyInstance</c> 不调 <c>EnsureInitialized()</c>，
    /// 而 <see cref="InstanceTracker.SetHandle"/> 接受 <c>default</c> 句柄（其 <c>Dispose()</c> 是空操作）。
    /// 于是回池、去重、池满、清池、计数全部走真实实现，只有「加载」那一步被省掉——
    /// 这几条此前是**可测而未测**。</para>
    /// <para><b>为什么在 PlayMode</b>：销毁路径调 <c>Object.Destroy</c>，EditMode 下引擎会记一条
    /// 「Destroy may not be called from edit mode」错误，被测试框架判为未预期日志。</para>
    /// <para><b>复位</b>：fixture 自建 GameObject 并在 TearDown 全部 <c>DestroyImmediate</c>，
    /// 使 <see cref="InstanceTracker"/> 的私有静态活跃计数回到 0（无公开复位入口，用「平衡」代替复位）
    /// ——PlayMode 下所有用例共享一个 player 实例，不复位即互相污染。</para>
    /// </summary>
    [TestFixture]
    public class AssetPoolTests
    {
        #region Private Fields

        private const string Location = "pool/test_prefab";

        /// <summary>README 与 <c>DefaultPoolSize</c> 的承诺：每种预制体默认最多保留 5 个闲置实例。</summary>
        private const int DefaultPoolSize = 5;

        private AssetManagerImpl _manager;
        private readonly List<GameObject> _created = new List<GameObject>();

        #endregion

        #region Lifecycle

        [SetUp]
        public void SetUp()
        {
            _manager = new AssetManagerImpl();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }
            _created.Clear();
            _manager = null;
        }

        #endregion

        #region Tests

        [Test]
        public void DestroyInstance_ReturnsInstanceToPool()
        {
            var go = CreateManagedInstance();

            _manager.DestroyInstance(go);

            var status = _manager.GetPoolStatus(Location);
            Assert.AreEqual(1, status.pooledCount, "实例应回池");
            Assert.AreEqual(0, status.activeCount, "回池即失活，活跃计数应归零");
        }

        [Test]
        public void DestroyInstance_Twice_DoesNotPoolTheSameInstanceTwice()
        {
            var go = CreateManagedInstance();

            _manager.DestroyInstance(go);
            _manager.DestroyInstance(go); // 重复调用：两处清理逻辑都碰了它

            Assert.AreEqual(1, _manager.GetPoolStatus(Location).pooledCount,
                "同一实例只应入池一份——入池两份会让两次取出拿到同一个 GameObject");
        }

        [Test]
        public void DestroyInstance_PoolFull_DoesNotPool()
        {
            _manager.SetPoolMaxSize(Location, 1);
            var first = CreateManagedInstance();
            var second = CreateManagedInstance();

            _manager.DestroyInstance(first);
            _manager.DestroyInstance(second); // 池满：走销毁路径

            var status = _manager.GetPoolStatus(Location);
            Assert.AreEqual(1, status.pooledCount, "池满时不得再入池");
            Assert.AreEqual(1, status.maxPoolSize);
        }

        [Test]
        public void DestroyInstance_UnmanagedObject_IsNotPooled()
        {
            var go = new GameObject("unmanaged");
            _created.Add(go); // 无 InstanceTracker：不属于池管理

            _manager.DestroyInstance(go);

            Assert.AreEqual(0, _manager.GetPoolStatus(Location).pooledCount, "无追踪器的实例不进池");
        }

        [Test]
        public void ClearPool_DestroysPooledInstancesAndReturnsCount()
        {
            for (int i = 0; i < 3; i++)
                _manager.DestroyInstance(CreateManagedInstance());

            int cleared = _manager.ClearPool(Location);

            Assert.AreEqual(3, cleared, "应返回实际销毁的闲置实例数");
            Assert.AreEqual(0, _manager.GetPoolStatus(Location).pooledCount, "清池后池应空");
        }

        [Test]
        public void ClearAllPools_ClearsEveryLocation()
        {
            _manager.DestroyInstance(CreateManagedInstance(Location));
            _manager.DestroyInstance(CreateManagedInstance("pool/other_prefab"));

            int cleared = _manager.ClearAllPools();

            Assert.AreEqual(2, cleared, "应跨地址累计销毁数");
            Assert.AreEqual(0, _manager.GetPoolStatus(Location).pooledCount);
            Assert.AreEqual(0, _manager.GetPoolStatus("pool/other_prefab").pooledCount);
        }

        [Test]
        public void ClearPool_UnknownLocation_ReturnsZero()
        {
            Assert.AreEqual(0, _manager.ClearPool("pool/never_pooled"));
        }

        [Test]
        public void SetPoolMaxSize_BelowOne_ClampsToOne()
        {
            _manager.SetPoolMaxSize(Location, 0);

            Assert.AreEqual(1, _manager.GetPoolStatus(Location).maxPoolSize, "容量下限为 1");
        }

        [Test]
        public void GetPoolStatus_UnknownLocation_ReportsDefaultMaxSize()
        {
            var status = _manager.GetPoolStatus("pool/never_seen");

            Assert.AreEqual(0, status.pooledCount);
            Assert.AreEqual(0, status.activeCount);
            Assert.AreEqual(DefaultPoolSize, status.maxPoolSize, "未配置时用默认容量");
        }

        #endregion

        #region Helpers

        /// <summary>
        /// 建一个「已托管」实例：挂 <see cref="InstanceTracker"/> 并登记地址。
        /// 句柄用 <c>default</c>——它的 <c>Dispose()</c> 是空操作，测试因此不需要 YooAsset。
        /// </summary>
        private GameObject CreateManagedInstance(string location = Location)
        {
            var go = new GameObject("pooled_instance");
            _created.Add(go);

            var tracker = go.AddComponent<InstanceTracker>();
            tracker.SetHandle(default, location);
            return go;
        }

        #endregion
    }
}
