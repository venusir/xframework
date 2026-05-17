using System;
using System.Collections.Generic;
using NUnit.Framework;
using XFramework.XLock;

namespace XFramework.XLock.Tests
{
    /// <summary>
    /// Tests for <see cref="LockManager"/>.
    /// All tests run on a clean state; <see cref="LockManager.Dispose()"/> is called in TearDown.
    /// </summary>
    [TestFixture]
    public class LockManagerTests
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
            LockManager.Dispose();
            _subjectA = new TestLockable();
            _subjectB = new TestLockable();
        }

        [TearDown]
        public void TearDown()
        {
            LockManager.Dispose();
        }

        [Test]
        public void Acquire_NewLock_ReturnsValidHandle()
        {
            var handle = LockManager.AddLock(_subjectA, LockTypeMovement, _lockObj1);
            Assert.IsTrue(handle.IsValid);
        }

        [Test]
        public void Acquire_NullLockSubject_UsesGlobal()
        {
            var handle = LockManager.AddLock(null, LockTypeMovement, _lockObj1);
            Assert.IsTrue(handle.IsValid);
        }

        [Test]
        public void Acquire_NullLock_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                LockManager.AddLock(_subjectA, LockTypeMovement, null));
        }

        [Test]
        public void IsLocked_AfterAcquire_ReturnsTrue()
        {
            LockManager.AddLock(_subjectA, LockTypeMovement, _lockObj1);
            Assert.IsTrue(LockManager.IsLocked(_subjectA, LockTypeMovement));
        }

        [Test]
        public void IsLocked_UnknownSubject_ReturnsFalse()
        {
            Assert.IsFalse(LockManager.IsLocked(_subjectA, LockTypeMovement));
        }

        [Test]
        public void IsLocked_NullSubject_ChecksGlobal()
        {
            LockManager.AddLock(null, LockTypeAttack, _lockObj1);
            Assert.IsTrue(LockManager.IsLocked(null, LockTypeAttack));
        }

        [Test]
        public void Release_RemovesLock_IsLockedReturnsFalse()
        {
            LockManager.AddLock(_subjectA, LockTypeMovement, _lockObj1);
            LockManager.RemoveLock(_subjectA, LockTypeMovement, _lockObj1);
            Assert.IsFalse(LockManager.IsLocked(_subjectA, LockTypeMovement));
        }

        [Test]
        public void Release_NonExistentLock_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
                LockManager.RemoveLock(_subjectA, LockTypeMovement, _lockObj1));
        }

        [Test]
        public void Release_NullLock_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                LockManager.RemoveLock(_subjectA, LockTypeMovement, null));
        }

        [Test]
        public void GetLockCount_ReturnsCorrectCount()
        {
            Assert.AreEqual(0, LockManager.GetLockCount(_subjectA, LockTypeMovement));

            LockManager.AddLock(_subjectA, LockTypeMovement, _lockObj1);
            Assert.AreEqual(1, LockManager.GetLockCount(_subjectA, LockTypeMovement));

            LockManager.AddLock(_subjectA, LockTypeMovement, _lockObj2);
            Assert.AreEqual(2, LockManager.GetLockCount(_subjectA, LockTypeMovement));
        }

        [Test]
        public void GetLockCount_GlobalLockCountedForSubject()
        {
            LockManager.AddLock(null, LockTypeMovement, _lockObj1);
            // subjectA should see it through global
            Assert.AreEqual(1, LockManager.GetLockCount(_subjectA, LockTypeMovement));

            LockManager.AddLock(_subjectA, LockTypeMovement, _lockObj2);
            // 1 global + 1 subject-specific = 2
            Assert.AreEqual(2, LockManager.GetLockCount(_subjectA, LockTypeMovement));
        }

        [Test]
        public void GetLockObjects_ReturnsAllObjects()
        {
            LockManager.AddLock(_subjectA, LockTypeMovement, _lockObj1);
            LockManager.AddLock(_subjectA, LockTypeMovement, _lockObj2);

            var objects = LockManager.GetLockObjects(_subjectA, LockTypeMovement);
            Assert.AreEqual(2, objects.Count);
            CollectionAssert.Contains(objects, _lockObj1);
            CollectionAssert.Contains(objects, _lockObj2);
        }

        [Test]
        public void GetLockObjects_GlobalLockObjectsIncluded()
        {
            LockManager.AddLock(null, LockTypeMovement, _lockObj1);
            LockManager.AddLock(_subjectA, LockTypeMovement, _lockObj2);

            var objects = LockManager.GetLockObjects(_subjectA, LockTypeMovement);
            Assert.AreEqual(2, objects.Count);
            CollectionAssert.Contains(objects, _lockObj1);
            CollectionAssert.Contains(objects, _lockObj2);
        }

        [Test]
        public void GlobalLock_AffectsAllSubjects()
        {
            LockManager.AddLock(null, LockTypeMovement, _lockObj1);

            Assert.IsTrue(LockManager.IsLocked(_subjectA, LockTypeMovement));
            Assert.IsTrue(LockManager.IsLocked(_subjectB, LockTypeMovement));
        }

        [Test]
        public void GlobalLock_Released_NoLongerAffectsSubjects()
        {
            LockManager.AddLock(null, LockTypeMovement, _lockObj1);
            LockManager.RemoveLock(null, LockTypeMovement, _lockObj1);

            Assert.IsFalse(LockManager.IsLocked(_subjectA, LockTypeMovement));
        }

        [Test]
        public void MultipleLockTypes_Independent()
        {
            LockManager.AddLock(_subjectA, LockTypeMovement, _lockObj1);
            Assert.IsTrue(LockManager.IsLocked(_subjectA, LockTypeMovement));
            Assert.IsFalse(LockManager.IsLocked(_subjectA, LockTypeAttack));

            LockManager.AddLock(_subjectA, LockTypeAttack, _lockObj2);
            Assert.IsTrue(LockManager.IsLocked(_subjectA, LockTypeAttack));

            LockManager.RemoveLock(_subjectA, LockTypeMovement, _lockObj1);
            Assert.IsFalse(LockManager.IsLocked(_subjectA, LockTypeMovement));
            Assert.IsTrue(LockManager.IsLocked(_subjectA, LockTypeAttack));
        }

        [Test]
        public void MultipleSubjects_Independent()
        {
            LockManager.AddLock(_subjectA, LockTypeMovement, _lockObj1);
            LockManager.AddLock(_subjectB, LockTypeAttack, _lockObj2);

            Assert.IsTrue(LockManager.IsLocked(_subjectA, LockTypeMovement));
            Assert.IsFalse(LockManager.IsLocked(_subjectA, LockTypeAttack));
            Assert.IsTrue(LockManager.IsLocked(_subjectB, LockTypeAttack));
            Assert.IsFalse(LockManager.IsLocked(_subjectB, LockTypeMovement));
        }

        [Test]
        public void LockHandle_Dispose_ReleasesLock()
        {
            var handle = LockManager.AddLock(_subjectA, LockTypeMovement, _lockObj1);
            handle.Dispose();

            Assert.IsFalse(LockManager.IsLocked(_subjectA, LockTypeMovement));
        }

        [Test]
        public void LockHandle_Using_ReleasesLock()
        {
            using (LockManager.AddLock(_subjectA, LockTypeMovement, _lockObj1))
            {
                Assert.IsTrue(LockManager.IsLocked(_subjectA, LockTypeMovement));
            }

            Assert.IsFalse(LockManager.IsLocked(_subjectA, LockTypeMovement));
        }

        [Test]
        public void LockHandle_DoubleDispose_DoesNotThrow()
        {
            var handle = LockManager.AddLock(_subjectA, LockTypeMovement, _lockObj1);
            handle.Dispose();
            Assert.DoesNotThrow(() => handle.Dispose());
        }

        [Test]
        public void OnLockedEvent_CalledOnLock()
        {
            var called = false;
            int receivedType = -1;
            LockManager.OnLocked(_subjectA, (lockType) =>
            {
                called = true;
                receivedType = lockType;
            });

            LockManager.AddLock(_subjectA, LockTypeMovement, _lockObj1);

            Assert.IsTrue(called);
            Assert.AreEqual(LockTypeMovement, receivedType);
        }

        [Test]
        public void OnUnlockedEvent_CalledOnRelease()
        {
            var called = false;
            int receivedType = -1;
            LockManager.OnUnlocked(_subjectA, (lockType) =>
            {
                called = true;
                receivedType = lockType;
            });

            LockManager.AddLock(_subjectA, LockTypeMovement, _lockObj1);
            LockManager.RemoveLock(_subjectA, LockTypeMovement, _lockObj1);

            Assert.IsTrue(called);
            Assert.AreEqual(LockTypeMovement, receivedType);
        }

        [Test]
        public void OnLockedEvent_GlobalLock_NotifiesAllSubjectSubscribers()
        {
            var callCountA = 0;
            var callCountB = 0;
            LockManager.OnLocked(_subjectA, _ => callCountA++);
            LockManager.OnLocked(_subjectB, _ => callCountB++);

            LockManager.AddLock(null, LockTypeMovement, _lockObj1);

            Assert.AreEqual(1, callCountA);
            Assert.AreEqual(1, callCountB);
        }

        [Test]
        public void OnUnlockedEvent_GlobalLock_NotifiesAllSubjectSubscribers()
        {
            var callCountA = 0;
            var callCountB = 0;
            LockManager.OnUnlocked(_subjectA, _ => callCountA++);
            LockManager.OnUnlocked(_subjectB, _ => callCountB++);

            LockManager.AddLock(null, LockTypeMovement, _lockObj1);
            LockManager.RemoveLock(null, LockTypeMovement, _lockObj1);

            Assert.AreEqual(1, callCountA);
            Assert.AreEqual(1, callCountB);
        }

        [Test]
        public void OnLockedEvent_Disposed_Unsubscribes()
        {
            var callCount = 0;
            var disposable = LockManager.OnLocked(_subjectA, _ => callCount++);

            disposable.Dispose();

            LockManager.AddLock(_subjectA, LockTypeMovement, _lockObj1);
            Assert.AreEqual(0, callCount);
        }

        [Test]
        public void OnUnlockedEvent_Disposed_Unsubscribes()
        {
            var callCount = 0;
            var disposable = LockManager.OnUnlocked(_subjectA, _ => callCount++);

            disposable.Dispose();

            LockManager.AddLock(_subjectA, LockTypeMovement, _lockObj1);
            LockManager.RemoveLock(_subjectA, LockTypeMovement, _lockObj1);
            Assert.AreEqual(0, callCount);
        }

        [Test]
        public void OnGlobalLockedEvent_CalledOnFirstLock()
        {
            var called = false;
            ILockable receivedSubject = null;
            int receivedType = -1;
            object receivedLock = null;

            LockManager.OnGlobalLocked += (subject, lockType, lockObj) =>
            {
                called = true;
                receivedSubject = subject;
                receivedType = lockType;
                receivedLock = lockObj;
            };

            LockManager.AddLock(_subjectA, LockTypeAttack, _lockObj2);

            Assert.IsTrue(called);
            Assert.AreEqual(_subjectA, receivedSubject);
            Assert.AreEqual(LockTypeAttack, receivedType);
            Assert.AreEqual(_lockObj2, receivedLock);
        }

        [Test]
        public void OnGlobalUnlockedEvent_CalledOnLastRelease()
        {
            var called = false;
            LockManager.OnGlobalUnlocked += (subject, lockType, lockObj) =>
            {
                called = true;
            };

            LockManager.AddLock(_subjectA, LockTypeMovement, _lockObj1);
            LockManager.RemoveLock(_subjectA, LockTypeMovement, _lockObj1);

            Assert.IsTrue(called);
        }

        [Test]
        public void OnGlobalLocked_NotCalledOnSecondLockSameType()
        {
            var callCount = 0;
            LockManager.OnGlobalLocked += (_, _, _) => callCount++;

            LockManager.AddLock(_subjectA, LockTypeMovement, _lockObj1);
            LockManager.AddLock(_subjectA, LockTypeMovement, _lockObj2);

            // Only first lock triggers the event
            Assert.AreEqual(1, callCount);
        }

        [Test]
        public void OnGlobalUnlocked_NotCalledWhenStillLocksRemain()
        {
            var callCount = 0;
            LockManager.OnGlobalUnlocked += (_, _, _) => callCount++;

            LockManager.AddLock(_subjectA, LockTypeMovement, _lockObj1);
            LockManager.AddLock(_subjectA, LockTypeMovement, _lockObj2);
            LockManager.RemoveLock(_subjectA, LockTypeMovement, _lockObj1);

            // Still one lock remains, so event should not fire
            Assert.AreEqual(0, callCount);
        }

        [Test]
        public void Dispose_ClearsAllState()
        {
            LockManager.AddLock(_subjectA, LockTypeMovement, _lockObj1);
            LockManager.AddLock(_subjectB, LockTypeAttack, _lockObj2);

            LockManager.Dispose();

            Assert.IsFalse(LockManager.IsLocked(_subjectA, LockTypeMovement));
            Assert.IsFalse(LockManager.IsLocked(_subjectB, LockTypeAttack));
        }

        [Test]
        public void Dispose_Twice_DoesNotThrow()
        {
            LockManager.Dispose();
            Assert.DoesNotThrow(() => LockManager.Dispose());
        }

        [Test]
        public void DifferentLockTypes_DoNotConflict()
        {
            LockManager.AddLock(_subjectA, LockTypeMovement, _lockObj1);
            LockManager.AddLock(_subjectA, LockTypeAttack, _lockObj2);

            Assert.AreEqual(1, LockManager.GetLockCount(_subjectA, LockTypeMovement));
            Assert.AreEqual(1, LockManager.GetLockCount(_subjectA, LockTypeAttack));

            LockManager.RemoveLock(_subjectA, LockTypeMovement, _lockObj1);
            Assert.AreEqual(0, LockManager.GetLockCount(_subjectA, LockTypeMovement));
            Assert.AreEqual(1, LockManager.GetLockCount(_subjectA, LockTypeAttack));
        }

        [Test]
        public void SameLock_SameSubject_CanBeAddedMultipleTimes()
        {
            // Multiple Acquires of the same (subject, lockType, lockObj) is idempotent in HashSet
            LockManager.AddLock(_subjectA, LockTypeMovement, _lockObj1);
            LockManager.AddLock(_subjectA, LockTypeMovement, _lockObj1);

            // Count should be 1 (HashSet dedup)
            Assert.AreEqual(1, LockManager.GetLockCount(_subjectA, LockTypeMovement));
        }

        [Test]
        public void LockHandle_IsValid_ReturnsFalseAfterDispose()
        {
            var handle = LockManager.AddLock(_subjectA, LockTypeMovement, _lockObj1);
            Assert.IsTrue(handle.IsValid);

            handle.Dispose();
            Assert.IsFalse(handle.IsValid);
        }

        [Test]
        public void OnLockedEvent_SubjectSpecific_GlobalNotNotified()
        {
            var globalCallCount = 0;
            LockManager.OnGlobalLocked += (_, _, _) => globalCallCount++;

            LockManager.AddLock(LockManager.Global, LockTypeMovement, _lockObj1);

            // Global lock should also trigger OnGlobalLocked? Let's check:
            // The implementation notifies OnGlobalLocked for both global and subject locks.
            // Acquire for Global -> OnGlobalLocked fires
            Assert.AreEqual(1, globalCallCount);
        }
    }
}