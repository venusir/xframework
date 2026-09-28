using System;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XLock;
using XFramework.XMessage;

namespace XFramework.XLock.Tests
{
    /// <summary>MonoBehaviour 主体替身（顶层类：Unity 要求组件类型可被 AddComponent 识别）。</summary>
    public sealed class LockDestroyMonoSubject : MonoBehaviour, ILockable { }

    /// <summary>
    /// 主体销毁时的清理：自动释放（销毁令牌）+ 显式批量（RemoveAllLocks/RemoveAllSubscriptions）。
    /// <para><b>为什么需要它</b>：<c>_locks</c> 与两张订阅表都以 <see cref="ILockable"/> 作**强引用键**，
    /// 而实体死亡/切场景/回池时若没走完释放路径，锁会永久留存（该主体再也解不开）**且该实体被强引用住
    /// 无法回收**——实测两次 GC 后 WeakReference 仍存活。原子地「删掉键」需要一个「键什么时候死」的
    /// 信号，这就是销毁令牌。</para>
    /// <para><b>绑定的是主体的令牌</b>（不是订阅者的）：泄漏的是字典的键，键就是主体。跨对象订阅
    /// （A 订阅 B 的锁事件、A 先死）不在覆盖范围内，见 README 的「已知限制」。</para>
    /// <para><b>注册粒度是「每主体一次」</b>：<c>AddLock</c> 走的是加锁边沿，主体是长寿命对象时逐次注册
    /// 会把回调节点堆在它的令牌上永不回收——那是本模块要消灭的那类无界增长。主体空闲（锁与订阅全清）
    /// 时注销。</para>
    /// </summary>
    [TestFixture]
    public class LockDestroyBindingTests
    {
        #region Test Doubles

        /// <summary>普通 C# 对象 + 销毁令牌：非 MonoBehaviour 的正规路径。</summary>
        private sealed class TokenSubject : ILockable, IDestroyCancellationToken
        {
            private readonly CancellationTokenSource _cts = new CancellationTokenSource();

            public CancellationToken DestroyCancellationToken => _cts.Token;

            public void Destroy() => _cts.Cancel();

            public void DisposeCts() => _cts.Dispose();
        }

        /// <summary>既不是 MonoBehaviour 也不实现 IDestroyCancellationToken——不会被自动释放。</summary>
        private sealed class PlainSubject : ILockable { }

        private const int TypeA = 0;
        private const int TypeB = 1;

        private TokenSubject _subject;

        #endregion

        #region Setup / Teardown

        [SetUp]
        public void SetUp()
        {
            LockManager.Dispose();
            _subject = new TokenSubject();
        }

        [TearDown]
        public void TearDown()
        {
            LockManager.Dispose();
            _subject.DisposeCts();
        }

        #endregion

        #region 自动释放

        [Test]
        public void Destroy_ReleasesLocks()
        {
            var handleA = LockManager.AddLock(_subject, TypeA, new object());
            LockManager.AddLock(_subject, TypeB, new object());
            Assert.IsTrue(LockManager.IsLocked(_subject, TypeA), "前置：锁已加");

            _subject.Destroy();

            Assert.IsFalse(LockManager.IsLocked(_subject, TypeA), "销毁应释放该主体的锁");
            Assert.IsFalse(LockManager.IsLocked(_subject, TypeB));
            Assert.IsFalse(handleA.IsHeld, "逐句柄查询同样归零");
        }

        [Test]
        public void Destroy_DropsSubscriptions()
        {
            int count = 0;
            LockManager.OnLocked(_subject, _ => count++);
            LockManager.OnLockStateChanged(_subject, (_, _) => count++);

            _subject.Destroy();

            LockManager.AddLock(LockManager.Global, TypeA, new object());
            Assert.AreEqual(0, count, "销毁后两条订阅都不该再被调用");
        }

        /// <summary>释放顺序：先丢订阅、再放锁——濒死对象自己的回调不该再跑，总线订阅者照常收到。</summary>
        [Test]
        public void Destroy_DropsOwnSubscriptionBeforeReleasingLocks()
        {
            int own = 0;
            int bus = 0;
            LockManager.OnUnlocked(_subject, _ => own++);
            LockManager.OnGlobalUnlocked += (_, _, _) => bus++;

            LockManager.AddLock(_subject, TypeA, new object());

            _subject.Destroy();

            Assert.AreEqual(0, own, "销毁过程中不该回调濒死对象自己的订阅者");
            Assert.AreEqual(1, bus, "总线订阅者与主体无关,照常收到解锁");
        }

        [Test]
        public void PlainObject_IsNotAutoReleased()
        {
            var plain = new PlainSubject();
            LockManager.AddLock(plain, TypeA, new object());

            // 既非 MonoBehaviour 也不实现 IDestroyCancellationToken：不绑定、也不告警
            Assert.IsTrue(LockManager.IsLocked(plain, TypeA), "没有被自动释放——这是已知限制,需显式 RemoveAllLocks");
        }

        [Test]
        public void AutoReleaseDisabled_KeepsLocks()
        {
            LockManager.AutoReleaseOnDestroy = false;
            LockManager.AddLock(_subject, TypeA, new object());

            _subject.Destroy();

            Assert.IsTrue(LockManager.IsLocked(_subject, TypeA), "关掉开关后框架不碰这些锁");
        }

        [Test]
        public void MonoSubject_Destroyed_ReleasesLocks()
        {
            var go = new GameObject("LockDestroyBindingTests");
            var mono = go.AddComponent<LockDestroyMonoSubject>();
            LockManager.AddLock(mono, TypeA, new object());
            Assert.IsTrue(LockManager.IsLocked(mono, TypeA), "前置：锁已加");

            UnityEngine.Object.DestroyImmediate(go);

            Assert.IsFalse(LockManager.IsLocked(mono, TypeA), "MonoBehaviour 销毁应释放它的锁");
        }

        [Test]
        public void AlreadyDestroyed_AddLock_IsIgnored()
        {
            _subject.Destroy();

            LogAssert.Expect(LogType.Warning, new Regex(@"\[Lock\] AddLock ignored"));
            var handle = LockManager.AddLock(_subject, TypeA, new object());

            Assert.IsFalse(handle.IsHeld, "已销毁的主体拿不到锁——短路而不是「加上再被回调清掉」");
            Assert.AreEqual(0, LockManager.GetLockCount(_subject, TypeA));
            Assert.DoesNotThrow(() => handle.Dispose());
        }

        [Test]
        public void AlreadyDestroyed_Subscribe_IsIgnored()
        {
            _subject.Destroy();

            LogAssert.Expect(LogType.Warning, new Regex(@"\[Lock\] subscription ignored"));
            int count = 0;
            var sub = LockManager.OnLocked(_subject, _ => count++);

            // 用全局锁来验：全局锁会通知所有主体订阅者，若刚才那条订阅其实挂上了,这里就会响
            LockManager.AddLock(LockManager.Global, TypeA, new object());
            Assert.AreEqual(0, count);
            Assert.DoesNotThrow(() => sub.Dispose());
        }

        /// <summary>聚合订阅落在已销毁主体上：只需**一条**告警，且返回的句柄可安全释放。</summary>
        [Test]
        public void AlreadyDestroyed_StateSubscription_WarnsExactlyOnce()
        {
            _subject.Destroy();

            int warnings = 0;
            Application.LogCallback counter = (condition, _, type) =>
            {
                if (type == LogType.Warning && condition.Contains("[Lock] subscription ignored"))
                    warnings++;
            };

            Application.logMessageReceived += counter;
            IDisposable sub;
            try
            {
                sub = LockManager.OnLockStateChanged(_subject, (_, _) => { });
            }
            finally
            {
                Application.logMessageReceived -= counter;
            }

            // 改前：它经两条内部订阅各撞一次短路，于是告警两次
            Assert.AreEqual(1, warnings, "聚合订阅只需一条告警");
            Assert.DoesNotThrow(() => sub.Dispose());
        }

        #endregion

        #region 显式批量

        [Test]
        public void RemoveAllLocks_ReleasesAllTypes_AndReportsCount()
        {
            LockManager.AddLock(_subject, TypeA, new object());
            LockManager.AddLock(_subject, TypeA, new object());
            LockManager.AddLock(_subject, TypeB, new object());

            int removed = LockManager.RemoveAllLocks(_subject);

            Assert.AreEqual(3, removed, "返回释放总数");
            Assert.IsFalse(LockManager.IsLocked(_subject, TypeA));
            Assert.IsFalse(LockManager.IsLocked(_subject, TypeB));
        }

        /// <summary>批量清理仍走解锁通知：每个类型一次边沿（载荷里的 lockObj 为 null，因为整批摘除）。</summary>
        [Test]
        public void RemoveAllLocks_NotifiesOncePerType()
        {
            int unlocked = 0;
            LockManager.OnUnlocked(_subject, _ => unlocked++);

            LockManager.AddLock(_subject, TypeA, new object());
            LockManager.AddLock(_subject, TypeB, new object());
            LockManager.RemoveAllLocks(_subject);

            Assert.AreEqual(2, unlocked, "两个类型各一次边沿（不是每把锁一次）");
        }

        [Test]
        public void RemoveAllLocks_Global_ClearsGlobalLocks()
        {
            LockManager.AddLock(LockManager.Global, TypeA, new object());

            int removed = LockManager.RemoveAllLocks(LockManager.Global);

            Assert.AreEqual(1, removed);
            Assert.IsFalse(LockManager.IsLocked(LockManager.Global, TypeA));
        }

        [Test]
        public void RemoveAllSubscriptions_StopsCallbacks_AndStaleHandleDisposeIsSafe()
        {
            int count = 0;
            var sub = LockManager.OnLocked(_subject, _ => count++);

            LockManager.RemoveAllSubscriptions(_subject);

            LockManager.AddLock(_subject, TypeA, new object());
            Assert.AreEqual(0, count, "订阅已被丢弃");

            Assert.DoesNotThrow(() => sub.Dispose(), "陈旧句柄再释放必须安全（键已不在表里）");
        }

        [Test]
        public void RemoveAllSubscriptions_DoesNotTouchLocks()
        {
            LockManager.OnLocked(_subject, _ => { });
            LockManager.AddLock(_subject, TypeA, new object());

            LockManager.RemoveAllSubscriptions(_subject);

            Assert.IsTrue(LockManager.IsLocked(_subject, TypeA), "只丢订阅,不动锁");
        }

        #endregion
    }
}
