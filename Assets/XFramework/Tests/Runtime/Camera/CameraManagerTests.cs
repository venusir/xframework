using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace XFramework.XCamera.Tests
{
    /// <summary>
    /// 门面生命周期与转发：注入、重复注入告警、探测型读宽容、变更命令抛错、Destroy 释放、会话复位。
    /// </summary>
    [TestFixture]
    public class CameraManagerTests
    {
        #region Fixture

        private GameObject _cameraObject;
        private Camera _camera;
        private GameObject _cameraObject2;
        private Camera _camera2;

        [SetUp]
        public void SetUp()
        {
            CameraManager.Destroy();
            _cameraObject = new GameObject("CameraManagerTests_Camera", typeof(Camera));
            _camera = _cameraObject.GetComponent<Camera>();
            _cameraObject2 = new GameObject("CameraManagerTests_Camera2", typeof(Camera));
            _camera2 = _cameraObject2.GetComponent<Camera>();
        }

        [TearDown]
        public void TearDown()
        {
            CameraManager.Destroy();
            if (_cameraObject != null)
                UnityEngine.Object.DestroyImmediate(_cameraObject);
            if (_cameraObject2 != null)
                UnityEngine.Object.DestroyImmediate(_cameraObject2);
        }

        private FakeCameraBackend Initialize()
        {
            var fake = new FakeCameraBackend();
            CameraManager.Initialize(fake);
            return fake;
        }

        #endregion

        #region Lifecycle

        [Test]
        public void IsInitialized_TracksInitializeAndDestroy()
        {
            Assert.IsFalse(CameraManager.IsInitialized);

            CameraManager.Initialize(new FakeCameraBackend());
            Assert.IsTrue(CameraManager.IsInitialized);

            CameraManager.Destroy();
            Assert.IsFalse(CameraManager.IsInitialized);
        }

        [Test]
        public void Initialize_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => CameraManager.Initialize(null));
        }

        [Test]
        public void Destroy_DisposeThrows_StillClearsInstance()
        {
            var throwing = new FakeCameraBackend { ThrowOnDispose = true };
            CameraManager.Initialize(throwing);

            Assert.Throws<InvalidOperationException>(() => CameraManager.Destroy());
            Assert.IsFalse(CameraManager.IsInitialized,
                "Dispose 抛出也必须清掉实例，否则留下永久闩锁（CAM-002）");

            var next = new FakeCameraBackend();
            Assert.DoesNotThrow(() => CameraManager.Initialize(next),
                "旧实例 Dispose 失败不得阻止下一次 Initialize");
            Assert.IsTrue(CameraManager.IsInitialized);
        }

        [Test]
        public void Initialize_Twice_LogsWarningAndKeepsFirst()
        {
            var first = new FakeCameraBackend();
            var second = new FakeCameraBackend();

            CameraManager.Initialize(first);
            LogAssert.Expect(LogType.Warning, new Regex("CameraManager\\.Initialize 被重复调用.*"));
            CameraManager.Initialize(second);

            CameraManager.Register("a", _camera);
            Assert.AreSame(_camera, first.LastRegistered);
            Assert.IsNull(second.LastRegistered);
        }

        [Test]
        public void Initialize_Twice_DoesNotDisposeSecond()
        {
            var first = new FakeCameraBackend();
            var second = new FakeCameraBackend();

            CameraManager.Initialize(first);
            LogAssert.Expect(LogType.Warning, new Regex("CameraManager\\.Initialize 被重复调用.*"));
            CameraManager.Initialize(second);

            Assert.IsFalse(second.Disposed,
                "被拒绝的第二个实例仍归调用方所有，门面不得擅自 Dispose");
        }

        [Test]
        public void Destroy_DisposesIdisposableImplementation()
        {
            var fake = Initialize();

            CameraManager.Destroy();

            Assert.IsTrue(fake.Disposed);
            Assert.IsFalse(CameraManager.IsInitialized);
        }

        [Test]
        public void ResetForNewSession_ClearsWithoutDisposing()
        {
            var fake = Initialize();

            CameraManager.ResetForNewSession();

            Assert.IsFalse(CameraManager.IsInitialized);
            Assert.IsFalse(fake.Disposed);
            Assert.AreEqual(1, fake.UnbindCount, "会话复位应让支持该能力的实现退订静态事件");
        }

        [Test]
        public void ResetForNewSession_UnbindThrows_LogsWarningAndClears()
        {
            var fake = new FakeCameraBackend { ThrowOnUnbind = true };
            CameraManager.Initialize(fake);

            LogAssert.Expect(LogType.Warning, new Regex("ResetForNewSession.*已忽略.*"));
            Assert.DoesNotThrow(() => CameraManager.ResetForNewSession());

            Assert.IsFalse(CameraManager.IsInitialized);
            Assert.IsFalse(fake.Disposed);
        }

        #endregion

        #region Probes / uninitialized

        [Test]
        public void Probes_WhenNotInitialized_ReturnDefaults()
        {
            Assert.IsNull(CameraManager.ActiveCameraId);
            Assert.IsNull(CameraManager.BaseCameraId);
            Assert.AreEqual(0, CameraManager.StackDepth);
            Assert.IsFalse(CameraManager.HasActiveCamera);
            Assert.IsNull(CameraManager.OutputCamera);
            Assert.IsFalse(CameraManager.IsBlending);
            Assert.IsFalse(CameraManager.IsExternallyControlled);
            Assert.AreEqual(0, CameraManager.CameraCount);
            Assert.AreEqual(0, CameraManager.GetState().RegisteredCount);

            var buffer = new List<CameraInfo> { default };
            Assert.AreEqual(0, CameraManager.CopyCameras(buffer));
            Assert.IsEmpty(buffer);

            Assert.IsFalse(CameraManager.TryGetCamera("a", out Camera camera));
            Assert.IsNull(camera);
            Assert.IsFalse(CameraManager.Unregister("a"));
        }

        [Test]
        public void Commands_WhenNotInitialized_ThrowWithRepairHint()
        {
            var exception = Assert.Throws<InvalidOperationException>(() => CameraManager.SwitchTo("a"));
            StringAssert.Contains("[CameraManager]", exception.Message);
            StringAssert.Contains("Initialize", exception.Message);

            Assert.Throws<InvalidOperationException>(() => CameraManager.Push("a"));
            Assert.Throws<InvalidOperationException>(() => CameraManager.Pop());
            Assert.Throws<InvalidOperationException>(() => CameraManager.PopAll());
            Assert.Throws<InvalidOperationException>(() => CameraManager.Clear());
            Assert.Throws<InvalidOperationException>(() => CameraManager.Register("a", _camera));
            Assert.Throws<InvalidOperationException>(() => CameraManager.Register(_camera));
        }

        [Test]
        public void StringEntryPoints_Null_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => CameraManager.Register(null, _camera));
            Assert.Throws<ArgumentNullException>(() => CameraManager.Unregister(null));
            Assert.Throws<ArgumentNullException>(() => CameraManager.SwitchTo(null));
            Assert.Throws<ArgumentNullException>(() => CameraManager.Push(null));
            Assert.Throws<ArgumentNullException>(() => CameraManager.TryGetCamera<Camera>(null, out _));
        }

        [Test]
        public void CopyCameras_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => CameraManager.CopyCameras(null));
        }

        #endregion

        #region Registry

        [Test]
        public void Register_ForwardsIdAndCamera()
        {
            var fake = Initialize();

            var id = CameraManager.Register("a", _camera);

            Assert.AreEqual("a", id);
            Assert.AreEqual("a", fake.LastRegisteredId);
            Assert.AreSame(_camera, fake.LastRegistered);
            Assert.IsFalse(fake.LastRegisteredIsGenerated);
        }

        [Test]
        public void Register_Generated_ReturnsAutoId()
        {
            var fake = Initialize();

            var first = CameraManager.Register(_camera);
            var second = CameraManager.Register(_camera2);

            StringAssert.StartsWith("@auto/", first);
            StringAssert.StartsWith("@auto/", second);
            Assert.AreNotEqual(first, second);
            Assert.IsTrue(fake.LastRegisteredIsGenerated);
        }

        [Test]
        public void Register_NullCamera_Throws()
        {
            Initialize();
            Assert.Throws<ArgumentNullException>(() => CameraManager.Register("a", null));
            Assert.Throws<ArgumentNullException>(() => CameraManager.Register(null));
        }

        [Test]
        public void Register_DestroyedCamera_Throws()
        {
            Initialize();
            var obj = new GameObject("Destroyed", typeof(Camera));
            var camera = obj.GetComponent<Camera>();
            UnityEngine.Object.DestroyImmediate(obj);

            Assert.Throws<ArgumentException>(() => CameraManager.Register("a", camera));
        }

        [Test]
        public void Register_EmptyOrReservedId_Throws()
        {
            Initialize();

            Assert.Throws<ArgumentException>(() => CameraManager.Register("", _camera));
            Assert.Throws<ArgumentException>(() => CameraManager.Register("   ", _camera));
            Assert.Throws<ArgumentException>(() => CameraManager.Register("@auto/1", _camera));
        }

        [Test]
        public void Unregister_Forwards()
        {
            var fake = Initialize();
            CameraManager.Register("a", _camera);

            Assert.IsTrue(CameraManager.Unregister("a"));
            Assert.AreEqual("a", fake.LastUnregisteredId);
            Assert.IsFalse(CameraManager.Unregister("unknown"));
        }

        #endregion

        #region Playback

        [Test]
        public void SwitchTo_ForwardsTransition()
        {
            var fake = Initialize();
            CameraManager.Register("a", _camera);

            CameraManager.SwitchTo("a", 0.3f, CameraBlendStyle.Linear);

            Assert.AreEqual("a", fake.LastActivatedId);
            Assert.AreEqual(0.3f, fake.LastActivateDuration, 0.0001f);
            Assert.AreEqual(CameraBlendStyle.Linear, fake.LastActivateStyle);
            Assert.AreEqual("a", CameraManager.BaseCameraId);
            Assert.AreEqual(1, CameraManager.StackDepth);
        }

        [Test]
        public void SwitchTo_SameTarget_IsNoOp()
        {
            var fake = Initialize();
            CameraManager.Register("a", _camera);

            CameraManager.SwitchTo("a");
            Assert.AreEqual(1, fake.ActivateCount);

            CameraManager.SwitchTo("a");
            Assert.AreEqual(1, fake.ActivateCount, "目标已是请求栈顶且活动时不应重复激活");
        }

        [Test]
        public void Push_AppendsAndPopReturnsToPrevious()
        {
            var fake = Initialize();
            CameraManager.Register("a", _camera);
            CameraManager.Register("b", _camera2);

            CameraManager.SwitchTo("a");
            CameraManager.Push("b", 0.2f);

            Assert.AreEqual("b", fake.LastActivatedId);
            Assert.AreEqual(2, CameraManager.StackDepth);
            Assert.AreEqual("a", CameraManager.BaseCameraId);

            CameraManager.Pop(0.25f);

            Assert.AreEqual("a", fake.LastActivatedId);
            Assert.AreEqual(1, CameraManager.StackDepth);
        }

        [Test]
        public void Push_SameTop_IsNoOp()
        {
            var fake = Initialize();
            CameraManager.Register("a", _camera);
            CameraManager.SwitchTo("a");

            CameraManager.Push("a");

            Assert.AreEqual(1, fake.ActivateCount);
            Assert.AreEqual(1, CameraManager.StackDepth);
        }

        [Test]
        public void Pop_WithOnlyBase_LogsWarningAndNoOps()
        {
            var fake = Initialize();
            CameraManager.Register("a", _camera);
            CameraManager.SwitchTo("a");

            LogAssert.Expect(LogType.Warning, new Regex("没有可弹出的覆盖层.*"));
            CameraManager.Pop();

            Assert.AreEqual(1, fake.ActivateCount);
            Assert.AreEqual(1, CameraManager.StackDepth);
        }

        [Test]
        public void PopAll_ReturnsToBase()
        {
            var fake = Initialize();
            CameraManager.Register("a", _camera);
            CameraManager.Register("b", _camera2);
            CameraManager.SwitchTo("a");
            CameraManager.Push("b");

            CameraManager.PopAll(0.4f);

            Assert.AreEqual("a", fake.LastActivatedId);
            Assert.AreEqual(1, CameraManager.StackDepth);
            Assert.AreEqual("a", CameraManager.BaseCameraId);
        }

        [Test]
        public void Clear_EmptiesStackAndBackend()
        {
            var fake = Initialize();
            CameraManager.Register("a", _camera);
            CameraManager.Register("b", _camera2);
            CameraManager.SwitchTo("a");
            CameraManager.Push("b");

            CameraManager.Clear();

            Assert.AreEqual(0, CameraManager.StackDepth);
            Assert.IsNull(CameraManager.BaseCameraId);
            Assert.AreEqual(1, fake.ClearActiveCount);
        }

        [Test]
        public void Unregister_Top_ReturnsToNewTop()
        {
            var fake = Initialize();
            CameraManager.Register("a", _camera);
            CameraManager.Register("b", _camera2);
            CameraManager.SwitchTo("a");
            CameraManager.Push("b");

            CameraManager.Unregister("b");

            Assert.AreEqual("a", fake.LastActivatedId);
            Assert.AreEqual(1, CameraManager.StackDepth);
        }

        [Test]
        public void Unregister_Middle_DoesNotChangeActive()
        {
            var fake = Initialize();
            CameraManager.Register("a", _camera);
            CameraManager.Register("b", _camera2);
            var thirdObject = new GameObject("Camera3", typeof(Camera));
            var third = thirdObject.GetComponent<Camera>();
            try
            {
                CameraManager.Register("c", third);
                CameraManager.SwitchTo("a");
                CameraManager.Push("b");
                CameraManager.Push("c");
                var countBefore = fake.ActivateCount;

                CameraManager.Unregister("b");

                Assert.AreEqual(countBefore, fake.ActivateCount, "移除中间层不改变当前激活");
                Assert.AreEqual(2, CameraManager.StackDepth);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(thirdObject);
            }
        }

        [Test]
        public void UnknownId_SwitchTo_LogsWarningAndNoOps()
        {
            var fake = Initialize();
            CameraManager.Register("a", _camera);
            CameraManager.SwitchTo("a");
            var count = fake.ActivateCount;

            LogAssert.Expect(LogType.Warning, new Regex("未知相机 id: missing.*"));
            CameraManager.SwitchTo("missing");

            Assert.AreEqual(count, fake.ActivateCount);
        }

        [Test]
        public void NonFiniteDuration_Throws()
        {
            Initialize();
            CameraManager.Register("a", _camera);

            Assert.Throws<ArgumentOutOfRangeException>(
                () => CameraManager.SwitchTo("a", float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CameraManager.SwitchTo("a", float.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CameraManager.Pop(float.NaN));
        }

        #endregion

        #region Escape hatch / diagnostics

        [Test]
        public void TryGetCamera_TypeMatches()
        {
            Initialize();
            CameraManager.Register("a", _camera);

            Assert.IsTrue(CameraManager.TryGetCamera("a", out Camera found));
            Assert.AreSame(_camera, found);
            Assert.IsFalse(CameraManager.TryGetCamera("a", out Transform _),
                "类型不匹配应返回 false");
            Assert.IsFalse(CameraManager.TryGetCamera("unknown", out Camera _));
        }

        [Test]
        public void GetState_ComposesStackAndBackend()
        {
            var fake = Initialize();
            CameraManager.Register("a", _camera);
            CameraManager.Register("b", _camera2);
            CameraManager.SwitchTo("a");
            CameraManager.Push("b");

            var state = CameraManager.GetState();

            Assert.AreEqual(2, state.RegisteredCount);
            Assert.AreEqual(2, state.NamedCount);
            Assert.AreEqual(0, state.GeneratedCount);
            Assert.AreEqual("a", state.BaseCameraId);
            Assert.AreEqual("b", state.ActiveCameraId);
            Assert.AreEqual(2, state.StackDepth);
            Assert.IsFalse(state.IsTransitioning);
            Assert.IsFalse(state.IsExternallyControlled);
        }

        [Test]
        public void IsExternallyControlled_WhenActiveIsNotTop()
        {
            var fake = Initialize();
            CameraManager.Register("a", _camera);
            CameraManager.SwitchTo("a");

            fake.ActiveCameraId = "external";
            fake.HasActiveCamera = true;
            Assert.IsTrue(CameraManager.IsExternallyControlled);

            fake.TransitionTargetId = "a";
            Assert.IsFalse(CameraManager.IsExternallyControlled,
                "有在途请求时不应报告外部接管");
        }

        [Test]
        public void CopyCameras_FlagsBaseStackAndActive()
        {
            Initialize();
            CameraManager.Register("a", _camera);
            CameraManager.Register("b", _camera2);
            CameraManager.SwitchTo("a");
            CameraManager.Push("b");

            var buffer = new List<CameraInfo>();
            Assert.AreEqual(2, CameraManager.CopyCameras(buffer));

            var a = buffer[0];
            Assert.AreEqual("a", a.CameraId);
            Assert.IsTrue(a.IsBase);
            Assert.IsTrue(a.IsInStack);
            Assert.IsFalse(a.IsActive);
            Assert.IsTrue(a.IsAlive);

            var b = buffer[1];
            Assert.AreEqual("b", b.CameraId);
            Assert.IsFalse(b.IsBase);
            Assert.IsTrue(b.IsInStack);
            Assert.IsTrue(b.IsActive);
            Assert.IsFalse(b.IsGenerated);
        }

        #endregion
    }
}
