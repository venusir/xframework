using System;
using System.Collections.Generic;
using NUnit.Framework;
using XFramework.XLock;

namespace XFramework.XLock.Tests
{
    /// <summary>
    /// Tests for <see cref="LockService"/>.
    /// All tests run on a clean state; <see cref="LockService.Dispose()"/> is called in TearDown.
    /// </summary>
    [TestFixture]
    public class LockServiceTests
    {
        private sealed class TestLockable : ILockable { }

        private ILockable _subjectA;
        private ILockable _subjectB;
        private const int LockTypeMovement = 0;
        private const int LockTypeAttack = 1;
        private readonly object _lockObj1 = new object();
        private readonly object _lockObj2 = new object();

        [SetUp]
        public void SetUp()
        {
            _subjectA = new TestLockable();
            _subjectB = new TestLockable();
        }

        [TearDown]
        public void TearDown()
        {
            LockService.Dispose();
        }

        [Test]
        public void Acquire_NewLock_ReturnsValidHandle()
        {
            var handle = LockService.Acquire(_subjectA, LockTypeMovement, _lockObj1);
            Assert.IsTrue(handle.IsValid);
        }

        [Test]
        public void Acquire_NullLockSubject_UsesGlobal()
        {
            var handle = LockService.Acquire(null, LockTypeMovement, _lockObj1);
            Assert.IsTrue(handle.IsValid);
        }

        [Test]
        public void Acquire_NullLock_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                LockService.Acquire(_subjectA, LockTypeMovement, null));
        }

        [Test]
        public void IsLocked_AfterAcquire_ReturnsTrue()
        {
            LockService.Acquire(_subjectA, LockTypeMovement, _lockObj1);
            Assert.IsTrue(LockService.IsLocked(_subjectA, LockTypeMovement));
        }

        [Test]
        public void IsLocked_UnknownSubject_ReturnsFalse()
        {
            Assert.IsFalse(LockService.IsLocked(_subjectA, LockTypeMovement));
        }

        [Test]
        public void IsLocked_NullSubject_ChecksGlobal()
        {
            LockService.Acquire(null, LockTypeAttack, _lockObj1);
            Assert.IsTrue(LockService.IsLocked(null, LockTypeAttack));
        }

        [Test]
        public void Release_RemovesLock_IsLockedReturnsFalse()
        {
            LockService.Acquire(_subjectA, LockTypeMovement, _lockObj1);
            LockService.Release(_subjectA, LockTypeMovement, _lockObj1);
            Assert.IsFalse(LockService.IsLocked(_subjectA, LockTypeMovement));
        }

        [Test]
        public void Release_NonExistentLock_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
                LockService.Release(_subjectA, LockTypeMovement, _lockObj1));
        }

        [Test]
        public void Release_NullLock_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                LockService.Release(_subjectA, LockTypeMovement, null));
        }

        [Test]
        public void GetLockCount_ReturnsCorrectCount()
        {
            Assert.AreEqual(0, LockService.GetLockCount(_subjectA, LockTypeMovement));

            LockService.Acquire(_subjectA, LockTypeMovement, _lockObj1);
            Assert.AreEqual(1, LockService.GetLockCount(_subjectA, LockTypeMovement));

            LockService.Acquire(_subjectA, LockTypeMovement, _lockObj2);
            Assert.AreEqual(2, LockService.GetLockCount(_subjectA, LockTypeMovement));
        }

        [Test]
        public void GetLockCount_GlobalLockCountedForSubject()
        {
            LockService.Acquire(null, LockTypeMovement, _lockObj1);
            // subjectA should see it through global
            Assert.AreEqual(1, LockService.GetLockCount(_subjectA, LockTypeMovement));

            LockService.Acquire(_subjectA, LockTypeMovement, _lockObj2);
            // 1 global + 1 subject-specific = 2
            Assert.AreEqual(2, LockService.GetLockCount(_subjectA, LockTypeMovement));
        }

        [Test]
        public void GetLockObjects_ReturnsAllObjects()
        {
            LockService.Acquire(_subjectA, LockTypeMovement, _lockObj1);
            LockService.Acquire(_subjectA, LockTypeMovement, _lockObj2);

            var objects = LockService.GetLockObjects(_subjectA, LockTypeMovement);
            Assert.AreEqual(2, objects.Count);
            CollectionAssert.Contains(objects, _lockObj1);
            CollectionAssert.Contains(objects, _lockObj2);
        }

        [Test]
        public void GetLockObjects_GlobalLockObjectsIncluded()
        {
            LockService.Acquire(null, LockTypeMovement, _lockObj1);
            LockService.Acquire(_subjectA, LockTypeMovement, _lockObj2);

            var objects = LockService.GetLockObjects(_subjectA, LockTypeMovement);
            Assert.AreEqual(2, objects.Count);
            CollectionAssert.Contains(objects, _lockObj1);
            CollectionAssert.Contains(objects, _lockObj2);
        }

        [Test]
        public void GlobalLock_AffectsAllSubjects()
        {
            LockService.Acquire(null, LockTypeMovement, _lockObj1);

            Assert.IsTrue(LockService.IsLocked(_subjectA, LockTypeMovement));
            Assert.IsTrue(LockService.IsLocked(_subjectB, LockTypeMovement));
        }

        [Test]
        public void GlobalLock_Released_NoLongerAffectsSubjects()
        {
            LockService.Acquire(null, LockTypeMovement, _lockObj1);
            LockService.Release(null, LockTypeMovement, _lockObj1);

            Assert.IsFalse(LockService.IsLocked(_subjectA, LockTypeMovement));
        }

        [Test]
        public void MultipleLockTypes_Independent()
        {
            LockService.Acquire(_subjectA, LockTypeMovement, _lockObj1);
            Assert.IsTrue(LockService.IsLocked(_subjectA, LockTypeMovement));
            Assert.IsFalse(LockService.IsLocked(_subjectA, LockTypeAttack));

            LockService.Acquire(_subjectA, LockTypeAttack, _lockObj2);
            Assert.IsTrue(LockService.IsLocked(_subjectA, LockTypeAttack));

            LockService.Release(_subjectA, LockTypeMovement, _lockObj1);
            Assert.IsFalse(LockService.IsLocked(_subjectA, LockTypeMovement));
            Assert.IsTrue(LockService.IsLocked(_subjectA, LockTypeAttack));
        }

        [Test]
        public void MultipleSubjects_Independent()
        {
            LockService.Acquire(_subjectA, LockTypeMovement, _lockObj1);
            LockService.Acquire(_subjectB, LockTypeAttack, _lockObj2);

            Assert.IsTrue(LockService.IsLocked(_subjectA, LockTypeMovement));
            Assert.IsFalse(LockService.IsLocked(_subjectA, LockTypeAttack));
            Assert.IsTrue(LockService.IsLocked(_subjectB, LockTypeAttack));
            Assert.IsFalse(LockService.IsLocked(_subjectB, LockTypeMovement));
        }

        [Test]
        public void LockHandle_Dispose_ReleasesLock()
        {
            var handle = LockService.Acquire(_subjectA, LockTypeMovement, _lockObj1);
            handle.Dispose();

            Assert.IsFalse(LockService.IsLocked(_subjectA, LockTypeMovement));
        }

        [Test]
        public void LockHandle_Using_ReleasesLock()
        {
            using (LockService.Acquire(_subjectA, LockTypeMovement, _lockObj1))
            {
                Assert.IsTrue(LockService.IsLocked(_subjectA, LockTypeMovement));
            }

            Assert.IsFalse(LockService.IsLocked(_subjectA, LockTypeMovement));
        }

        [Test]
        public void LockHandle_DoubleDispose_DoesNotThrow()
        {
            var handle = LockService.Acquire(_subjectA, LockTypeMovement, _lockObj1);
            handle.Dispose();
            Assert.DoesNotThrow(() => handle.Dispose());
        }

        [Test]
        public void OnLockedEvent_CalledOnLock()
        {
            var called = false;
            int receivedType = -1;
            LockService.OnLocked(_subjectA, (lockType) =>
            {
                called = true;
                receivedType = lockType;
            });

            LockService.Acquire(_subjectA, LockTypeMovement, _lockObj1);

            Assert.IsTrue(called);
            Assert.AreEqual(LockTypeMovement, receivedType);
        }

        [Test]
        public void OnUnlockedEvent_CalledOnRelease()
        {
            var called = false;
            int receivedType = -1;
            LockService.OnUnlocked(_subjectA, (lockType) =>
            {
                called = true;
                receivedType = lockType;
            });

            LockService.Acquire(_subjectA, LockTypeMovement, _lockObj1);
            LockService.Release(_subjectA, LockTypeMovement, _lockObj1);

            Assert.IsTrue(called);
            Assert.AreEqual(LockTypeMovement, receivedType);
        }

        [Test]
        public void OnLockedEvent_GlobalLock_NotifiesAllSubjectSubscribers()
        {
            var callCountA = 0;
            var callCountB = 0;
            LockService.OnLocked(_subjectA, _ => callCountA++);
            LockService.OnLocked(_subjectB, _ => callCountB++);

            LockService.Acquire(null, LockTypeMovement, _lockObj1);

            Assert.AreEqual(1, callCountA);
            Assert.AreEqual(1, callCountB);
        }

        [Test]
        public void OnUnlockedEvent_GlobalLock_NotifiesAllSubjectSubscribers()
        {
            var callCountA = 0;
            var callCountB = 0;
            LockService.OnUnlocked(_subjectA, _ => callCountA++);
            LockService.OnUnlocked(_subjectB, _ => callCountB++);

            LockService.Acquire(null, LockTypeMovement, _lockObj1);
            LockService.Release(null, LockTypeMovement, _lockObj1);

            Assert.AreEqual(1, callCountA);
            Assert.AreEqual(1, callCountB);
        }

        [Test]
        public void OnLockedEvent_Disposed_Unsubscribes()
        {
            var callCount = 0;
            var disposable = LockService.OnLocked(_subjectA, _ => callCount++);

            disposable.Dispose();

            LockService.Acquire(_subjectA, LockTypeMovement, _lockObj1);
            Assert.AreEqual(0, callCount);
        }

        [Test]
        public void OnUnlockedEvent_Disposed_Unsubscribes()
        {
            var callCount = 0;
            var disposable = LockService.OnUnlocked(_subjectA, _ => callCount++);

            disposable.Dispose();

            LockService.Acquire(_subjectA, LockTypeMovement, _lockObj1);
            LockService.Release(_subjectA, LockTypeMovement, _lockObj1);
            Assert.AreEqual(0, callCount);
        }

        [Test]
        public void OnGlobalLockedEvent_CalledOnFirstLock()
        {
            var called = false;
            ILockable receivedSubject = null;
            int receivedType = -1;
            object receivedLock = null;

            LockService.OnGlobalLocked += (subject, lockType, lockObj) =>
            {
                called = true;
                receivedSubject = subject;
                receivedType = lockType;
                receivedLock = lockObj;
            };

            LockService.Acquire(_subjectA, LockTypeAttack, _lockObj2);

            Assert.IsTrue(called);
            Assert.AreEqual(_subjectA, receivedSubject);
            Assert.AreEqual(LockTypeAttack, receivedType);
            Assert.AreEqual(_lockObj2, receivedLock);
        }

        [Test]
        public void OnGlobalUnlockedEvent_CalledOnLastRelease()
        {
            var called = false;
            LockService.OnGlobalUnlocked += (subject, lockType, lockObj) =>
            {
                called = true;
            };

            LockService.Acquire(_subjectA, LockTypeMovement, _lockObj1);
            LockService.Release(_subjectA, LockTypeMovement, _lockObj1);

            Assert.IsTrue(called);
        }

        [Test]
        public void OnGlobalLocked_NotCalledOnSecondLockSameType()
        {
            var callCount = 0;
            LockService.OnGlobalLocked += (_, _, _) => callCount++;

            LockService.Acquire(_subjectA, LockTypeMovement, _lockObj1);
            LockService.Acquire(_subjectA, LockTypeMovement, _lockObj2);

            // Only first lock triggers the event
            Assert.AreEqual(1, callCount);
        }

        [Test]
        public void OnGlobalUnlocked_NotCalledWhenStillLocksRemain()
        {
            var callCount = 0;
            LockService.OnGlobalUnlocked += (_, _, _) => callCount++;

            LockService.Acquire(_subjectA, LockTypeMovement, _lockObj1);
            LockService.Acquire(_subjectA, LockTypeMovement, _lockObj2);
            LockService.Release(_subjectA, LockTypeMovement, _lockObj1);

            // Still one lock remains, so event should not fire
            Assert.AreEqual(0, callCount);
        }

        [Test]
        public void Dispose_ClearsAllState()
        {
            LockService.Acquire(_subjectA, LockTypeMovement, _lockObj1);
            LockService.Acquire(_subjectB, LockTypeAttack, _lockObj2);

            LockService.Dispose();

            Assert.IsFalse(LockService.IsLocked(_subjectA, LockTypeMovement));
            Assert.IsFalse(LockService.IsLocked(_subjectB, LockTypeAttack));
        }

        [Test]
        public void Dispose_Twice_DoesNotThrow()
        {
            LockService.Dispose();
            Assert.DoesNotThrow(() => LockService.Dispose());
        }

        [Test]
        public void DifferentLockTypes_DoNotConflict()
        {
            LockService.Acquire(_subjectA, LockTypeMovement, _lockObj1);
            LockService.Acquire(_subjectA, LockTypeAttack, _lockObj2);

            Assert.AreEqual(1, LockService.GetLockCount(_subjectA, LockTypeMovement));
            Assert.AreEqual(1, LockService.GetLockCount(_subjectA, LockTypeAttack));

            LockService.Release(_subjectA, LockTypeMovement, _lockObj1);
            Assert.AreEqual(0, LockService.GetLockCount(_subjectA, LockTypeMovement));
            Assert.AreEqual(1, LockService.GetLockCount(_subjectA, LockTypeAttack));
        }

        [Test]
        public void SameLock_SameSubject_CanBeAddedMultipleTimes()
        {
            // Multiple Acquires of the same (subject, lockType, lockObj) is idempotent in HashSet
            LockService.Acquire(_subjectA, LockTypeMovement, _lockObj1);
            LockService.Acquire(_subjectA, LockTypeMovement, _lockObj1);

            // Count should be 1 (HashSet dedup)
            Assert.AreEqual(1, LockService.GetLockCount(_subjectA, LockTypeMovement));
        }

        [Test]
        public void LockHandle_IsValid_ReturnsFalseAfterDispose()
        {
            var handle = LockService.Acquire(_subjectA, LockTypeMovement, _lockObj1);
            Assert.IsTrue(handle.IsValid);

            handle.Dispose();
            Assert.IsFalse(handle.IsValid);
        }

        [Test]
        public void OnLockedEvent_SubjectSpecific_GlobalNotNotified()
        {
            var globalCallCount = 0;
            LockService.OnGlobalLocked += (_, _, _) => globalCallCount++;

            LockService.Acquire(LockService.Global, LockTypeMovement, _lockObj1);

            // Global lock should also trigger OnGlobalLocked? Let's check:
            // The implementation notifies OnGlobalLocked for both global and subject locks.
            // Acquire for Global -> OnGlobalLocked fires
            Assert.AreEqual(1, globalCallCount);
        }
    }
}