using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using XFramework.Architecture.Tests;

namespace XFramework.XReactive.Tests
{
    /// <summary>
    /// 集合遍历的零装箱契约。
    /// <para>锁两件事：</para>
    /// <para>① <b>结构断言</b>——两个类型的公开 <c>GetEnumerator()</c> 返回<b>值类型</b>。
    /// 装箱的充要原因就是它返回接口（<c>IEnumerator&lt;T&gt;</c>），这条<strong>不依赖任何计时或内存尺子</strong>，
    /// 是确定性的：谁哪天把返回类型改回接口，它立刻红。</para>
    /// <para>② <b>粗尺子实测</b>——10 万次直接 <c>foreach</c> 的堆增量远低于装箱信号
    /// （改造前实测 512000 B，阈值取它的 1/4 作余量）。测量机制——为什么本环境只有
    /// <c>Profiler.GetMonoUsedSizeLong()</c> 可用、为什么必须放大到 10 万次——已抽为共享工具
    /// <see cref="AllocationRuler"/>：它是全仓唯一允许读那把尺子的地方，本 fixture 曾是其唯一实践者。</para>
    /// </summary>
    [TestFixture]
    public class ReactiveEnumerationAllocationTests
    {
        private static long _sink;

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
                AllocationRuler.AssertNoHeapGrowth("ReactiveList 直接 foreach",
                    () => { foreach (var x in list) _sink ^= x; }, 100_000, 128_000);
                AllocationRuler.AssertNoHeapGrowth("ReactiveDictionary 直接 foreach",
                    () => { foreach (var kv in dict) _sink ^= kv.Key; }, 100_000, 128_000);
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
    }
}
