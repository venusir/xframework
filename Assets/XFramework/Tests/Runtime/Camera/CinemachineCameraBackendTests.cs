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
    /// <see cref="CinemachineCameraBackend"/> 的核心行为：注册 / 优先级激活 / 混合请求 / 外部销毁。
    /// <para>Brain 用 <see cref="CinemachineBrain.UpdateMethods.ManualUpdate"/> + <c>ManualUpdate(frame, delta)</c>
    /// 确定性推进，不依赖真实帧时长。</para>
    /// </summary>
    [TestFixture]
    public class CinemachineCameraBackendTests
    {
        #region Fixture

        private GameObject _cameraObject;
        private Camera _camera;
        private CinemachineBrain _brain;
        private GameObject _vcamAObject;
        private GameObject _vcamBObject;
        private CinemachineCamera _vcamA;
        private CinemachineCamera _vcamB;
        private CinemachineCameraBackend _backend;
        private int _frame;

        [SetUp]
        public void SetUp()
        {
            CinemachineCore.GetBlendOverride = null;

            _cameraObject = new GameObject("Camera");
            _camera = _cameraObject.AddComponent<Camera>();
            _brain = _cameraObject.AddComponent<CinemachineBrain>();
            _brain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;

            _vcamAObject = new GameObject("VcamA");
            _vcamA = _vcamAObject.AddComponent<CinemachineCamera>();
            _vcamBObject = new GameObject("VcamB");
            _vcamB = _vcamBObject.AddComponent<CinemachineCamera>();

            _backend = new CinemachineCameraBackend(_brain);
            Assert.IsTrue(_backend.Register("a", _vcamA, false));
            Assert.IsTrue(_backend.Register("b", _vcamB, false));
            _frame = 0;
        }

        [TearDown]
        public void TearDown()
        {
            _backend?.Dispose();
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

        private CinemachineCamera CreateVcam(string name, int priority = 0)
        {
            var obj = new GameObject(name);
            var vcam = obj.AddComponent<CinemachineCamera>();
            vcam.Priority = priority;
            return vcam;
        }

        #endregion

        #region Activation

        [Test]
        public void Activate_MakesTargetActive()
        {
            _backend.Activate("a", 0f, CameraBlendStyle.Cut);
            Pump();

            Assert.AreEqual("a", _backend.ActiveCameraId);
            Assert.IsTrue(_backend.HasActiveCamera);
            Assert.AreSame(_camera, _backend.OutputCamera);
            Assert.IsFalse(_backend.IsTransitioning);
            Assert.IsNull(_backend.TransitionTargetId);
            Assert.Greater(_vcamA.Priority.Value, _vcamB.Priority.Value,
                "激活只提升目标，不把其它相机压成 0");
        }

        [Test]
        public void Activate_BoostsAboveUnregisteredHighPriorityCamera()
        {
            var external = CreateVcam("External", 10);
            try
            {
                Pump();
                Assert.AreSame(external, _brain.ActiveVirtualCamera,
                    "前提：外部高优先级相机应先成为活动相机");

                LogAssert.Expect(LogType.Warning, new Regex("未在 CameraManager 注册.*"));
                _backend.Activate("a", 0f, CameraBlendStyle.Cut);
                Pump();

                Assert.AreEqual("a", _backend.ActiveCameraId,
                    "后端必须把目标提升到场景内同通道最大优先级之上");
                Assert.AreEqual(10, external.Priority.Value,
                    "后端不得改写未注册相机的优先级");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(external.gameObject);
            }
        }

        [Test]
        public void Activate_UnknownId_LogsWarningAndNoOps()
        {
            _backend.Activate("a", 0f, CameraBlendStyle.Cut);
            Pump();

            LogAssert.Expect(LogType.Warning, new Regex("未知相机 id: missing.*"));
            _backend.Activate("missing", 0f, CameraBlendStyle.Cut);

            Assert.AreEqual("a", _backend.ActiveCameraId);
        }

        [Test]
        public void Activate_ExternallyDestroyedCamera_WarnsAndNoOps()
        {
            var deadObject = new GameObject("Dead", typeof(CinemachineCamera));
            var dead = deadObject.GetComponent<CinemachineCamera>();
            _backend.Register("dead", dead, false);
            UnityEngine.Object.DestroyImmediate(deadObject);

            LogAssert.Expect(LogType.Warning, new Regex("已被外部销毁.*"));
            _backend.Activate("dead", 0f, CameraBlendStyle.Cut);

            Assert.IsTrue(_backend.IsRegistered("dead"));
            Assert.IsFalse(_backend.TryGetCamera("dead", out _));
        }

        #endregion

        #region Transition

        [Test]
        public void Activate_Blend_ReportsTransitionAndCompletes()
        {
            _backend.Activate("a", 0f, CameraBlendStyle.Cut);
            Pump();

            _backend.Activate("b", 0.2f, CameraBlendStyle.Linear);
            Pump(0.1f);

            Assert.IsTrue(_backend.IsTransitioning);
            Assert.AreEqual("b", _backend.TransitionTargetId);
            Assert.AreEqual("b", _backend.ActiveCameraId);
            Assert.IsNotNull(_brain.ActiveBlend);
            Assert.AreEqual(0.2f, _brain.ActiveBlend.Duration, 0.0001f,
                "请求的 duration 应通过 GetBlendOverride 生效");
            Assert.Greater(_backend.TransitionProgress, 0f);

            for (var i = 0; i < 8 && _backend.IsTransitioning; i++)
                Pump(0.05f);

            Assert.IsFalse(_backend.IsTransitioning);
            Assert.IsNull(_backend.TransitionTargetId);
            Assert.AreEqual("b", _backend.ActiveCameraId);
        }

        [Test]
        public void Activate_ZeroDuration_Cuts()
        {
            _backend.Activate("a", 0f, CameraBlendStyle.Cut);
            Pump();

            _backend.Activate("b", 0f, CameraBlendStyle.EaseInOut);
            Pump();

            Assert.IsFalse(_backend.IsTransitioning);
            Assert.AreEqual("b", _backend.ActiveCameraId);
        }

        [Test]
        public void Activate_AlreadyActive_CompletesImmediately()
        {
            _backend.Activate("a", 0f, CameraBlendStyle.Cut);
            Pump();

            _backend.Activate("a", 0.5f, CameraBlendStyle.Linear);
            Pump();

            Assert.IsFalse(_backend.IsTransitioning);
            Assert.IsNull(_backend.TransitionTargetId);
            Assert.AreEqual("a", _backend.ActiveCameraId);
        }

        [Test]
        public void Activate_NonFiniteDuration_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _backend.Activate("a", float.NaN, CameraBlendStyle.Cut));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _backend.Activate("a", float.PositiveInfinity, CameraBlendStyle.Cut));
        }

        [Test]
        public void Transition_DoesNotModifyBrainDefaultBlend()
        {
            var before = _brain.DefaultBlend;

            _backend.Activate("a", 0f, CameraBlendStyle.Cut);
            Pump();
            _backend.Activate("b", 0.3f, CameraBlendStyle.Linear);
            Pump(0.1f);

            Assert.AreEqual(before.Style, _brain.DefaultBlend.Style);
            Assert.AreEqual(before.Time, _brain.DefaultBlend.Time, 0.0001f,
                "本次请求只经 GetBlendOverride，不得修改 DefaultBlend");
        }

        [Test]
        public void ExternalBlend_DoesNotSetManagerTransition()
        {
            _backend.Activate("a", 0f, CameraBlendStyle.Cut);
            Pump();

            var external = CreateVcam("ExternalBlend", 100);
            try
            {
                Pump(0.1f);

                Assert.IsTrue(_backend.IsBlending, "前提：Brain 应正在向外部相机混合");
                Assert.IsFalse(_backend.IsTransitioning,
                    "IsTransitioning 只统计本后端发起的非 cut 过渡");
                Assert.IsNull(_backend.ActiveCameraId,
                    "外部未注册相机解析不到 id");
                Assert.IsTrue(_backend.HasActiveCamera);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(external.gameObject);
            }
        }

        [Test]
        public void ExternalTakeover_KeepsManagerBoost_AndResumesAfterRelease()
        {
            _backend.Activate("a", 0f, CameraBlendStyle.Cut);
            Pump();
            var boosted = _vcamA.Priority.Value;
            Assert.Greater(boosted, 0);

            var externalObject = new GameObject("External", typeof(CinemachineCamera));
            var external = externalObject.GetComponent<CinemachineCamera>();
            external.Priority = boosted + 100;
            try
            {
                Pump(0.1f);
                Assert.AreSame(external, _brain.ActiveVirtualCamera,
                    "前提：外部高优先级相机接管");
                Assert.AreEqual(boosted, _vcamA.Priority.Value,
                    "外部接管时保持管理器的 boost，过场结束后才能回到游戏相机");

                UnityEngine.Object.DestroyImmediate(externalObject);
                Pump(0.1f);

                Assert.AreEqual("a", _backend.ActiveCameraId,
                    "过场释放后应回到已 boost 的栈顶");
            }
            finally
            {
                if (externalObject != null)
                    UnityEngine.Object.DestroyImmediate(externalObject);
            }
        }

        [Test]
        public void ActiveCameraId_ResolvesClearShotChildToRegisteredParent()
        {
            var parentObject = new GameObject("Group");
            var childObject = new GameObject("GroupChild");
            try
            {
                // 先建父相机、再建子相机：子相机 OnEnable 时才能缓存到 ParentCamera。
                var clearShot = parentObject.AddComponent<CinemachineClearShot>();
                childObject.transform.SetParent(parentObject.transform);
                var child = childObject.AddComponent<CinemachineCamera>();
                _backend.Register("group", clearShot, false);

                var overrideId = _brain.SetCameraOverride(-1, 100, null, child, 1f, 0f);
                try
                {
                    Pump();

                    Assert.AreEqual("group", _backend.ActiveCameraId,
                        "分组相机的活动子相机应向上解析到已注册的父相机 id");
                    Assert.IsTrue(_backend.HasActiveCamera);
                }
                finally
                {
                    _brain.ReleaseCameraOverride(overrideId);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parentObject);
            }
        }

        [Test]
        public void ClearActive_RestoresAuthoredPriority()
        {
            _backend.Activate("a", 0f, CameraBlendStyle.Cut);
            Pump();
            Assert.Greater(_vcamA.Priority.Value, 0);

            _backend.ClearActive();

            Assert.AreEqual(0, _vcamA.Priority.Value);
            Assert.IsFalse(_backend.IsTransitioning);
            Assert.IsNull(_backend.TransitionTargetId);
        }

        #endregion

        #region Bindings

        [Test]
        public void SetFollowAndLookAt_UpdateTargets()
        {
            var targetObject = new GameObject("Target");
            try
            {
                var target = targetObject.transform;

                _backend.SetFollow("a", target);
                _backend.SetLookAt("a", target);

                Assert.AreSame(target, _vcamA.Follow);
                Assert.AreSame(target, _vcamA.LookAt);

                _backend.SetFollow("a", null);
                Assert.IsNull(_vcamA.Follow);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(targetObject);
            }
        }

        [Test]
        public void SetFieldOfView_UpdatesLensAndOutput()
        {
            _backend.Activate("a", 0f, CameraBlendStyle.Cut);
            Pump();

            _backend.SetFieldOfView("a", 45f);
            Assert.AreEqual(45f, _vcamA.Lens.FieldOfView, 0.001f);

            Pump();
            Assert.AreEqual(45f, _camera.fieldOfView, 0.1f,
                "活动相机的 Lens 应被输出到 Unity Camera");
        }

        [Test]
        public void SetFieldOfView_Group_WarnsAndNoOps()
        {
            var parentObject = new GameObject("Group");
            var childObject = new GameObject("GroupChild");
            try
            {
                childObject.transform.SetParent(parentObject.transform);
                childObject.AddComponent<CinemachineCamera>();
                var clearShot = parentObject.AddComponent<CinemachineClearShot>();
                _backend.Register("group", clearShot, false);

                LogAssert.Expect(LogType.Warning, new Regex("分组相机没有 Lens.*"));
                Assert.DoesNotThrow(() => _backend.SetFieldOfView("group", 40f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parentObject);
            }
        }

        [Test]
        public void SetFieldOfView_Invalid_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _backend.SetFieldOfView("a", float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _backend.SetFieldOfView("a", 0f));
        }

        [Test]
        public void Bindings_UnknownOrDead_WarnAndNoOps()
        {
            var targetObject = new GameObject("Target");
            try
            {
                LogAssert.Expect(LogType.Warning, new Regex("未知相机 id: missing.*"));
                _backend.SetFollow("missing", targetObject.transform);

                var deadObject = new GameObject("Dead", typeof(CinemachineCamera));
                var dead = deadObject.GetComponent<CinemachineCamera>();
                _backend.Register("dead", dead, false);
                UnityEngine.Object.DestroyImmediate(deadObject);

                LogAssert.Expect(LogType.Warning, new Regex("已被外部销毁.*SetLookAt.*"));
                _backend.SetLookAt("dead", targetObject.transform);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(targetObject);
            }
        }

        [Test]
        public void Shake_CreatesSourceAndSetsDuration()
        {
            _backend.Activate("a", 0f, CameraBlendStyle.Cut);
            Pump();

            LogAssert.Expect(LogType.Warning, new Regex("缺少 CinemachineImpulseListener.*"));
            _backend.Shake(0.5f, 0.3f);

            var source = _cameraObject.GetComponent<CinemachineImpulseSource>();
            Assert.IsNotNull(source, "震屏应在输出对象上惰性创建 ImpulseSource");
            Assert.AreEqual(0.3f, source.ImpulseDefinition.ImpulseDuration, 0.0001f);

            _backend.Shake(0.6f, 0.4f);
            Assert.AreEqual(0.4f, source.ImpulseDefinition.ImpulseDuration, 0.0001f,
                "连续震屏以最后一次调用的时长为准");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Shake_WithListener_DoesNotWarn()
        {
            _vcamAObject.AddComponent<CinemachineImpulseListener>();
            _backend.Activate("a", 0f, CameraBlendStyle.Cut);
            Pump();

            _backend.Shake(0.5f, 0.2f);

            LogAssert.NoUnexpectedReceived();
            Assert.IsNotNull(_cameraObject.GetComponent<CinemachineImpulseSource>());
        }

        [Test]
        public void Shake_InvalidArguments_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _backend.Shake(float.NaN, 0.2f));
            Assert.Throws<ArgumentOutOfRangeException>(() => _backend.Shake(0f, 0.2f));
            Assert.Throws<ArgumentOutOfRangeException>(() => _backend.Shake(0.5f, float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => _backend.Shake(0.5f, 0f));
        }

        #endregion

        #region Registry

        [Test]
        public void Register_SameIdSameCamera_IsIdempotent()
        {
            Assert.IsFalse(_backend.Register("a", _vcamA, false));
            Assert.AreEqual(2, _backend.CameraCount);
        }

        [Test]
        public void Register_SameIdDifferentCamera_Throws()
        {
            Assert.Throws<ArgumentException>(() => _backend.Register("a", _vcamB, false));
        }

        [Test]
        public void Register_SameCameraDifferentId_Throws()
        {
            Assert.Throws<ArgumentException>(() => _backend.Register("other", _vcamA, false));
        }

        [Test]
        public void Register_WrongType_Throws()
        {
            var wrong = new GameObject("Wrong", typeof(Camera));
            try
            {
                Assert.Throws<ArgumentException>(
                    () => _backend.Register("wrong", wrong.GetComponent<Camera>(), false));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(wrong);
            }
        }

        [Test]
        public void Register_DestroyedCamera_Throws()
        {
            var obj = new GameObject("Destroyed", typeof(CinemachineCamera));
            var camera = obj.GetComponent<CinemachineCamera>();
            UnityEngine.Object.DestroyImmediate(obj);

            Assert.Throws<ArgumentException>(() => _backend.Register("dead", camera, false));
        }

        [Test]
        public void Register_NullArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => _backend.Register(null, _vcamA, false));
            Assert.Throws<ArgumentNullException>(() => _backend.Register("x", null, false));
        }

        [Test]
        public void Unregister_RemovesAndRestoresAuthoredState()
        {
            _backend.Activate("a", 0f, CameraBlendStyle.Cut);
            Pump();

            Assert.IsTrue(_backend.Unregister("a"));

            Assert.IsFalse(_backend.IsRegistered("a"));
            Assert.IsFalse(_backend.TryGetCamera("a", out _));
            Assert.AreEqual(0, _vcamA.Priority.Value);
            Assert.AreEqual(1, _backend.CameraCount);
        }

        [Test]
        public void Unregister_Unknown_ReturnsFalse()
        {
            Assert.IsFalse(_backend.Unregister("missing"));
        }

        [Test]
        public void DeadSlot_CountsUntilUnregistered()
        {
            var obj = new GameObject("Dead", typeof(CinemachineCamera));
            var camera = obj.GetComponent<CinemachineCamera>();
            _backend.Register("dead", camera, false);
            UnityEngine.Object.DestroyImmediate(obj);

            Assert.IsTrue(_backend.IsRegistered("dead"));
            Assert.IsFalse(_backend.TryGetCamera("dead", out _));
            Assert.AreEqual(3, _backend.CameraCount);

            Assert.IsTrue(_backend.Unregister("dead"));
            Assert.IsFalse(_backend.IsRegistered("dead"));
            Assert.AreEqual(2, _backend.CameraCount);
        }

        [Test]
        public void CopyCameraIds_InsertionOrderAndCounts()
        {
            var thirdObject = new GameObject("VcamC", typeof(CinemachineCamera));
            try
            {
                _backend.Register("c", thirdObject.GetComponent<CinemachineCamera>(), true);

                var buffer = new List<string>();
                Assert.AreEqual(3, _backend.CopyCameraIds(buffer));
                CollectionAssert.AreEqual(new[] { "a", "b", "c" }, buffer);
                Assert.AreEqual(2, _backend.NamedCount);
                Assert.AreEqual(1, _backend.GeneratedCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(thirdObject);
            }
        }

        #endregion

        #region Lifecycle

        [Test]
        public void Dispose_RestoresAuthoredStateAndOverride()
        {
            var installed = CinemachineCore.GetBlendOverride;
            Assert.IsNotNull(installed);

            _backend.Activate("a", 0f, CameraBlendStyle.Cut);
            Pump();

            _backend.Dispose();

            Assert.AreEqual(0, _vcamA.Priority.Value);
            Assert.IsNull(CinemachineCore.GetBlendOverride,
                "Dispose 应恢复构造时保存的前一个 override（此处为 null）");
        }

        [Test]
        public void Dispose_SecondBackend_RestoresFirstOverride()
        {
            var first = CinemachineCore.GetBlendOverride;
            var second = new CinemachineCameraBackend(_brain);

            second.Dispose();

            Assert.AreSame(first, CinemachineCore.GetBlendOverride);
        }

        [Test]
        public void SessionReset_DoesNotTouchSceneAndDetaches()
        {
            ((ICameraBackendSessionReset)_backend).UnbindSessionEvents();

            Assert.Throws<ObjectDisposedException>(() => _ = _backend.ActiveCameraId);
            Assert.Throws<ObjectDisposedException>(() => _backend.Activate("a", 0f, CameraBlendStyle.Cut));
            Assert.AreEqual(0, _vcamA.Priority.Value,
                "会话复位不得改写场景对象");
        }

        #endregion
    }
}
