using System.Collections.Generic;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using XFramework.XCamera.Cinemachine;

namespace XFramework.XCamera.Tests
{
    /// <summary>
    /// 诊断快照：计数 / 栈 / 活动 / 过渡 / 外部接管，以及 <c>CopyCameras</c> 的标志。
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

            CameraManager.Initialize(_brain);
            CameraManager.Register("a", _vcamA);
            CameraManager.Register("b", _vcamB);
            _frame = 0;
        }

        [TearDown]
        public void TearDown()
        {
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

        #endregion

        #region GetState

        [Test]
        public void GetState_CountsBaseActiveAndStack()
        {
            CameraManager.SwitchTo("a");
            Pump();

            var state = CameraManager.GetState();

            Assert.AreEqual(2, state.RegisteredCount);
            Assert.AreEqual(2, state.NamedCount);
            Assert.AreEqual(0, state.GeneratedCount);
            Assert.AreEqual("a", state.BaseCameraId);
            Assert.AreEqual("a", state.ActiveCameraId);
            Assert.IsTrue(state.HasActiveCamera);
            Assert.AreEqual(1, state.StackDepth);
            Assert.IsFalse(state.IsTransitioning);
            Assert.IsNull(state.TransitionTargetId);
            Assert.IsFalse(state.IsBrainBlending);
            Assert.IsFalse(state.IsExternallyControlled);
            Assert.AreSame(_camera, state.OutputCamera);
        }

        [Test]
        public void FacadeProbes_ReflectRealBackend()
        {
            Assert.IsFalse(CameraManager.HasActiveCamera);
            Assert.IsFalse(CameraManager.IsBlending);
            Assert.IsNull(CameraManager.ActiveCamera);
            Assert.AreSame(_camera, CameraManager.OutputCamera,
                "Brain 的 OutputCamera 在 Awake 就已解析");

            CameraManager.SwitchTo("a");
            Pump();

            Assert.IsTrue(CameraManager.HasActiveCamera);
            Assert.IsFalse(CameraManager.IsBlending);
            Assert.AreSame(_vcamA, CameraManager.ActiveCamera);
            Assert.AreSame(_camera, CameraManager.OutputCamera);

            CameraManager.Push("b", 0.4f, CameraBlendStyle.Linear);
            Pump(0.1f);

            Assert.IsTrue(CameraManager.IsBlending);
            Assert.AreSame(_vcamB, CameraManager.ActiveCamera,
                "混合期间活动相机是 incoming");
            Assert.AreSame(_camera, CameraManager.OutputCamera);
        }

        [Test]
        public void GetState_ReportsManagerTransitionAndProgress()
        {
            CameraManager.SwitchTo("a");
            Pump();

            CameraManager.Push("b", 0.4f, CameraBlendStyle.Linear);
            Pump(0.2f);

            var state = CameraManager.GetState();
            Assert.IsTrue(state.IsTransitioning);
            Assert.AreEqual("b", state.TransitionTargetId);
            Assert.IsTrue(state.IsBrainBlending);
            Assert.Greater(state.TransitionProgress, 0f);
            Assert.Less(state.TransitionProgress, 1f);
            Assert.AreEqual("b", state.ActiveCameraId);
            Assert.AreEqual(2, state.StackDepth);
            Assert.AreEqual("a", state.BaseCameraId);

            for (var i = 0; i < 8 && CameraManager.GetState().IsTransitioning; i++)
                Pump(0.05f);

            state = CameraManager.GetState();
            Assert.IsFalse(state.IsTransitioning);
            Assert.IsNull(state.TransitionTargetId);
            Assert.AreEqual("b", state.ActiveCameraId);
        }

        [Test]
        public void GetState_ExternalBlend_ReportsBrainBlendingNotManagerTransition()
        {
            CameraManager.SwitchTo("a");
            Pump();

            var externalObject = new GameObject("External", typeof(CinemachineCamera));
            var external = externalObject.GetComponent<CinemachineCamera>();
            external.Priority = 100;
            try
            {
                Pump(0.1f);

                var state = CameraManager.GetState();
                Assert.IsTrue(state.IsBrainBlending, "Brain 的真实混合应可见");
                Assert.IsFalse(state.IsTransitioning, "管理器过渡只统计本管理器发起的请求");
                Assert.IsNull(state.ActiveCameraId, "外部未注册相机没有 id");
                Assert.IsTrue(state.HasActiveCamera);
                Assert.IsTrue(state.IsExternallyControlled,
                    "活动相机不是栈顶且无在途请求 → 外部接管");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(externalObject);
            }
        }

        [Test]
        public void GetState_EmptyBackend_ReturnsEmptyCounts()
        {
            CameraManager.Destroy();
            CameraManager.Initialize(_brain);

            var state = CameraManager.GetState();

            Assert.AreEqual(0, state.RegisteredCount);
            Assert.AreEqual(0, state.NamedCount);
            Assert.AreEqual(0, state.GeneratedCount);
            Assert.IsNull(state.BaseCameraId);
            Assert.IsNull(state.ActiveCameraId);
            Assert.AreEqual(0, state.StackDepth);
            Assert.IsFalse(state.IsExternallyControlled);
        }

        #endregion

        #region CopyCameras

        [Test]
        public void CopyCameras_ClearsBufferAndFlagsStack()
        {
            CameraManager.SwitchTo("a");
            Pump();
            CameraManager.Push("b");
            Pump();

            var buffer = new List<CameraInfo> { default, default, default };
            Assert.AreEqual(2, CameraManager.CopyCameras(buffer));
            Assert.AreEqual(2, buffer.Count);

            var a = buffer[0];
            Assert.AreEqual("a", a.CameraId);
            Assert.IsTrue(a.IsBase);
            Assert.IsTrue(a.IsInStack);
            Assert.IsFalse(a.IsActive);
            Assert.IsFalse(a.IsTransitionTarget);
            Assert.IsTrue(a.IsAlive);
            Assert.IsFalse(a.IsGenerated);

            var b = buffer[1];
            Assert.AreEqual("b", b.CameraId);
            Assert.IsFalse(b.IsBase);
            Assert.IsTrue(b.IsInStack);
            Assert.IsTrue(b.IsActive);
            Assert.IsTrue(b.IsAlive);
        }

        [Test]
        public void CopyCameras_GeneratedCameraIsFlagged()
        {
            var dynamicObject = new GameObject("Dynamic", typeof(CinemachineCamera));
            try
            {
                var dynamicCamera = dynamicObject.GetComponent<CinemachineCamera>();
                var id = CameraManager.Register(dynamicCamera);

                var buffer = new List<CameraInfo>();
                CameraManager.CopyCameras(buffer);

                var info = buffer.Find(item => item.CameraId == id);
                Assert.IsTrue(info.IsGenerated);
                Assert.IsTrue(info.IsAlive);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(dynamicObject);
            }
        }

        [Test]
        public void CopyCameras_DeadSlot_IsNotAlive()
        {
            var deadObject = new GameObject("Dead", typeof(CinemachineCamera));
            var dead = deadObject.GetComponent<CinemachineCamera>();
            CameraManager.Register("dead", dead);
            UnityEngine.Object.DestroyImmediate(deadObject);

            var buffer = new List<CameraInfo>();
            CameraManager.CopyCameras(buffer);

            var info = buffer.Find(item => item.CameraId == "dead");
            Assert.IsFalse(info.IsAlive);
            Assert.AreEqual(3, buffer.Count, "已销毁但未注销的槽位仍计入");
        }

        [Test]
        public void CopyCameras_Uninitialized_ClearsBuffer()
        {
            CameraManager.Destroy();
            var buffer = new List<CameraInfo> { default };

            Assert.AreEqual(0, CameraManager.CopyCameras(buffer));
            Assert.IsEmpty(buffer);
            Assert.AreEqual(0, CameraManager.GetState().RegisteredCount);
        }

        #endregion
    }
}
