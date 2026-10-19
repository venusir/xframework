using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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

        /// <summary>
        /// 强制完成混合（R3-CIN-001）：从 A 过渡到 B 的中途再次 Activate("b")，必须立即结束混合，
        /// 而不是让原先的过渡按剩余时长继续。上游官方入口是 <c>CinemachineBrain.ActiveBlend = null</c>。
        /// </summary>
        [Test]
        public void Activate_DuringBlendToSameTarget_ForcesCutImmediately()
        {
            _manager.Activate("a");
            Pump();

            _manager.TransitionTo("b", 2f, CameraBlendStyle.Linear);
            Pump(0.1f);

            Assert.IsTrue(_brain.IsBlending, "前提：应处于 A→B 的混合中");
            Assert.AreSame(_vcamB, _brain.ActiveVirtualCamera);

            _manager.Activate("b");
            Pump();

            Assert.IsFalse(_brain.IsBlending, "Activate 同一目标必须强制完成混合");
            Assert.IsNull(_brain.ActiveBlend);
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

        /// <summary>
        /// 槽位复用的 ABA 防线（R3-CIN-005）：注销后同一槽位被新相机复用、代际递增，
        /// 旧 <see cref="CameraHandle"/> 必须按代际失配失效，不能操作到新相机。
        /// </summary>
        [Test]
        public void DynamicHandle_AfterSlotReuse_OldHandleIsRejected()
        {
            var objC = new GameObject("VcamC");
            var vcamC = objC.AddComponent<CinemachineCamera>();
            var objD = new GameObject("VcamD");
            var vcamD = objD.AddComponent<CinemachineCamera>();

            try
            {
                var oldHandle = _manager.Register(vcamC);
                Assert.IsTrue(_manager.Unregister(oldHandle));

                var newHandle = _manager.Register(vcamD); // 复用同一槽位
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
                UnityEngine.Object.DestroyImmediate(objC);
                UnityEngine.Object.DestroyImmediate(objD);
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

        [Test]
        public void TransitionTo_NonFiniteDuration_Throws()
        {
            // 核心 UnityCameraManager 对 NaN / ±∞ 抛 ArgumentOutOfRangeException——同一接口的
            // 两种实现不得行为相反（CIN-002 / CAM-001）。
            Assert.Throws<ArgumentOutOfRangeException>(() => _manager.TransitionTo("b", float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => _manager.TransitionTo("b", float.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => _manager.TransitionTo("b", float.NegativeInfinity));
        }

        #endregion

        #region 被外部销毁的 vcam

        [Test]
        public void Activate_ExternallyDestroyedVcam_WarnsAndNoOps()
        {
            _manager.Activate("b");
            Pump();
            UnityEngine.Object.DestroyImmediate(_vcamAObject);

            LogAssert.Expect(LogType.Warning, new Regex("已被外部销毁"));
            _manager.Activate("a");

            Assert.AreEqual("b", _manager.ActiveHandle.NamedId,
                "激活已销毁的 vcam 不得改变活动相机（CIN-003）");
        }

        [Test]
        public void Deactivate_ExternallyDestroyedVcam_WarnsAndNoOps()
        {
            _manager.Activate("a");
            Pump();
            UnityEngine.Object.DestroyImmediate(_vcamAObject);

            LogAssert.Expect(LogType.Warning, new Regex("已被外部销毁"));
            Assert.DoesNotThrow(() => _manager.Deactivate("a"),
                "反激活已销毁的 vcam 不得抛 MissingReferenceException（CIN-003）");
        }

        [Test]
        public void TransitionTo_ExternallyDestroyedVcam_WarnsAndNoOps()
        {
            _manager.Activate("b");
            Pump();
            UnityEngine.Object.DestroyImmediate(_vcamAObject);

            LogAssert.Expect(LogType.Warning, new Regex("已被外部销毁"));
            _manager.TransitionTo("a", 1f);

            Assert.AreEqual("b", _manager.ActiveHandle.NamedId,
                "过渡到已销毁的 vcam 必须整体忽略（CIN-003）");
        }

        [Test]
        public void HandleChangeSurface_ExternallyDestroyedVcam_WarnsAndNoOps()
        {
            _manager.Activate("b");
            Pump();

            var extraObject = new GameObject("VcamC");
            var extra = extraObject.AddComponent<CinemachineCamera>();
            var handle = _manager.Register(extra);

            try
            {
                UnityEngine.Object.DestroyImmediate(extraObject);

                LogAssert.Expect(LogType.Warning, new Regex("已被外部销毁"));
                _manager.Activate(handle);

                LogAssert.Expect(LogType.Warning, new Regex("已被外部销毁"));
                _manager.TransitionTo(handle, 1f);

                LogAssert.Expect(LogType.Warning, new Regex("已被外部销毁"));
                Assert.DoesNotThrow(() => _manager.Deactivate(handle),
                    "反激活已销毁的 vcam 不得抛 MissingReferenceException（CIN-003）");

                Assert.AreEqual("b", _manager.ActiveHandle.NamedId,
                    "句柄变更面在 vcam 销毁后必须全部 no-op（CIN-003）");
            }
            finally
            {
                if (extraObject != null)
                    UnityEngine.Object.DestroyImmediate(extraObject);
            }
        }

        [Test]
        public void DestroyedVcam_PrunesStaleReverseEntry()
        {
            _manager.Activate("a");
            Pump();
            UnityEngine.Object.DestroyImmediate(_vcamAObject);

            LogAssert.Expect(LogType.Warning, new Regex("已被外部销毁"));
            _manager.Deactivate("a");

            var field = typeof(CinemachineCameraManager).GetField("_reverse",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "反查表字段名变了，请同步本用例");
            var reverse = (IDictionary)field.GetValue(_manager);
            Assert.AreEqual(1, reverse.Count,
                "已销毁 vcam 在反查表里的陈旧键必须被清掉（CIN-003）；剩余的一项应是 b");
        }

        #endregion
    }
}
