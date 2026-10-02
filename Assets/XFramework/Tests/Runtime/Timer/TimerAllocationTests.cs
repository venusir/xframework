using System;
using NUnit.Framework;
using UnityEngine;
using XFramework.XUpdate;

namespace XFramework.XTimer.Tests
{
    /// <summary>
    /// 零分配承诺的可执行形式。
    /// <para><b>为什么值得单独一个 fixture</b>：「零分配的可复用句柄」是本模块立项的四条理由之一
    /// （<c>UniTask.Delay</c> 每次要一个 <c>CancellationTokenSource</c>）。而分配是<b>看不见的回归</b>——
    /// 谁哪天在扫尾里顺手写个 <c>foreach</c>、给槽位加个引用类型字段、或把状态改成 <c>object</c>，
    /// 功能用例全绿，只有这条会红。</para>
    /// <para><b>每个用例先预热再量</b>：JIT、静态构造、表的首次分配与字典建表的开销都发生在预热段，
    /// 量的是稳态路径。委托一律用静态字段缓存——使用方每次 new 一个闭包本来就要分配，那不是框架的账。</para>
    /// </summary>
    [TestFixture]
    public class TimerAllocationTests
    {
        #region Test Doubles

        /// <summary>值类型载荷：它的意义就是「别被装箱」。</summary>
        private struct Payload
        {
            // ReSharper disable once NotAccessedField.Local —— 字段只用来撑出「这是个值类型」的事实。
            // 有意不赋值；结构体不支持字段初始化器（CS8773，本仓 LangVersion 9.0），故用 pragma 消 CS0649
#pragma warning disable CS0649
            public int Value;
#pragma warning restore CS0649
        }

        #endregion

        #region Fixture

        private static readonly Action Noop = () => { };

        private static readonly Action<Payload> ConsumePayload = static _ => { };

        private float _originalTimeScale;
        private TimerDriver _driver;

        [SetUp]
        public void SetUp()
        {
            _originalTimeScale = Time.timeScale;
            TimerTestKit.SetUp();
            _driver = new TimerDriver();
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = _originalTimeScale;
            TimerTestKit.TearDown();
        }

        #endregion

        #region 创建与释放

        /// <summary>
        /// 创建 + 释放 1000 次，一个字节都不许分配。
        /// <para>哨兵定时器是必要的：它让驱动器全程保持注册，否则每次把最后一条定时器释放掉都会走一遍
        /// 注册/注销，而那是调度器侧的账、不该混进这条断言。</para>
        /// </summary>
        [Test]
        public void CreateAndDispose_DoNotAllocate()
        {
            TimerManager.Every(60f, Noop); // 哨兵

            for (int i = 0; i < 64; i++)
                TimerManager.After(60f, Noop).Dispose();

            long before = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < 1000; i++)
                TimerManager.After(60f, Noop).Dispose();

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0L, allocated,
                $"创建/释放 1000 次分配了 {allocated} 字节——零分配的可复用句柄是立项理由之一");
        }

        /// <summary>
        /// 带状态重载用<b>值类型</b>载荷时同样零分配。
        /// <para>这条是「按 (时间轴, 状态类型) 分表」这个设计的验收条件：若状态改用一个 <c>object</c> 字段存，
        /// 值类型载荷会在每次创建时装箱，这里立刻会红——而功能用例一条都不会失败。</para>
        /// </summary>
        [Test]
        public void StatefulOverload_WithStructState_DoesNotBox()
        {
            TimerManager.Every(60f, Noop); // 哨兵

            for (int i = 0; i < 64; i++)
                TimerManager.After(60f, ConsumePayload, new Payload()).Dispose();

            long before = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < 1000; i++)
                TimerManager.After(60f, ConsumePayload, new Payload()).Dispose();

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0L, allocated,
                $"值类型载荷创建/释放 1000 次分配了 {allocated} 字节——状态被装箱了？");
        }

        /// <summary>
        /// 不传令牌时连令牌登记都不存在，因此这条路径与「传了令牌」是结构上不同的两条路。
        /// <para>本条只锁「不传时零分配」，即 <c>CancellationToken.Register</c> 那类开销确实没有发生。</para>
        /// </summary>
        [Test]
        public void WithoutToken_NoRegistrationHappens()
        {
            TimerManager.Every(60f, Noop);

            for (int i = 0; i < 64; i++)
                TimerManager.After(60f, Noop).Dispose();

            long before = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < 1000; i++)
                TimerManager.After(60f, Noop, UpdateTimeMode.Scaled, default).Dispose();

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0L, allocated, $"默认令牌路径分配了 {allocated} 字节");
        }

        #endregion

        #region 扫尾

        /// <summary>
        /// 稳态下推进 60 帧零分配：扫尾是每帧路径，框架的 GC 约定在这里没有例外。
        /// </summary>
        [Test]
        public void SweepingActiveTimers_DoesNotAllocate()
        {
            TimerManager.Every(0.05f, Noop);
            TimerManager.After(60f, Noop);
            _driver.Advance(10); // 预热：把节拍累加器、待处理操作缓冲等都跑热

            long before = GC.GetAllocatedBytesForCurrentThread();

            _driver.Advance(60);

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0L, allocated,
                $"推进 60 帧分配了 {allocated} 字节——每帧路径不得有闭包、装箱或字符串拼接");
        }

        #endregion
    }
}
