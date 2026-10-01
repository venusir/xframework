using System;
using NUnit.Framework;

namespace XFramework.XLog.Tests
{
    /// <summary>
    /// 「未启用不格式化」的可执行形式——本模块立项三条理由之一。
    /// <para><b>为什么它是唯一能锁住这条承诺的东西</b>：档位过滤的正确性用行为用例就能测，但「不分配」
    /// 看不见——谁哪天把模板路径改成先 <c>string.Format</c> 再判档位，功能全绿，只有这条会红。
    /// 修法很便宜（把判档位挪回格式化之前），但前提是有人告诉过他。</para>
    /// <para><b>量的是稳态路径</b>：JIT、静态构造、首次走完整条链路的分支预热都在预热段完成。
    /// 断言形态沿用 <c>TimerAllocationTests</c>：取 <c>GC.GetAllocatedBytesForCurrentThread()</c> 前后差。</para>
    /// </summary>
    [TestFixture]
    public class LogAllocationTests
    {
        #region Fixture

        [SetUp]
        public void SetUp()
        {
            LogTestKit.SetUp();
        }

        [TearDown]
        public void TearDown()
        {
            LogTestKit.TearDown();
        }

        #endregion

        #region 未启用路径

        /// <summary>模板 + 2 参：关档后 1000 次一个字节都不许分配。</summary>
        [Test]
        public void DisabledPath_TemplateOverload_DoesNotAllocate()
        {
            LogManager.SetCategoryLevel(LogCategories.Save, LogLevel.Off);

            for (int i = 0; i < 64; i++) // 预热：JIT / 静态构造 / 首次走链路
                LogManager.Warning(LogCategories.Save, "x {0} y {1}", 1, 2f);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
                LogManager.Warning(LogCategories.Save, "x {0} y {1}", 1, 2f);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0L, allocated,
                $"关档后写 1000 条分配了 {allocated} 字节——「未启用时不格式化」是本模块立项的三条理由之一");
        }

        /// <summary>现成字符串重载同理。</summary>
        [Test]
        public void DisabledPath_MessageOverload_DoesNotAllocate()
        {
            LogManager.SetCategoryLevel(LogCategories.Save, LogLevel.Off);

            for (int i = 0; i < 64; i++)
                LogManager.Warning(LogCategories.Save, "现成字符串");

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
                LogManager.Warning(LogCategories.Save, "现成字符串");
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0L, allocated, $"关档后写 1000 条分配了 {allocated} 字节");
        }

        /// <summary>通用入口（<c>Log</c>）与分级入口走同一条路，同样不许分配。</summary>
        [Test]
        public void DisabledPath_GenericEntry_DoesNotAllocate()
        {
            LogManager.MinimumLevel = LogLevel.Off;

            for (int i = 0; i < 64; i++)
                LogManager.Log(LogLevel.Error, LogCategories.Data, "{0}", i);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
                LogManager.Log(LogLevel.Error, LogCategories.Data, "{0}", i);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0L, allocated, $"全局关档后写 1000 条分配了 {allocated} 字节");
        }

        /// <summary><c>IsEnabled</c> 是「先探测再构造昂贵参数」的入口，它自己必须免费。</summary>
        [Test]
        public void IsEnabled_DoesNotAllocate()
        {
            LogManager.SetCategoryLevel(LogCategories.Save, LogLevel.Off);

            for (int i = 0; i < 64; i++)
                LogManager.IsEnabled(LogLevel.Warning, LogCategories.Save);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
                LogManager.IsEnabled(LogLevel.Warning, LogCategories.Save);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0L, allocated, $"探测 1000 次分配了 {allocated} 字节");
        }

        #endregion

        #region 启用路径的边界

        /// <summary>
        /// 参数在调用方求值——与插值一致，这是<b>刻意的</b>。本用例把这条写进契约，防止有人
        /// 「优化」成延迟求值（那会改变副作用时机）。
        /// </summary>
        [Test]
        public void DisabledPath_StillEvaluatesArguments()
        {
            LogManager.SetCategoryLevel(LogCategories.Save, LogLevel.Off);

            int callCount = 0;
            LogManager.Warning(LogCategories.Save, "{0}", Count());
            Assert.AreEqual(1, callCount, "关档只免掉格式化，不该免掉参数求值");

            int Count()
            {
                callCount++;
                return callCount;
            }
        }

        #endregion
    }
}
