using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using XFramework.XPool;

namespace XFramework.XPool.Tests
{
    /// <summary>
    /// Pool 的 <b>PlayMode 侧</b>覆盖：跨帧的持有 / 归还 / 复用。
    /// <para><b>为什么值得单独一组</b>：本模块此前的入池-归还用例都是**同一时间片内**完成的
    /// （取出、断言、还回去）。而真实用法里实例常常跨帧被持有——`using` 块的 `Dispose` 落在某一帧的
    /// 末尾、归还后的复用发生在再下一帧。`IPoolable` 的回调此前也只验过**次数**，没验过它们落在哪一帧。
    /// 本模块此前的用例全在 <c>Tests/Editor/</c>（2026-10-13 覆盖失衡盘点的处置之一）。</para>
    /// <para>纯 C# 对象，不碰 Unity GameObject——这一组验的是**时间**，不是引擎生命周期。</para>
    /// </summary>
    [TestFixture]
    public class PoolFrameLifecycleTests
    {
        #region Test Doubles

        private sealed class Item
        {
            public int Value;
        }

        /// <summary>带生命周期回调的替身：把每次回调时的帧号记下来，「跨帧」由此可验。</summary>
        private sealed class FramedItem : IPoolable
        {
            public readonly List<int> RentFrames = new List<int>();
            public readonly List<int> ReturnFrames = new List<int>();

            void IPoolable.OnRent() => RentFrames.Add(Time.frameCount);

            void IPoolable.OnReturn() => ReturnFrames.Add(Time.frameCount);
        }

        #endregion

        #region Fixture

        [SetUp]
        public void SetUp()
        {
            PoolManager.RemovePool<Item>();
            PoolManager.RemovePool<FramedItem>();
        }

        [TearDown]
        public void TearDown()
        {
            // 池是静态的，PlayMode 下所有用例共享一个 player 实例——不复位即互相污染
            PoolManager.RemovePool<Item>();
            PoolManager.RemovePool<FramedItem>();
            PoolManager.ClearAll();
        }

        #endregion

        #region 跨帧持有与复用

        [Test]
        public async Task GetPooled_HeldAcrossFrame_ReturnsOnDispose_AndNextRentReusesInstance()
        {
            Item item;
            using (PoolManager.GetPooled<Item>(out item))
            {
                await UniTask.Yield(); // 跨帧持有

                IPool<Item> held = PoolManager.GetPool<Item>();
                Assert.AreEqual(1, held.CountActive, "跨帧持有期间应当算一个活跃实例");
                Assert.AreEqual(0, held.CountInactive, "此刻还没有闲置实例");
            }

            // using 块结束 → 归还发生在跨帧之后的 finally 里
            IPool<Item> afterReturn = PoolManager.GetPool<Item>();
            Assert.AreEqual(0, afterReturn.CountActive, "归还后活跃数应归零");
            Assert.AreEqual(1, afterReturn.CountInactive, "归还后应当有一个闲置实例");

            await UniTask.Yield();

            Assert.AreSame(item, PoolManager.Get<Item>(),
                "跨帧归还的实例应当被复用——引用同一性就是「复用」的定义");
        }

        [Test]
        public async Task IPoolable_OnRentAndOnReturn_FireInTheirRespectiveFrames()
        {
            FramedItem item = PoolManager.Get<FramedItem>();
            int rentFrame = Time.frameCount;

            await UniTask.Yield();
            int returnFrame = Time.frameCount;

            PoolManager.Return(item);

            Assert.AreEqual(1, item.RentFrames.Count, "取出时应当回调恰好一次");
            Assert.AreEqual(1, item.ReturnFrames.Count, "归还时应当回调恰好一次");
            Assert.AreEqual(rentFrame, item.RentFrames[0], "租出发生在取出的那一帧");
            Assert.AreEqual(returnFrame, item.ReturnFrames[0], "归还在后续帧发生——跨帧后回调仍成对");
            Assert.Greater(returnFrame, rentFrame, "两帧应当不同（这就是「真的跨了一帧」的证据）");
        }

        [Test]
        public async Task ClearAll_BetweenFrames_KeepsThePoolRegisteredAndUsable()
        {
            Item first = PoolManager.Get<Item>();
            PoolManager.Return(first);

            await UniTask.Yield();

            PoolManager.ClearAll();

            Assert.IsTrue(PoolManager.HasPool<Item>(), "ClearAll 清的是闲置实例，不是注册表");
            Assert.AreEqual(0, PoolManager.GetPool<Item>().CountInactive, "闲置实例应被清空");

            await UniTask.Yield();

            Item second = PoolManager.Get<Item>();
            Assert.IsNotNull(second, "ClearAll 之后池仍应可用");
            PoolManager.Return(second);
        }

        #endregion
    }
}
