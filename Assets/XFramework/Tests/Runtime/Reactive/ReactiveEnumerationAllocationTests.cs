using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.Profiling;

namespace XFramework.XReactive.Tests
{
    /// <summary>
    /// 集合遍历的零装箱契约。
    /// <para>锁两件事：</para>
    /// <para>① <b>结构断言</b>——两个类型的公开 <c>GetEnumerator()</c> 返回<b>值类型</b>。
    /// 装箱的充要原因就是它返回接口（<c>IEnumerator&lt;T&gt;</c>），这条<strong>不依赖任何计时或内存尺子</strong>，
    /// 是确定性的：谁哪天把返回类型改回接口，它立刻红。</para>
    /// <para>② <b>粗尺子实测</b>——10 万次直接 <c>foreach</c> 的堆增量远低于装箱信号。</para>
    /// <para><b>为什么必须用粗尺子、为什么必须放大到 10 万次</b>：本环境下
    /// <c>GC.GetAllocatedBytesForCurrentThread()</c> 与 <c>Profiler.GetTotalAllocatedMemoryLong()</c>
    /// <b>都恒返回 0</b>（2026-10-11 实测：分配 1 MB 数组后增量仍为 0，而同一次
    /// <c>Profiler.GetMonoUsedSizeLong()</c> 测得 1003520 B），唯一可用的是后者，其粒度约 4–8 KB——
    /// 单次装箱（约 32–40 B）根本看不见，故必须放大。</para>
    /// </summary>
    [TestFixture]
    public class ReactiveEnumerationAllocationTests
    {
        private static long _sink;

        private const int Iters = 100_000;

        /// <summary>改造前实测的装箱信号量级（两个类型各 512000 B）；阈值取它的 1/4 作余量。</summary>
        private const long BoxingSignalFloor = 128_000;

        [Test]
        public void GetEnumerator_ReturnsValueType_NoBoxing()
        {
            Assert.IsTrue(GetEnumeratorReturnType(typeof(ReactiveList<int>)).IsValueType,
                "ReactiveList<T>.GetEnumerator() 必须返回值类型（结构体枚举器）——返回 IEnumerator<T> 即意味着每次 foreach 装箱");

            Assert.IsTrue(GetEnumeratorReturnType(typeof(ReactiveDictionary<int, int>)).IsValueType,
                "ReactiveDictionary<TKey,TValue>.GetEnumerator() 同上");
        }

        [Test]
        public void DirectForeach_DoesNotAllocate()
        {
            var list = new ReactiveList<int>(4);
            list.Add(1);
            list.Add(2);
            var dict = new ReactiveDictionary<int, int>(4);
            dict[1] = 1;
            dict[2] = 2;
            try
            {
                AssertNoHeapGrowth("ReactiveList 直接 foreach",
                    () => { foreach (var x in list) _sink ^= x; });
                AssertNoHeapGrowth("ReactiveDictionary 直接 foreach",
                    () => { foreach (var kv in dict) _sink ^= kv.Key; });
            }
            finally
            {
                list.Dispose();
                dict.Dispose();
            }
        }

        private static Type GetEnumeratorReturnType(Type type)
        {
            var method = type.GetMethod("GetEnumerator", Type.EmptyTypes);
            Assert.IsNotNull(method, $"{type.Name} 找不到公开的无参 GetEnumerator()——它被改名或改成显式实现了？");
            return method.ReturnType;
        }

        private static void AssertNoHeapGrowth(string label, Action action)
        {
            for (int i = 0; i < 1000; i++)
                action();   // 预热:JIT 与首次分配挤出测量窗口

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var before = Profiler.GetMonoUsedSizeLong();
            for (int i = 0; i < Iters; i++)
                action();
            var after = Profiler.GetMonoUsedSizeLong();

            Assert.Less(after - before, BoxingSignalFloor,
                $"{label}:10 万次堆增量应远低于装箱信号(改造前实测 512000 B)," +
                $"实测 {after - before} B——涨回来即说明装箱又被引入");
        }
    }
}
