using System;
using System.Collections.Generic;
using NUnit.Framework;
using XFramework.XPool;

namespace XFramework.XPool.Tests
{
    /// <summary>
    /// <see cref="PoolManager.CopyPoolStats"/> / <see cref="PoolManager.PoolCount"/>：池规模投影。
    /// <para>隔离手法与 <c>PoolManagerTests</c> 一致：本 fixture 用独占类型作键，TearDown 清闲置
    /// （PoolManager 是静态全局状态）。</para>
    /// </summary>
    [TestFixture]
    public class PoolDiagnosticsTests
    {
        private sealed class DiagnosticsItem
        {
        }

        [TearDown]
        public void TearDown()
        {
            PoolManager.ClearAll();
        }

        private static PoolStats Find(List<PoolStats> buffer, Type type)
        {
            for (int i = 0; i < buffer.Count; i++)
            {
                if (buffer[i].ObjectType == type)
                    return buffer[i];
            }

            Assert.Fail($"缓冲区里没有类型 '{type.Name}' 的池");
            return default;
        }

        [Test]
        public void CopyPoolStats_ReportsInactiveActiveAndTotal()
        {
            var first = PoolManager.Get<DiagnosticsItem>();
            PoolManager.Get<DiagnosticsItem>();
            PoolManager.Return(first);

            var buffer = new List<PoolStats>();
            int count = PoolManager.CopyPoolStats(buffer);

            Assert.AreEqual(buffer.Count, count);
            var stats = Find(buffer, typeof(DiagnosticsItem));
            Assert.AreEqual(1, stats.CountInactive);
            Assert.AreEqual(1, stats.CountActive);
            Assert.AreEqual(2, stats.CountAll);
        }

        [Test]
        public void CopyPoolStats_ClearsBufferFirst()
        {
            var buffer = new List<PoolStats> { new PoolStats(typeof(string), 9, 9, 9) };

            PoolManager.CopyPoolStats(buffer);

            for (int i = 0; i < buffer.Count; i++)
                Assert.AreNotEqual(typeof(string), buffer[i].ObjectType);
        }

        [Test]
        public void CopyPoolStats_NullBuffer_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => PoolManager.CopyPoolStats(null));
        }

        [Test]
        public void PoolCount_ReflectsCreatedPools()
        {
            PoolManager.Get<DiagnosticsItem>();

            Assert.GreaterOrEqual(PoolManager.PoolCount, 1);
        }

        [Test]
        public void PoolStats_ToString_MentionsType()
        {
            var stats = new PoolStats(typeof(DiagnosticsItem), 1, 2, 3);

            StringAssert.Contains(nameof(DiagnosticsItem), stats.ToString());
        }
    }
}
