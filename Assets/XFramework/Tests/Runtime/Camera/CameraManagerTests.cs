using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XCamera.Messages;
using XFramework.XMessage;

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
        public void Initialize_Twice_DoesNotDisposeSecond()
        {
            var first = new FakeCameraManager();
            var second = new FakeCameraManager();

            CameraManager.Initialize(first);
            LogAssert.Expect(LogType.Warning, new Regex("CameraManager\\.Initialize 被重复调用.*"));
            CameraManager.Initialize(second);

            Assert.IsFalse(second.Disposed,
                "被拒绝的第二个实例仍归调用方所有，门面不得擅自 Dispose");
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
            Assert.AreEqual(1, fake.UnbindCount, "会话复位应让支持该能力的实现退订静态事件");
        }

        [Test]
        public void ResetForNewSession_UnbindThrows_LogsWarningAndClears()
        {
            var fake = new FakeCameraManager { ThrowOnUnbind = true };
            CameraManager.Initialize(fake);

            LogAssert.Expect(LogType.Warning, new Regex("ResetForNewSession.*已忽略.*"));
            Assert.DoesNotThrow(() => CameraManager.ResetForNewSession());

            Assert.IsFalse(CameraManager.IsInitialized);
            Assert.AreEqual(1, fake.UnbindCount);
            Assert.IsFalse(fake.Disposed);
        }

        [Test]
        public void Subscribe_WithoutInitialize_ForwardsMessages()
        {
            CameraManager.Destroy();
            var received = new List<CameraActivatedMessage>();
            using var subscription = CameraManager.Subscribe(
                (CameraActivatedMessage message) => received.Add(message));

            var handle = new CameraHandle(0, 1, "main");
            MessageManager.Publish(new CameraActivatedMessage(handle, default, true));

            Assert.AreEqual(1, received.Count);
            Assert.AreEqual(handle, received[0].Handle);
            Assert.IsTrue(received[0].IsCut);
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
            Assert.Throws<InvalidOperationException>(
                () => CameraManager.TransitionToAsync(new CameraHandle(0, 1), 1f));
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

            var buffer = new List<CameraInfo>
            {
                new CameraInfo(new CameraHandle(1, 1), false, false, true),
            };
            Assert.AreEqual(0, CameraManager.CopyCameras(buffer));
            Assert.IsEmpty(buffer, "未初始化时 CopyCameras 必须清空 buffer");
            Assert.AreEqual(0, CameraManager.GetState().RegisteredCount);
        }

        [Test]
        public void Forwarding_Diagnostics()
        {
            var handle = new CameraHandle(0, 1, "main");
            var fake = new FakeCameraManager
            {
                State = new CameraStateSnapshot(
                    1, 1, 0, handle, default, false, true, 0.5f, _camera),
            };
            fake.CopyResult.Add(new CameraInfo(handle, true, false, true));
            CameraManager.Initialize(fake);

            var buffer = new List<CameraInfo>();
            var count = CameraManager.CopyCameras(buffer);

            Assert.AreEqual(1, count);
            Assert.AreEqual(handle, buffer[0].Handle);
            Assert.IsTrue(buffer[0].IsActive);
            Assert.IsTrue(buffer[0].IsAlive);

            var state = CameraManager.GetState();
            Assert.AreEqual(1, state.RegisteredCount);
            Assert.AreEqual(handle, state.ActiveHandle);
            Assert.IsTrue(state.IsBrainBlending);
            Assert.AreEqual(0.5f, state.TransitionProgress);
            Assert.AreSame(_camera, state.OutputCamera);
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

            LogAssert.Expect(LogType.Warning, new Regex("未知相机 id.*"));
            var asyncResult = CameraManager.TransitionToAsync("missing", 1f).GetAwaiter().GetResult();
            Assert.AreEqual(CameraTransitionResult.TargetUnregistered, asyncResult);

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
        public void Forwarding_TransitionToAsync()
        {
            var handle = new CameraHandle(0, 1, "battle");
            var fake = new FakeCameraManager { AsyncResult = CameraTransitionResult.Completed };
            fake.NamedHandles["battle"] = handle;
            CameraManager.Initialize(fake);

            var result = CameraManager.TransitionToAsync("battle", 0.5f, CameraBlendStyle.Linear)
                .GetAwaiter().GetResult();

            Assert.AreEqual(CameraTransitionResult.Completed, result);
            Assert.AreEqual(handle, fake.LastAsyncHandle);
            Assert.AreEqual(0.5f, fake.LastAsyncDuration);
            Assert.AreEqual(CameraBlendStyle.Linear, fake.LastAsyncStyle);
        }

        [Test]
        public void CopyCameras_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => CameraManager.CopyCameras(null));
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
