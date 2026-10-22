using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.TestTools;

namespace XFramework.XCamera.Cinemachine.Tests
{
    /// <summary>
    /// <see cref="CinemachineCameraManager"/> 的行为：统一句柄注册 / 优先级激活 / Brain 过渡 / 外部销毁。
    /// <para>Brain 用 <see cref="CinemachineBrain.UpdateMethods.ManualUpdate"/> + <c>ManualUpdate(frame, delta)</c> 确定性推进，
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
        private CameraHandle _handleA;
        private CameraHandle _handleB;
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

            _manager = new CinemachineCameraManager(_brain);
            _handleA = _manager.Register("a", _vcamA);
            _handleB = _manager.Register("b", _vcamB);
            _frame = 0;
        }

        [TearDown]
        public void TearDown()
        {
            _manager?.Dispose();
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
            _manager.Activate(_handleA);
            Pump();

            Assert.AreEqual("a", _manager.ActiveHandle.NamedId);
            Assert.AreSame(_camera, _manager.OutputCamera);
            Assert.IsTrue(_manager.IsActive(_handleA));
            Assert.IsFalse(_manager.IsTransitioning);
            Assert.Greater(_vcamA.Priority.Value, _vcamB.Priority.Value,
                "激活只提升目标，不把其它相机压成 0");
        }

        [Test]
        public void Activate_UnknownHandle_LogsWarningAndNoOps()
        {
            _manager.Activate(_handleA);
            Pump();

            LogAssert.Expect(LogType.Warning, new Regex("相机句柄无效或已注销.*"));
            _manager.Activate(new CameraHandle(99, 1));

            Assert.AreEqual("a", _manager.ActiveHandle.NamedId);
        }

        [Test]
        public void Deactivate_ActiveCamera_LetsBrainPickNext()
        {
            _manager.Activate(_handleA);
            Pump();

            _manager.Deactivate(_handleA);
            Pump();

            Assert.IsFalse(_manager.IsActive(_handleA));
            Assert.AreEqual("b", _manager.ActiveHandle.NamedId);
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
                _manager.Activate(_handleA);
                Pump();

                Assert.IsTrue(_manager.IsActive(_handleA),
                    "管理器必须把目标提升到场景内同通道最大优先级之上");
                Assert.AreEqual(10, external.Priority.Value,
                    "管理器不得改写未注册相机的优先级");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(external.gameObject);
            }
        }

        [Test]
        public void Deactivate_NoAlternative_DoesNotThrow()
        {
            _manager.Activate(_handleA);
            Pump();

            _manager.Deactivate(_handleB); // 从未激活的相机
            Pump();

            Assert.IsTrue(_manager.IsActive(_handleA));
        }

        #endregion

        #region Transition

        [Test]
        public void TransitionTo_BlendsAndCompletes()
        {
            _manager.Activate(_handleA);
            Pump();

            _manager.TransitionTo(_handleB, 0.2f, CameraBlendStyle.Linear);
            Pump(0.1f);

            Assert.IsTrue(_manager.IsTransitioning);
            Assert.AreEqual("b", _manager.ActiveHandle.NamedId);
            Assert.IsNotNull(_brain.ActiveBlend);
            Assert.AreEqual(0.2f, _brain.ActiveBlend.Duration, 0.0001f,
                "管理器请求的 duration 应通过 GetBlendOverride 生效");

            for (var i = 0; i < 6 && _manager.IsTransitioning; i++)
                Pump(0.05f);

            Assert.IsFalse(_manager.IsTransitioning);
            Assert.AreEqual("b", _manager.ActiveHandle.NamedId);
            Assert.IsTrue(_manager.IsActive(_handleB));
            Assert.IsFalse(_manager.IsActive(_handleA));
        }

        [Test]
        public void TransitionTo_ZeroDuration_Cuts()
        {
            _manager.Activate(_handleA);
            Pump();

            _manager.TransitionTo(_handleB, 0f);
            Pump();

            Assert.IsFalse(_manager.IsTransitioning);
            Assert.AreEqual("b", _manager.ActiveHandle.NamedId);
        }

        /// <summary>
        /// 强制完成混合（R3-CIN-001）：从 A 过渡到 B 的中途再次 Activate(B)，必须立即结束混合，
        /// 而不是让原先的过渡按剩余时长继续。上游官方入口是 <c>CinemachineBrain.ActiveBlend = null</c>。
        /// </summary>
        [Test]
        public void Activate_DuringBlendToSameTarget_ForcesCutImmediately()
        {
            _manager.Activate(_handleA);
            Pump();

            _manager.TransitionTo(_handleB, 2f, CameraBlendStyle.Linear);
            Pump(0.1f);

            Assert.IsTrue(_brain.IsBlending, "前提：应处于 A→B 的混合中");
            Assert.AreSame(_vcamB, _brain.ActiveVirtualCamera);

            _manager.Activate(_handleB);
            Pump();

            Assert.IsFalse(_brain.IsBlending, "Activate 同一目标必须强制完成混合");
            Assert.IsNull(_brain.ActiveBlend);
            Assert.IsFalse(_manager.IsTransitioning);
            Assert.AreEqual("b", _manager.ActiveHandle.NamedId);
        }

        [Test]
        public void TransitionTo_UnknownHandle_LogsWarningAndNoOps()
        {
            _manager.Activate(_handleA);
            Pump();

            LogAssert.Expect(LogType.Warning, new Regex("相机句柄无效或已注销.*"));
            _manager.TransitionTo(new CameraHandle(99, 1), 1f);

            Assert.AreEqual("a", _manager.ActiveHandle.NamedId);
        }

        [Test]
        public void TransitionTo_DoesNotModifyBrainDefaultBlend()
        {
            var before = _brain.DefaultBlend;

            _manager.Activate(_handleA);
            Pump();
            _manager.TransitionTo(_handleB, 0.2f, CameraBlendStyle.Linear);
            Pump(0.1f);

            Assert.AreEqual(before.Style, _brain.DefaultBlend.Style,
                "管理器通过 GetBlendOverride 请求本次混合，不得改写 Brain.DefaultBlend");
            Assert.AreEqual(before.Time, _brain.DefaultBlend.Time, 0.0001f);
        }

        [Test]
        public void Activate_DoesNotModifyBrainDefaultBlend()
        {
            var before = _brain.DefaultBlend;

            _manager.Activate(_handleA);
            Pump();

            Assert.AreEqual(before.Style, _brain.DefaultBlend.Style);
            Assert.AreEqual(before.Time, _brain.DefaultBlend.Time, 0.0001f);
        }

        [Test]
        public void IsTransitioning_ExternalBlend_DoesNotSet()
        {
            _manager.Activate(_handleA);
            Pump();

            var external = CreateVcam("External", 10);
            try
            {
                external.Prioritize();
                Pump();

                Assert.IsTrue(_brain.IsBlending || ReferenceEquals(_brain.ActiveVirtualCamera, external),
                    "前提：外部相机接管");
                Assert.IsFalse(_manager.IsTransitioning,
                    "外部 / Timeline 混合不得污染管理器自有的过渡状态");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(external.gameObject);
            }
        }

        [Test]
        public void TransitionTo_NonFiniteDuration_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _manager.TransitionTo(_handleB, float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => _manager.TransitionTo(_handleB, float.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => _manager.TransitionTo(_handleB, float.NegativeInfinity));
        }

        #endregion

        #region Registry / Lifetime

        [Test]
        public void RegisterAndUnregister_UpdateRegistry()
        {
            var extra = CreateVcam("VcamC");

            try
            {
                var handle = _manager.Register("c", extra);
                Assert.IsTrue(_manager.TryGetHandle("c", out var resolved));
                Assert.AreEqual(handle, resolved);
                Assert.AreEqual(3, _manager.CameraCount);

                _manager.Activate(handle);
                Pump();
                Assert.AreEqual("c", _manager.ActiveHandle.NamedId);

                Assert.IsTrue(_manager.Unregister(handle));
                Assert.IsFalse(_manager.TryGetHandle("c", out _));
                Assert.AreEqual(2, _manager.CameraCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(extra.gameObject);
            }
        }

        [Test]
        public void Register_SameIdSameCamera_IsIdempotent()
        {
            var again = _manager.Register("a", _vcamA);

            Assert.AreEqual(_handleA, again);
            Assert.AreEqual(2, _manager.CameraCount);
        }

        [Test]
        public void Register_SameIdDifferentCamera_Throws()
        {
            var extra = CreateVcam("VcamC");
            try
            {
                Assert.Throws<ArgumentException>(() => _manager.Register("a", extra));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(extra.gameObject);
            }
        }

        [Test]
        public void Register_SameCameraDifferentId_Throws()
        {
            Assert.Throws<ArgumentException>(() => _manager.Register("c", _vcamA));
        }

        [Test]
        public void Register_DynamicForNamedCamera_Throws()
        {
            Assert.Throws<ArgumentException>(() => _manager.Register(_vcamA));
        }

        [Test]
        public void Register_DynamicTwice_ReturnsSameHandle()
        {
            var extra = CreateVcam("VcamC");
            try
            {
                var first = _manager.Register(extra);
                var second = _manager.Register(extra);

                Assert.AreEqual(first, second);
                Assert.IsNull(first.NamedId);
                Assert.AreEqual(3, _manager.CameraCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(extra.gameObject);
            }
        }

        [Test]
        public void Register_DestroyedCamera_Throws()
        {
            var extra = CreateVcam("VcamC");
            var obj = extra.gameObject;
            UnityEngine.Object.DestroyImmediate(obj);

            Assert.Throws<ArgumentException>(() => _manager.Register("c", extra));
        }

        [Test]
        public void Register_AfterDestroyedSlot_AllocatesNewGeneration()
        {
            UnityEngine.Object.DestroyImmediate(_vcamAObject);

            var extra = CreateVcam("VcamC");
            try
            {
                var handle = _manager.Register("c", extra);

                Assert.IsFalse(handle.IsDefault);
                Assert.IsFalse(_manager.TryGetHandle("a", out _),
                    "已销毁的旧槽位应在注册新相机时被释放");
                Assert.IsTrue(_manager.TryGetHandle("c", out var resolved));
                Assert.AreEqual(handle, resolved);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(extra.gameObject);
            }
        }

        /// <summary>
        /// 槽位复用的 ABA 防线（R3-CIN-005）：注销后同一槽位被新相机复用、代际递增，
        /// 旧 <see cref="CameraHandle"/> 必须按代际失配失效，不能操作到新相机。
        /// </summary>
        [Test]
        public void DynamicHandle_AfterSlotReuse_OldHandleIsRejected()
        {
            var vcamC = CreateVcam("VcamC");
            var vcamD = CreateVcam("VcamD");

            try
            {
                var oldHandle = _manager.Register(vcamC);
                Assert.IsTrue(_manager.Unregister(oldHandle));

                var newHandle = _manager.Register(vcamD);
                Assert.AreEqual(oldHandle.Id, newHandle.Id, "前提：槽位应被复用");
                Assert.AreNotEqual(oldHandle.Generation, newHandle.Generation, "槽位复用必须递增代际");

                Assert.IsFalse(_manager.IsActive(oldHandle), "旧句柄必须按代际失配失效");

                LogAssert.Expect(LogType.Warning, new Regex("相机句柄无效或已注销.*"));
                _manager.Activate(oldHandle);
                Pump();

                Assert.IsFalse(_manager.IsActive(oldHandle));
                Assert.AreNotEqual(int.MaxValue, vcamD.Priority.Value,
                    "旧句柄不得操作到复用槽位的新相机");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(vcamC.gameObject);
                UnityEngine.Object.DestroyImmediate(vcamD.gameObject);
            }
        }

        [Test]
        public void RegisterDynamic_Activate_Unregister()
        {
            var extra = CreateVcam("VcamC");

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

                Assert.IsTrue(_manager.Unregister(handle));
                Assert.IsFalse(_manager.IsActive(handle));
                Assert.IsFalse(_manager.Unregister(handle));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(extra.gameObject);
            }
        }

        [Test]
        public void TransitionTo_DynamicHandle_BlendsAndCompletes()
        {
            _manager.Activate(_handleA);
            Pump();

            var extra = CreateVcam("VcamC");

            try
            {
                var handle = _manager.Register(extra);
                _manager.TransitionTo(handle, 0.2f, CameraBlendStyle.Linear);
                Pump(0.1f);

                Assert.IsTrue(_manager.IsTransitioning);
                Assert.AreEqual(handle, _manager.ActiveHandle);
                Assert.IsNull(_manager.ActiveHandle.NamedId);

                for (var i = 0; i < 6 && _manager.IsTransitioning; i++)
                    Pump(0.05f);

                Assert.IsFalse(_manager.IsTransitioning);
                Assert.AreEqual(handle, _manager.ActiveHandle);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(extra.gameObject);
            }
        }

        [Test]
        public void Activate_WhenBrainActiveIsUnregistered_LogsWarning()
        {
            _manager.Activate(_handleA);
            Pump();

            var external = CreateVcam("External", int.MaxValue);

            try
            {
                external.Prioritize();
                _manager.Deactivate(_handleA);
                Pump();

                Assert.IsTrue(_manager.ActiveHandle.IsDefault);
                Assert.AreSame(_camera, _manager.OutputCamera);

                LogAssert.Expect(LogType.Warning, new Regex("未在 CameraManager 注册.*"));
                _manager.Activate(_handleB);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(external.gameObject);
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
        public void NullId_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => _manager.Register<CinemachineCamera>(null, _vcamA));
            Assert.Throws<ArgumentNullException>(() => _manager.TryGetHandle(null, out _));
        }

        [Test]
        public void Dispose_ThrowsOnUse()
        {
            _manager.Dispose();

            Assert.Throws<ObjectDisposedException>(() => _manager.Activate(_handleA));
        }

        [Test]
        public void Dispose_RestoresAuthoredPriority()
        {
            var extra = CreateVcam("VcamC", 5);

            try
            {
                var handle = _manager.Register("c", extra);
                _manager.Activate(handle);
                Pump();

                Assert.Greater(extra.Priority.Value, 5);

                _manager.Dispose();

                Assert.AreEqual(5, extra.Priority.Value,
                    "Dispose 必须恢复注册时捕获的作者优先级");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(extra.gameObject);
            }
        }

        #endregion

        #region 被外部销毁的 vcam

        [Test]
        public void Activate_ExternallyDestroyedVcam_WarnsAndNoOps()
        {
            _manager.Activate(_handleB);
            Pump();
            UnityEngine.Object.DestroyImmediate(_vcamAObject);

            LogAssert.Expect(LogType.Warning, new Regex("已被外部销毁"));
            _manager.Activate(_handleA);

            Assert.AreEqual("b", _manager.ActiveHandle.NamedId,
                "激活已销毁的 vcam 不得改变活动相机");
        }

        [Test]
        public void Deactivate_ExternallyDestroyedVcam_WarnsAndNoOps()
        {
            _manager.Activate(_handleA);
            Pump();
            UnityEngine.Object.DestroyImmediate(_vcamAObject);

            LogAssert.Expect(LogType.Warning, new Regex("已被外部销毁"));
            Assert.DoesNotThrow(() => _manager.Deactivate(_handleA),
                "反激活已销毁的 vcam 不得抛 MissingReferenceException");
        }

        [Test]
        public void TransitionTo_ExternallyDestroyedVcam_WarnsAndNoOps()
        {
            _manager.Activate(_handleB);
            Pump();
            UnityEngine.Object.DestroyImmediate(_vcamAObject);

            LogAssert.Expect(LogType.Warning, new Regex("已被外部销毁"));
            _manager.TransitionTo(_handleA, 1f);

            Assert.AreEqual("b", _manager.ActiveHandle.NamedId,
                "过渡到已销毁的 vcam 必须整体忽略");
        }

        [Test]
        public void HandleChangeSurface_ExternallyDestroyedVcam_WarnsAndNoOps()
        {
            _manager.Activate(_handleB);
            Pump();

            var extra = CreateVcam("VcamC");
            var handle = _manager.Register(extra);

            try
            {
                UnityEngine.Object.DestroyImmediate(extra.gameObject);

                LogAssert.Expect(LogType.Warning, new Regex("已被外部销毁"));
                _manager.Activate(handle);

                LogAssert.Expect(LogType.Warning, new Regex("已被外部销毁"));
                _manager.TransitionTo(handle, 1f);

                LogAssert.Expect(LogType.Warning, new Regex("已被外部销毁"));
                Assert.DoesNotThrow(() => _manager.Deactivate(handle),
                    "反激活已销毁的 vcam 不得抛 MissingReferenceException");

                Assert.AreEqual("b", _manager.ActiveHandle.NamedId,
                    "句柄变更面在 vcam 销毁后必须全部 no-op");
            }
            finally
            {
                if (extra != null)
                    UnityEngine.Object.DestroyImmediate(extra.gameObject);
            }
        }

        [Test]
        public void DestroyedVcam_UnregisterRemovesSlot()
        {
            _manager.Activate(_handleA);
            Pump();
            UnityEngine.Object.DestroyImmediate(_vcamAObject);

            Assert.IsTrue(_manager.TryGetHandle("a", out _),
                "外部销毁的相机仍占槽位，直到 Unregister");
            Assert.AreEqual(2, _manager.CameraCount);

            Assert.IsTrue(_manager.Unregister(_handleA));
            Assert.IsFalse(_manager.TryGetHandle("a", out _));
            Assert.AreEqual(1, _manager.CameraCount);
        }

        #endregion
    }
}
