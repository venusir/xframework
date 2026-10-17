using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace XFramework.XCamera.Tests
{
    /// <summary>
    /// 门面生命周期与转发：注入、重复注入告警、未知状态抛错、Destroy 释放、会话复位。
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
        public void Initialize_Twice_LogsWarningAndKeepsFirst()
        {
            var first = new FakeCameraManager();
            var second = new FakeCameraManager();

            CameraManager.Initialize(first);
            LogAssert.Expect(LogType.Warning, new Regex("CameraManager\\.Initialize 被重复调用.*"));
            CameraManager.Initialize(second);

            CameraManager.Activate("main");
            Assert.AreEqual("main", first.LastActivated);
            Assert.IsNull(second.LastActivated);
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
        public void EnsureInitialized_ThrowsWithRepairHint()
        {
            var exception = Assert.Throws<InvalidOperationException>(() => CameraManager.Activate("main"));

            StringAssert.Contains("[CameraManager]", exception.Message);
            StringAssert.Contains("Initialize", exception.Message);
        }

        #endregion

        #region Forwarding

        [Test]
        public void Forwarding_Queries()
        {
            var ids = new[] { "main", "battle" };
            var fake = new FakeCameraManager
            {
                OutputCamera = _camera,
                CameraIds = ids,
                ActiveHandle = new CameraHandle(0, 1, "main"),
                IsTransitioning = true,
            };
            CameraManager.Initialize(fake);

            Assert.AreSame(_camera, CameraManager.OutputCamera);
            Assert.AreSame(ids, CameraManager.CameraIds);
            Assert.AreEqual("main", CameraManager.ActiveCameraId);
            Assert.IsTrue(CameraManager.IsTransitioning);

            Assert.IsTrue(CameraManager.IsActive("main"));
            Assert.IsFalse(CameraManager.IsActive("battle"));
            Assert.IsFalse(CameraManager.IsActive(null));
        }

        [Test]
        public void Forwarding_Commands()
        {
            var fake = new FakeCameraManager();
            CameraManager.Initialize(fake);

            CameraManager.Activate("battle");
            Assert.AreEqual("battle", fake.LastActivated);

            CameraManager.Deactivate("battle");
            Assert.AreEqual("battle", fake.LastDeactivated);

            CameraManager.TransitionTo("cutscene", 0.5f, CameraBlendStyle.Linear);
            Assert.AreEqual("cutscene", fake.LastTransitionTarget);
            Assert.AreEqual(0.5f, fake.LastTransitionDuration);
            Assert.AreEqual(CameraBlendStyle.Linear, fake.LastTransitionStyle);
        }

        [Test]
        public void Forwarding_ActiveHandle()
        {
            var handle = new CameraHandle(2, 5);
            var fake = new FakeCameraManager { ActiveHandle = handle };
            CameraManager.Initialize(fake);

            Assert.AreEqual(handle, CameraManager.ActiveHandle);
        }

        [Test]
        public void Forwarding_Register()
        {
            var expected = new CameraHandle(3, 1);
            var fake = new FakeCameraManager { RegisterResult = expected };
            CameraManager.Initialize(fake);

            var handle = CameraManager.Register(_camera);

            Assert.AreEqual(expected, handle);
            Assert.AreSame(_camera, fake.LastRegistered);
        }

        [Test]
        public void Forwarding_Unregister()
        {
            var fake = new FakeCameraManager { UnregisterResult = false };
            CameraManager.Initialize(fake);
            var handle = new CameraHandle(1, 2);

            Assert.IsFalse(CameraManager.Unregister(handle));
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
        }

        #endregion
    }
}
