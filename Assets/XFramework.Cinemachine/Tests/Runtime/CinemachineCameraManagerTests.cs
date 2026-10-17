using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.TestTools;

namespace XFramework.XCamera.Cinemachine.Tests
{
    /// <summary>
    /// <see cref="CinemachineCameraManager"/> 的行为：优先级激活 / Brain 过渡 / 状态反查 / 注册表。
    /// <para>Brain 用 <see cref="UpdateMethods.ManualUpdate"/> + <c>ManualUpdate(frame, delta)</c> 确定性推进，
    /// 不依赖真实帧时长。</para>
    /// </summary>
    [TestFixture]
    public class CinemachineCameraManagerTests
    {
        #region Fixture

        private GameObject _cameraObject;
        private Camera _camera;
        private CinemachineBrain _brain;
        private GameObject _vcamAObject;
        private GameObject _vcamBObject;
        private CinemachineCamera _vcamA;
        private CinemachineCamera _vcamB;
        private CinemachineCameraManager _manager;
        private int _frame;

        [SetUp]
        public void SetUp()
        {
            _cameraObject = new GameObject("Camera");
            _camera = _cameraObject.AddComponent<Camera>();
            _brain = _cameraObject.AddComponent<CinemachineBrain>();
            _brain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;

            _vcamAObject = new GameObject("VcamA");
            _vcamA = _vcamAObject.AddComponent<CinemachineCamera>();
            _vcamBObject = new GameObject("VcamB");
            _vcamB = _vcamBObject.AddComponent<CinemachineCamera>();

            _manager = new CinemachineCameraManager(_brain,
                new Dictionary<string, CinemachineVirtualCameraBase>
                {
                    ["a"] = _vcamA,
                    ["b"] = _vcamB,
                });
            _frame = 0;
        }

        [TearDown]
        public void TearDown()
        {
            _manager?.Dispose();

            if (_vcamAObject != null)
                UnityEngine.Object.DestroyImmediate(_vcamAObject);
            if (_vcamBObject != null)
                UnityEngine.Object.DestroyImmediate(_vcamBObject);
            if (_cameraObject != null)
                UnityEngine.Object.DestroyImmediate(_cameraObject);
        }

        private void Pump(float delta = 0f)
        {
            _brain.ManualUpdate(_frame++, delta);
        }

        #endregion

        #region Activation

        [Test]
        public void Activate_MakesTargetActive()
        {
            _manager.Activate("a");
            Pump();

            Assert.AreEqual("a", _manager.ActiveHandle.NamedId);
            Assert.AreSame(_camera, _manager.OutputCamera);
            Assert.IsTrue(_manager.IsActive("a"));
            Assert.IsFalse(_manager.IsTransitioning);
            Assert.AreEqual(int.MaxValue, _vcamA.Priority.Value);
            Assert.AreEqual(0, _vcamB.Priority.Value);
        }

        [Test]
        public void Activate_UnknownId_LogsWarningAndNoOps()
        {
            _manager.Activate("a");
            Pump();

            LogAssert.Expect(LogType.Warning, new Regex("未知相机 id.*"));
            _manager.Activate("missing");

            Assert.AreEqual("a", _manager.ActiveHandle.NamedId);
        }

        [Test]
        public void Deactivate_ActiveCamera_LetsBrainPickNext()
        {
            _manager.Activate("a");
            Pump();

            _manager.Deactivate("a");
            Pump();

            Assert.IsFalse(_manager.IsActive("a"));
            Assert.AreEqual("b", _manager.ActiveHandle.NamedId);
        }

        #endregion

        #region Transition

        [Test]
        public void TransitionTo_BlendsAndCompletes()
        {
            _manager.Activate("a");
            Pump();

            _manager.TransitionTo("b", 0.2f, CameraBlendStyle.Linear);
            Pump(0.1f);
            Assert.IsTrue(_manager.IsTransitioning);
            Assert.AreEqual("b", _manager.ActiveHandle.NamedId);

            for (var i = 0; i < 4 && _manager.IsTransitioning; i++)
                Pump(0.05f);

            Assert.IsFalse(_manager.IsTransitioning);
            Assert.AreEqual("b", _manager.ActiveHandle.NamedId);
            Assert.IsTrue(_manager.IsActive("b"));
            Assert.IsFalse(_manager.IsActive("a"));
        }

        [Test]
        public void TransitionTo_ZeroDuration_Cuts()
        {
            _manager.Activate("a");
            Pump();

            _manager.TransitionTo("b", 0f);
            Pump();

            Assert.IsFalse(_manager.IsTransitioning);
            Assert.AreEqual("b", _manager.ActiveHandle.NamedId);
        }

        [Test]
        public void TransitionTo_UnknownId_LogsWarningAndNoOps()
        {
            _manager.Activate("a");
            Pump();

            LogAssert.Expect(LogType.Warning, new Regex("未知相机 id.*"));
            _manager.TransitionTo("missing", 1f);

            Assert.AreEqual("a", _manager.ActiveHandle.NamedId);
        }

        #endregion

        #region Registry / Lifetime

        [Test]
        public void RegisterAndUnregister_UpdateRegistry()
        {
            var extraObject = new GameObject("VcamC");
            var extra = extraObject.AddComponent<CinemachineCamera>();

            try
            {
                _manager.Register("c", extra);
                CollectionAssert.Contains(_manager.CameraIds, "c");

                _manager.Activate("c");
                Pump();
                Assert.AreEqual("c", _manager.ActiveHandle.NamedId);

                Assert.IsTrue(_manager.Unregister("c"));
                CollectionAssert.DoesNotContain(_manager.CameraIds, "c");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(extraObject);
            }
        }

        [Test]
        public void RegisterDynamic_Activate_Unregister()
        {
            var extraObject = new GameObject("VcamC");
            var extra = extraObject.AddComponent<CinemachineCamera>();

            try
            {
                var handle = _manager.Register(extra);
                Assert.IsFalse(handle.IsDefault);

                _manager.Activate(handle);
                Pump();

                Assert.AreEqual(handle, _manager.ActiveHandle);
                Assert.IsNull(_manager.ActiveHandle.NamedId);
                Assert.AreSame(_camera, _manager.OutputCamera);
                Assert.IsTrue(_manager.IsActive(handle));
                Assert.AreEqual(int.MaxValue, extra.Priority.Value);

                Assert.IsTrue(_manager.Unregister(handle));
                Assert.IsFalse(_manager.IsActive(handle));
                Assert.IsFalse(_manager.Unregister(handle));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(extraObject);
            }
        }

        [Test]
        public void TransitionTo_DynamicHandle_BlendsAndCompletes()
        {
            _manager.Activate("a");
            Pump();

            var extraObject = new GameObject("VcamC");
            var extra = extraObject.AddComponent<CinemachineCamera>();

            try
            {
                var handle = _manager.Register(extra);
                _manager.TransitionTo(handle, 0.2f, CameraBlendStyle.Linear);
                Pump(0.1f);

                Assert.IsTrue(_manager.IsTransitioning);
                Assert.AreEqual(handle, _manager.ActiveHandle);
                Assert.IsNull(_manager.ActiveHandle.NamedId);

                for (var i = 0; i < 4 && _manager.IsTransitioning; i++)
                    Pump(0.05f);

                Assert.IsFalse(_manager.IsTransitioning);
                Assert.AreEqual(handle, _manager.ActiveHandle);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(extraObject);
            }
        }

        [Test]
        public void Activate_WhenBrainActiveIsUnregistered_LogsWarning()
        {
            _manager.Activate("a");
            Pump();

            var extraObject = new GameObject("ExternalVcam");
            var extra = extraObject.AddComponent<CinemachineCamera>();

            try
            {
                extra.Priority = int.MaxValue;
                extra.Prioritize();
                _manager.Deactivate("a");
                Pump();

                Assert.IsNull(_manager.ActiveHandle.NamedId);
                Assert.IsTrue(_manager.ActiveHandle.IsDefault);
                Assert.AreSame(_camera, _manager.OutputCamera);

                LogAssert.Expect(LogType.Warning, new Regex("未在 CameraManager 注册.*"));
                _manager.Activate("b");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(extraObject);
            }
        }

        [Test]
        public void RegisterDynamic_WrongType_Throws()
        {
            Assert.Throws<ArgumentException>(() => _manager.Register<object>(new object()));
            Assert.Throws<ArgumentNullException>(
                () => _manager.Register<CinemachineVirtualCameraBase>(null));
        }

        [Test]
        public void Dispose_ThrowsOnUse()
        {
            _manager.Dispose();

            Assert.Throws<ObjectDisposedException>(() => _manager.Activate("a"));
        }

        [Test]
        public void NullId_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _manager.Activate(null));
            Assert.Throws<ArgumentNullException>(() => _manager.Deactivate(null));
            Assert.Throws<ArgumentNullException>(() => _manager.TransitionTo(null, 1f));
        }

        #endregion
    }
}
