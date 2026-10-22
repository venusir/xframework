using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;

namespace XFramework.XCamera.Cinemachine.Tests
{
    /// <summary>
    /// 诊断快照：<c>GetState()</c> 与 <c>CopyCameras()</c> 的计数 / 活动 / 过渡 / 存活信息。
    /// </summary>
    [TestFixture]
    public class CameraDiagnosticsTests
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
        private CameraHandle _handleA;
        private CameraHandle _handleB;
        private int _frame;

        [SetUp]
        public void SetUp()
        {
            CameraManager.Destroy();
            CinemachineCore.GetBlendOverride = null;

            _cameraObject = new GameObject("Camera");
            _camera = _cameraObject.AddComponent<Camera>();
            _brain = _cameraObject.AddComponent<CinemachineBrain>();
            _brain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;

            _vcamAObject = new GameObject("VcamA");
            _vcamA = _vcamAObject.AddComponent<CinemachineCamera>();
            _vcamBObject = new GameObject("VcamB");
            _vcamB = _vcamBObject.AddComponent<CinemachineCamera>();

            _manager = new CinemachineCameraManager(_brain);
            _handleA = _manager.Register("a", _vcamA);
            _handleB = _manager.Register("b", _vcamB);
            _frame = 0;
        }

        [TearDown]
        public void TearDown()
        {
            _manager?.Dispose();
            CameraManager.Destroy();
            CinemachineCore.GetBlendOverride = null;

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

        private static CameraInfo Find(List<CameraInfo> buffer, CameraHandle handle)
        {
            for (var i = 0; i < buffer.Count; i++)
            {
                if (buffer[i].Handle == handle)
                    return buffer[i];
            }

            Assert.Fail("buffer 中找不到句柄 " + handle);
            return default;
        }

        #endregion

        #region GetState

        [Test]
        public void GetState_CountsAndActiveAndTransition()
        {
            _manager.Activate(_handleA);
            Pump();

            var idle = _manager.GetState();
            Assert.AreEqual(2, idle.RegisteredCount);
            Assert.AreEqual(2, idle.NamedCount);
            Assert.AreEqual(0, idle.DynamicCount);
            Assert.AreEqual(_handleA, idle.ActiveHandle);
            Assert.AreEqual("a", idle.ActiveCameraId);
            Assert.IsFalse(idle.IsTransitioning);
            Assert.IsFalse(idle.IsBrainBlending);
            Assert.AreEqual(0f, idle.TransitionProgress);
            Assert.AreSame(_camera, idle.OutputCamera);

            _manager.TransitionTo(_handleB, 0.2f, CameraBlendStyle.Linear);
            Pump(0.1f);

            var blending = _manager.GetState();
            Assert.IsTrue(blending.IsTransitioning);
            Assert.IsTrue(blending.IsBrainBlending);
            Assert.AreEqual(_handleB, blending.TransitionTarget);
            Assert.Greater(blending.TransitionProgress, 0f);
            Assert.LessOrEqual(blending.TransitionProgress, 1f);
        }

        [Test]
        public void GetState_NoCameras_ReturnsEmptyCounts()
        {
            var emptyObject = new GameObject("EmptyCamera");
            var emptyBrain = emptyObject.AddComponent<CinemachineBrain>();
            emptyBrain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;

            var empty = new CinemachineCameraManager(emptyBrain);
            try
            {
                var state = empty.GetState();

                Assert.AreEqual(0, state.RegisteredCount);
                Assert.AreEqual(0, state.NamedCount);
                Assert.AreEqual(0, state.DynamicCount);
                Assert.IsTrue(state.ActiveHandle.IsDefault);
                Assert.IsFalse(state.IsTransitioning);
                Assert.IsFalse(state.IsBrainBlending);
            }
            finally
            {
                empty.Dispose();
                UnityEngine.Object.DestroyImmediate(emptyObject);
            }
        }

        #endregion

        #region CopyCameras

        [Test]
        public void CopyCameras_ClearsBufferAndIncludesNamedDynamic()
        {
            _manager.Activate(_handleA);
            Pump();

            var extraObject = new GameObject("VcamC");
            var extra = extraObject.AddComponent<CinemachineCamera>();
            try
            {
                var handleC = _manager.Register(extra);
                var buffer = new List<CameraInfo>
                {
                    new CameraInfo(new CameraHandle(99, 1), false, false, true),
                };

                var count = _manager.CopyCameras(buffer);

                Assert.AreEqual(3, count);
                Assert.AreEqual(3, buffer.Count, "CopyCameras 必须先清空 buffer");

                var a = Find(buffer, _handleA);
                Assert.IsTrue(a.IsNamed);
                Assert.IsTrue(a.IsAlive);
                Assert.IsTrue(a.IsActive);

                var b = Find(buffer, _handleB);
                Assert.IsTrue(b.IsNamed);
                Assert.IsTrue(b.IsAlive);
                Assert.IsFalse(b.IsActive);

                var c = Find(buffer, handleC);
                Assert.IsFalse(c.IsNamed);
                Assert.IsTrue(c.IsAlive);

                UnityEngine.Object.DestroyImmediate(extraObject);

                var deadCount = _manager.CopyCameras(buffer);
                Assert.AreEqual(3, deadCount, "外部销毁的槽位仍占位，直到 Unregister");
                Assert.IsFalse(Find(buffer, handleC).IsAlive);
                Assert.IsFalse(Find(buffer, handleC).IsNamed);
            }
            finally
            {
                if (extraObject != null)
                    UnityEngine.Object.DestroyImmediate(extraObject);
            }
        }

        [Test]
        public void CopyCameras_TransitionTarget_IsFlagged()
        {
            _manager.Activate(_handleA);
            Pump();
            _manager.TransitionTo(_handleB, 0.2f, CameraBlendStyle.Linear);
            Pump(0.1f);

            var buffer = new List<CameraInfo>();
            _manager.CopyCameras(buffer);

            Assert.IsTrue(Find(buffer, _handleB).IsTransitionTarget);
            Assert.IsFalse(Find(buffer, _handleA).IsTransitionTarget);
        }

        [Test]
        public void CopyCameras_NullBuffer_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _manager.CopyCameras(null));
        }

        #endregion

        #region Facade

        [Test]
        public void Facade_GetStateAndCopyCameras_Forward()
        {
            CameraManager.Initialize(_manager);
            try
            {
                _manager.Activate(_handleA);
                Pump();

                var state = CameraManager.GetState();
                Assert.AreEqual(2, state.RegisteredCount);
                Assert.AreEqual(_handleA, state.ActiveHandle);

                var buffer = new List<CameraInfo>();
                Assert.AreEqual(2, CameraManager.CopyCameras(buffer));
                Assert.AreEqual(2, buffer.Count);
            }
            finally
            {
                CameraManager.Destroy();
                // 门面 Destroy 已 Dispose 管理器；TearDown 的第二次 Dispose 幂等。
            }
        }

        #endregion
    }
}
