using System;
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

        [SetUp]
        public void SetUp()
        {
            CameraManager.Destroy();
            _cameraObject = new GameObject("CameraManagerTests_Camera", typeof(Camera));
            _camera = _cameraObject.GetComponent<Camera>();
        }

        [TearDown]
        public void TearDown()
        {
            CameraManager.Destroy();
            if (_cameraObject != null)
                UnityEngine.Object.DestroyImmediate(_cameraObject);
        }

        #endregion

        #region Lifecycle

        [Test]
        public void IsInitialized_TracksInitializeAndDestroy()
        {
            Assert.IsFalse(CameraManager.IsInitialized);

            CameraManager.Initialize(new FakeCameraManager());
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
            var throwing = new FakeCameraManager { ThrowOnDispose = true };
            CameraManager.Initialize(throwing);

            Assert.Throws<InvalidOperationException>(() => CameraManager.Destroy());
            Assert.IsFalse(CameraManager.IsInitialized,
                "Dispose 抛出也必须清掉实例，否则留下永久闩锁（CAM-002）");

            var next = new FakeCameraManager();
            Assert.DoesNotThrow(() => CameraManager.Initialize(next),
                "旧实例 Dispose 失败不得阻止下一次 Initialize");
            Assert.IsTrue(CameraManager.IsInitialized);
        }

        [Test]
        public void Initialize_Twice_LogsWarningAndKeepsFirst()
        {
            var first = new FakeCameraManager();
            var second = new FakeCameraManager();

            CameraManager.Initialize(first);
            LogAssert.Expect(LogType.Warning, new Regex("CameraManager\\.Initialize 被重复调用.*"));
            CameraManager.Initialize(second);

            CameraManager.Register(_camera);
            Assert.AreSame(_camera, first.LastRegistered);
            Assert.IsNull(second.LastRegistered);
        }

        [Test]
        public void Destroy_DisposesIdisposableImplementation()
        {
            var fake = new FakeCameraManager();
            CameraManager.Initialize(fake);

            CameraManager.Destroy();

            Assert.IsTrue(fake.Disposed);
            Assert.IsFalse(CameraManager.IsInitialized);
        }

        [Test]
        public void ResetForNewSession_ClearsWithoutDisposing()
        {
            var fake = new FakeCameraManager();
            CameraManager.Initialize(fake);

            CameraManager.ResetForNewSession();

            Assert.IsFalse(CameraManager.IsInitialized);
            Assert.IsFalse(fake.Disposed);
        }

        [Test]
        public void Commands_WhenNotInitialized_ThrowWithRepairHint()
        {
            var exception = Assert.Throws<InvalidOperationException>(() => CameraManager.Activate("main"));
            StringAssert.Contains("[CameraManager]", exception.Message);
            StringAssert.Contains("Initialize", exception.Message);

            Assert.Throws<InvalidOperationException>(() => CameraManager.Activate(new CameraHandle(0, 1)));
            Assert.Throws<InvalidOperationException>(() => CameraManager.Deactivate("main"));
            Assert.Throws<InvalidOperationException>(() => CameraManager.TransitionTo("main", 1f));
            Assert.Throws<InvalidOperationException>(() => CameraManager.Register(_camera));
        }

        [Test]
        public void Probes_WhenNotInitialized_ReturnDefaults()
        {
            Assert.IsNull(CameraManager.OutputCamera);
            Assert.IsTrue(CameraManager.ActiveHandle.IsDefault);
            Assert.IsNull(CameraManager.ActiveCameraId);
            Assert.IsFalse(CameraManager.IsTransitioning);
            Assert.AreEqual(0, CameraManager.CameraCount);
            Assert.IsFalse(CameraManager.TryGetHandle("main", out var handle));
            Assert.IsTrue(handle.IsDefault);
            Assert.IsFalse(CameraManager.IsActive("main"));
            Assert.IsFalse(CameraManager.IsActive(new CameraHandle(0, 1)));
            Assert.IsFalse(CameraManager.Unregister(new CameraHandle(0, 1)));
            Assert.IsFalse(CameraManager.Unregister("main"));
        }

        #endregion

        #region Forwarding

        [Test]
        public void Forwarding_Queries()
        {
            var handle = new CameraHandle(0, 1, "main");
            var fake = new FakeCameraManager
            {
                OutputCamera = _camera,
                ActiveHandle = handle,
                IsTransitioning = true,
                CameraCount = 2,
            };
            fake.NamedHandles["main"] = handle;
            CameraManager.Initialize(fake);

            Assert.AreSame(_camera, CameraManager.OutputCamera);
            Assert.AreEqual(handle, CameraManager.ActiveHandle);
            Assert.AreEqual("main", CameraManager.ActiveCameraId);
            Assert.IsTrue(CameraManager.IsTransitioning);
            Assert.AreEqual(2, CameraManager.CameraCount);

            Assert.IsTrue(CameraManager.TryGetHandle("main", out var resolved));
            Assert.AreEqual(handle, resolved);
            Assert.IsFalse(CameraManager.TryGetHandle("battle", out _));

            Assert.IsTrue(CameraManager.IsActive("main"));
            Assert.IsFalse(CameraManager.IsActive("battle"));
            Assert.IsFalse(CameraManager.IsActive((string)null));
            Assert.IsTrue(CameraManager.IsActive(handle));
        }

        [Test]
        public void Forwarding_Commands()
        {
            var battle = new CameraHandle(0, 1, "battle");
            var cutscene = new CameraHandle(1, 1, "cutscene");
            var fake = new FakeCameraManager();
            fake.NamedHandles["battle"] = battle;
            fake.NamedHandles["cutscene"] = cutscene;
            CameraManager.Initialize(fake);

            CameraManager.Activate("battle");
            Assert.AreEqual(battle, fake.LastActivatedHandle);

            CameraManager.Deactivate("battle");
            Assert.AreEqual(battle, fake.LastDeactivatedHandle);

            CameraManager.TransitionTo("cutscene", 0.5f, CameraBlendStyle.Linear);
            Assert.AreEqual(cutscene, fake.LastTransitionHandle);
            Assert.AreEqual(0.5f, fake.LastTransitionDuration);
            Assert.AreEqual(CameraBlendStyle.Linear, fake.LastTransitionStyle);
        }

        [Test]
        public void StringCommands_UnknownId_WarnAndNoOp()
        {
            var fake = new FakeCameraManager();
            CameraManager.Initialize(fake);

            LogAssert.Expect(LogType.Warning, new Regex("未知相机 id.*"));
            CameraManager.Activate("missing");
            Assert.IsTrue(fake.LastActivatedHandle.IsDefault);

            LogAssert.Expect(LogType.Warning, new Regex("未知相机 id.*"));
            CameraManager.Deactivate("missing");
            Assert.IsTrue(fake.LastDeactivatedHandle.IsDefault);

            LogAssert.Expect(LogType.Warning, new Regex("未知相机 id.*"));
            CameraManager.TransitionTo("missing", 1f);
            Assert.IsTrue(fake.LastTransitionHandle.IsDefault);

            Assert.IsFalse(CameraManager.Unregister("missing"));
        }

        [Test]
        public void Forwarding_Register()
        {
            var expected = new CameraHandle(0, 1, "main");
            var fake = new FakeCameraManager { RegisterResult = expected };
            CameraManager.Initialize(fake);

            var named = CameraManager.Register("main", _camera);
            Assert.AreEqual(expected, named);
            Assert.AreEqual("main", fake.LastRegisteredId);
            Assert.AreSame(_camera, fake.LastRegistered);

            var dynamicHandle = CameraManager.Register(_camera);
            Assert.AreEqual(expected, dynamicHandle);
            Assert.IsNull(fake.LastRegisteredId);
            Assert.AreSame(_camera, fake.LastRegistered);
        }

        [Test]
        public void Forwarding_Unregister()
        {
            var handle = new CameraHandle(1, 2, "main");
            var fake = new FakeCameraManager { UnregisterResult = false };
            fake.NamedHandles["main"] = handle;
            CameraManager.Initialize(fake);

            Assert.IsFalse(CameraManager.Unregister(handle));
            Assert.AreEqual(handle, fake.LastUnregistered);

            Assert.IsFalse(CameraManager.Unregister("main"));
            Assert.AreEqual(handle, fake.LastUnregistered);
        }

        [Test]
        public void Unregister_WhenNotInitialized_ReturnsFalse()
        {
            Assert.IsFalse(CameraManager.Unregister(new CameraHandle(0, 1)));
        }

        [Test]
        public void Forwarding_HandleCommands()
        {
            var fake = new FakeCameraManager();
            CameraManager.Initialize(fake);
            var handle = new CameraHandle(4, 2);

            CameraManager.Activate(handle);
            Assert.AreEqual(handle, fake.LastActivatedHandle);

            CameraManager.Deactivate(handle);
            Assert.AreEqual(handle, fake.LastDeactivatedHandle);

            CameraManager.TransitionTo(handle, 0.5f, CameraBlendStyle.Linear);
            Assert.AreEqual(handle, fake.LastTransitionHandle);

            fake.ActiveHandle = handle;
            Assert.IsTrue(CameraManager.IsActive(handle));
        }

        [Test]
        public void Register_Null_Throws()
        {
            CameraManager.Initialize(new FakeCameraManager());

            Assert.Throws<ArgumentNullException>(() => CameraManager.Register<Camera>(null));
            Assert.Throws<ArgumentNullException>(() => CameraManager.Register("main", (Camera)null));
        }

        [Test]
        public void Register_DestroyedCamera_Throws()
        {
            CameraManager.Initialize(new FakeCameraManager());
            var temp = new GameObject("DestroyedCamera", typeof(Camera));
            var camera = temp.GetComponent<Camera>();
            UnityEngine.Object.DestroyImmediate(temp);

            Assert.Throws<ArgumentException>(() => CameraManager.Register("main", camera));
        }

        [Test]
        public void StringEntryPoints_Null_Throw()
        {
            CameraManager.Initialize(new FakeCameraManager());

            Assert.Throws<ArgumentNullException>(() => CameraManager.Activate((string)null));
            Assert.Throws<ArgumentNullException>(() => CameraManager.Deactivate((string)null));
            Assert.Throws<ArgumentNullException>(() => CameraManager.TransitionTo((string)null, 1f));
            Assert.Throws<ArgumentNullException>(() => CameraManager.Unregister((string)null));
            Assert.Throws<ArgumentNullException>(() => CameraManager.TryGetHandle(null, out _));
        }

        #endregion
    }
}
