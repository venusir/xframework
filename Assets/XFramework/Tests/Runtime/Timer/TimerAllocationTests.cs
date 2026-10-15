using System;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using XFramework.Architecture.Tests;
using XFramework.XUpdate;

namespace XFramework.XTimer.Tests
{
    /// <summary>
    /// 零分配承诺的可执行形式。
    /// <para><b>为什么值得单独一个 fixture</b>：「零分配的可复用句柄」是本模块立项的四条理由之一
    /// （<c>UniTask.Delay</c> 每次要一个 <c>CancellationTokenSource</c>）。而分配是<b>看不见的回归</b>——
    /// 谁哪天在扫尾里顺手写个 <c>foreach</c>、给槽位加个引用类型字段、或把状态改成 <c>object</c>，
    /// 功能用例全绿，只有这里会红。</para>
    /// <para><b>两道防线</b>：① <b>结构断言</b>——「零分配」的原因几乎都是可反射验证的结构事实
    /// （句柄是值类型且实例字段全 readonly、状态槽位按类型参数泛化而不是存 <c>object</c>、
    /// 槽位上没有登记句柄可存的位置），不依赖任何尺子；② <b>粗尺子</b>——把动作放大到
    /// 「最小坏形态」的信号 ≥ 1 MB，阈值取实测信号的 1/4（机制见 <see cref="AllocationRuler"/>）。</para>
    /// <para><b>为什么必须补第一道</b>：本环境的两把分配尺子恒返回 0（见 <see cref="AllocationRuler"/> 的
    /// 类注释），2026-10-12 之前这里只有尺子——四条 <c>Assert.AreEqual(0L, …)</c> 全是恒真的。</para>
    /// <para><b>每个用例先预热再量</b>：JIT、静态构造、表的首次分配与字典建表的开销都发生在预热段
    /// （<see cref="AllocationRuler"/> 统一做 1000 次），量的是稳态路径。委托一律用静态字段缓存——
    /// 使用方每次 new 一个闭包本来就要分配，那不是框架的账。</para>
    /// </summary>
    [TestFixture]
    public class TimerAllocationTests
    {
        #region Constants

        /// <summary>创建/释放类动作的放大次数：坏形态（每迭代一个对象 ≈ 16–32 B）信号 ≥ 1.6 MB。</summary>
        private const int AmplifiedIterations = 100_000;

        /// <summary>扫尾类动作的放大帧数：每帧一个对象的坏形态信号 ≥ 640 KB。</summary>
        private const int AmplifiedFrames = 40_000;

        /// <summary>
        /// 粗尺子阈值 = 实测坏形态信号 1/4。2026-10-12 注入校准（在动作里多加一次 <c>new object()</c>，
        /// 三处创建/释放站点全部转红）：实测 1,576,960 / 1,568,768 / 1,564,672 B（≈16 B/迭代）。
        /// 取 400 KB——是 <c>GetMonoUsedSizeLong</c> 4–8 KB 粒度的 50 倍以上，又远低于最小坏形态
        /// （噪声地板实测为 0）。
        /// </summary>
        private const long CreationThresholdBytes = 400_000;

        /// <summary>扫尾侧同上：4 万帧各注入一个对象，实测 606,208 B（≈15 B/帧），阈值取 160 KB。</summary>
        private const long SweepThresholdBytes = 160_000;

        #endregion

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

        /// <summary>第二个值类型载荷：用来复验槽位「真按类型参数泛化」而不是恰好长得像。</summary>
        private struct Payload2
        {
#pragma warning disable CS0649
            public float Weight;
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

        #region 结构断言（不依赖任何尺子）

        /// <summary>
        /// 句柄是值类型，且实例字段全是值类型 + <c>readonly</c>。
        /// <para><b>为什么这是「创建/释放不分配」的证据</b>：句柄是创建路径返回给调用方的唯一新增对象——
        /// 「类句柄」或「句柄里塞了委托/对象引用」正是会分配的那两种形态。本模块的 XML 文档自述
        /// 「不可变值类型，只存三个整型，不存委托、不存对象引用」，本用例把这句话变成可执行断言。</para>
        /// <para><b>射程</b>：拦不住 <c>Create</c> 内部的其它分配（例如把回调包一层闭包）——那由粗尺子管。</para>
        /// </summary>
        [Test]
        public void TimerHandle_IsValueTypeWithOnlyReadonlyValueFields()
        {
            var handle = typeof(TimerHandle);

            Assert.IsTrue(handle.IsValueType,
                "TimerHandle 必须是值类型——改成类即意味着每次创建都多一个堆对象");

            var fields = handle.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotEmpty(fields, "TimerHandle 一个实例字段都没有？反射没找对目标");

            foreach (var field in fields)
            {
                Assert.IsTrue(field.FieldType.IsValueType,
                    $"TimerHandle.{field.Name} 的类型是 {field.FieldType}——引用类型字段会和句柄一起分配");
                Assert.IsTrue(field.IsInitOnly,
                    $"TimerHandle.{field.Name} 不是 readonly——可变的句柄破坏了「可自由复制」的前提");
            }
        }

        /// <summary>
        /// 带状态槽位把状态存成<b>类型参数本身</b>（<c>TState</c>），而不是 <c>object</c>。
        /// <para><b>为什么这是「值类型载荷不装箱」的充要原因</b>：值类型载荷装箱的唯一途径就是被存进
        /// <c>object</c> 字段。字段声明为 <c>TState</c> 时，闭合到值类型即不可能装箱——这与
        /// <c>ReactiveEnumerationAllocationTests</c> 的「<c>GetEnumerator()</c> 返回类型即装箱的充要原因」
        /// 是同一种强度的判据。</para>
        /// <para><b>第二个载荷复验「真泛化」</b>：如果哪天有人把字段改成固定类型（或加了一层只对某些
        /// 类型走的旁路），两个载荷的类型断言不会同时成立。</para>
        /// <para><b>射程</b>：若有人另加一个 <c>object _boxedState</c> 并让值类型走它，本断言看不见——
        /// 「无 object 字段」那条腿只覆盖槽位自身，粗尺子兜底。</para>
        /// </summary>
        [Test]
        public void StatefulSlot_StoresStateAsTheGenericParameter()
        {
            var slot = typeof(TimerSlot<Payload>);
            Assert.IsTrue(slot.IsValueType, "TimerSlot<TState> 必须是结构体——槽位数组是连续内存，这是扫尾零指针追逐的前提");

            var stateField = slot.GetField("State");
            Assert.IsNotNull(stateField, "TimerSlot<TState> 找不到 State 字段——改名了？");
            Assert.AreEqual(typeof(Payload), stateField.FieldType,
                "State 字段的类型必须就是类型参数本身——存成 object 会让值类型载荷每次创建都装箱");

            var other = typeof(TimerSlot<Payload2>).GetField("State");
            Assert.IsNotNull(other, "TimerSlot<Payload2> 找不到 State 字段");
            Assert.AreEqual(typeof(Payload2), other.FieldType,
                "换一个载荷类型后 State 的字段类型应随之改变——没有改变说明它被固定成了别的类型");

            foreach (var field in slot.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                Assert.AreNotEqual(typeof(object), field.FieldType,
                    $"TimerSlot<TState>.{field.Name} 是 object——值类型载荷走它会装箱");
            }
        }

        /// <summary>
        /// 槽位只存 <c>CancellationToken</c> 本身，槽位与表上都没有 <c>CancellationTokenRegistration</c> 的容身之处。
        /// <para><b>如实说明：这是必要条件而非充分条件</b>。登记令牌后必须持有登记句柄才能注销，故
        /// 「设计上没有登记」必然表现为「没有存放登记句柄的字段」；但「<c>Register</c> 完立刻丢弃句柄」
        /// 的写法能骗过它。「不用 <c>Register</c>（它自身分配一次）」是本模块已定的设计取舍，
        /// 本条把它机械锁住，粗尺子兜底。</para>
        /// </summary>
        [Test]
        public void Slot_StoresTheTokenItself_NotARegistration()
        {
            int tokenFields = 0;

            foreach (var field in typeof(TimerSlot<Payload>).GetFields(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                Assert.IsFalse(ContainsType(field.FieldType, typeof(CancellationTokenRegistration)),
                    $"TimerSlot<Payload>.{field.Name} 的类型 {field.FieldType} 含 CancellationTokenRegistration——" +
                    "那意味着令牌登记句柄被存了下来，即创建时真的走了 Register（它自身会分配）");
                if (field.FieldType == typeof(CancellationToken))
                    tokenFields++;
            }

            Assert.AreEqual(1, tokenFields, "槽位应恰好存一个 CancellationToken 字段（default 表示不受约束）");

            foreach (var field in typeof(TimerTable<Payload>).GetFields(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                Assert.IsFalse(ContainsType(field.FieldType, typeof(CancellationTokenRegistration)),
                    $"TimerTable<Payload>.{field.Name} 的类型 {field.FieldType} 含 CancellationTokenRegistration——" +
                    "登记句柄应当只在创建路径的栈上短暂存在，不该被表持有");
            }
        }

        /// <summary>类型本身是目标、或是装了目标的数组/泛型（只看一层泛型实参，够用且不过度）。</summary>
        private static bool ContainsType(Type type, Type target)
        {
            if (type == target)
                return true;
            if (type.IsArray)
                return ContainsType(type.GetElementType(), target);
            if (type.IsGenericType)
            {
                foreach (var argument in type.GetGenericArguments())
                {
                    if (ContainsType(argument, target))
                        return true;
                }
            }

            return false;
        }

        #endregion

        #region 创建与释放（结构断言 + 粗尺子）

        /// <summary>
        /// 创建 + 释放 10 万次，堆增量必须远低于最小坏形态。
        /// <para>哨兵定时器是必要的：它让驱动器全程保持注册，否则每次把最后一条定时器释放掉都会走一遍
        /// 注册/注销，而那是调度器侧的账、不该混进这条断言。</para>
        /// </summary>
        [Test]
        public void CreateAndDispose_DoNotAllocate()
        {
            TimerManager.Every(60f, Noop); // 哨兵

            AllocationRuler.AssertNoHeapGrowth("创建/释放 10 万次",
                () => TimerManager.After(60f, Noop).Dispose(), AmplifiedIterations, CreationThresholdBytes);
        }

        /// <summary>
        /// 带状态重载用<b>值类型</b>载荷时同样零分配。
        /// <para>这条是「按 (时间轴, 状态类型) 分表」这个设计的验收条件：若状态改用一个 <c>object</c>
        /// 字段存，值类型载荷会在每次创建时装箱，这里立刻会红——而功能用例一条都不会失败。</para>
        /// </summary>
        [Test]
        public void StatefulOverload_WithStructState_DoesNotBox()
        {
            TimerManager.Every(60f, Noop); // 哨兵

            AllocationRuler.AssertNoHeapGrowth("值类型载荷创建/释放 10 万次",
                () => TimerManager.After(60f, ConsumePayload, new Payload()).Dispose(),
                AmplifiedIterations, CreationThresholdBytes);
        }

        /// <summary>
        /// 不传令牌时连令牌登记都不存在，因此这条路径与「传了令牌」是结构上不同的两条路。
        /// <para>本条锁「不传时零分配」，即 <c>CancellationToken.Register</c> 那类开销确实没有发生；
        /// <see cref="Slot_StoresTheTokenItself_NotARegistration"/> 从结构上锁同一件事。</para>
        /// </summary>
        [Test]
        public void WithoutToken_NoRegistrationHappens()
        {
            TimerManager.Every(60f, Noop);

            AllocationRuler.AssertNoHeapGrowth("默认令牌路径创建/释放 10 万次",
                () => TimerManager.After(60f, Noop, UpdateTimeMode.Scaled, default).Dispose(),
                AmplifiedIterations, CreationThresholdBytes);
        }

        #endregion

        #region 扫尾

        /// <summary>
        /// 稳态下推进 4 万帧零分配：扫尾是每帧路径，框架的 GC 约定在这里没有例外。
        /// <para><b>为什么放大到 4 万帧</b>：单帧的坏形态（每帧一个对象）在 4–8 KB 粒度的尺子下不可见，
        /// 放大后信号 ≥ 640 KB。<b>前置的定时器组不要改</b>：<c>Every(0.05f)</c> 让档位停在 Tier0，
        /// 保证每帧都真的走一遍扫尾——改了档位，放大就不再作用于每帧路径。</para>
        /// </summary>
        [Test]
        public void SweepingActiveTimers_DoesNotAllocate()
        {
            TimerManager.Every(0.05f, Noop);
            TimerManager.After(1000f, Noop); // 4 万帧 ≈ 666 秒虚拟时间，它全程待触发、不会中途兑现并释放槽位
            _driver.Advance(10); // 预热：把节拍累加器、待处理操作缓冲等都跑热

            AllocationRuler.AssertNoHeapGrowth("推进 4 万帧",
                () => _driver.Advance(1), AmplifiedFrames, SweepThresholdBytes);
        }

        #endregion
    }
}
