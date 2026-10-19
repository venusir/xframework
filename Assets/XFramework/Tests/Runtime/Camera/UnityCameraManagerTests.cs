using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XUpdate;

namespace XFramework.XCamera.Tests
{
    /// <summary>
    /// 默认实现的行为：激活 / 反激活 / cut / 插值过渡 / 中断 / 注册表 / 生命周期。
    /// <para>用 <see cref="UpdateManager.Tick(float)"/> 手动驱动，不依赖真实帧时长。</para>
    /// </summary>
    [TestFixture]
    public class UnityCameraManagerTests
    {
        #region Fixture

        private GameObject _objectA;
        private GameObject _objectB;
        private Camera _cameraA;
        private Camera _cameraB;

        [SetUp]
        public void SetUp()
        {
            CameraManager.Destroy();
            UpdateManager.AutoDriveEnabled = false;
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _objectA = new GameObject("CameraA");
            _objectB = new GameObject("CameraB");
            _cameraA = _objectA.AddComponent<Camera>();
            _cameraB = _objectB.AddComponent<Camera>();
            _cameraA.transform.position = Vector3.zero;
            _cameraB.transform.position = new Vector3(10f, 0f, 0f);
            _cameraA.fieldOfView = 60f;
            _cameraB.fieldOfView = 40f;
        }

        [TearDown]
        public void TearDown()
        {
            CameraManager.Destroy();
            UpdateManager.AutoDriveEnabled = true;
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            if (_objectA != null)
                UnityEngine.Object.DestroyImmediate(_objectA);
            if (_objectB != null)
                UnityEngine.Object.DestroyImmediate(_objectB);
        }

        private UnityCameraManager CreateManager()
        {
            return new UnityCameraManager(new Dictionary<string, Camera>
            {
                ["a"] = _cameraA,
                ["b"] = _cameraB,
            });
        }

        private static void Tick(float time)
        {
            UpdateManager.Tick(time);
        }

        #endregion

        #region Activation

        [Test]
        public void Activate_EnablesTargetAndDisablesOthers()
        {
            var manager = CreateManager();

            manager.Activate("a");

            Assert.IsTrue(_cameraA.enabled);
            Assert.IsFalse(_cameraB.enabled);
            Assert.AreEqual("a", manager.ActiveHandle.NamedId);
            Assert.AreSame(_cameraA, manager.OutputCamera);
            Assert.IsTrue(manager.IsActive("a"));
            Assert.IsFalse(manager.IsTransitioning);
        }

        [Test]
        public void Activate_UnknownId_LogsWarningAndNoOps()
        {
            var manager = CreateManager();
            manager.Activate("a");

            LogAssert.Expect(LogType.Warning, new Regex("未知相机 id.*"));
            manager.Activate("missing");

            Assert.AreEqual("a", manager.ActiveHandle.NamedId);
            Assert.IsTrue(_cameraA.enabled);
        }

        [Test]
        public void Deactivate_ActiveCamera_ClearsActiveAndOutput()
        {
            var manager = CreateManager();
            manager.Activate("a");

            manager.Deactivate("a");

            Assert.IsFalse(_cameraA.enabled);
            Assert.IsNull(manager.ActiveHandle.NamedId);
            Assert.IsNull(manager.OutputCamera);
        }

        #endregion

        #region Transition

        [Test]
        public void TransitionTo_InterpolatesAndCompletes()
        {
            var manager = CreateManager();
            manager.Activate("a");

            manager.TransitionTo("b", 1f, CameraBlendStyle.Linear);
            Tick(0f); // 注册入缓冲，本拍生效且首次 delta 锚定为 0

            Assert.IsTrue(manager.IsTransitioning);
            Assert.AreEqual("b", manager.ActiveHandle.NamedId);
            Assert.IsTrue(_cameraB.enabled);
            Assert.IsFalse(_cameraA.enabled);
            Assert.AreEqual(_cameraA.transform.position.x, _cameraB.transform.position.x, 0.001f,
                "过渡起点应是源相机的当前位姿");

            Tick(0.5f);
            Assert.AreEqual(5f, _cameraB.transform.position.x, 0.01f);
            Assert.AreEqual(50f, _cameraB.fieldOfView, 0.01f);
            Assert.IsTrue(manager.IsTransitioning);

            Tick(1f);
            Assert.AreEqual(10f, _cameraB.transform.position.x, 0.001f);
            Assert.AreEqual(40f, _cameraB.fieldOfView, 0.001f);
            Assert.IsFalse(manager.IsTransitioning);
            Assert.AreEqual("b", manager.ActiveHandle.NamedId);
        }

        [Test]
        public void TransitionTo_DuringTransition_RestartsFromCurrentVision()
        {
            var manager = CreateManager();
            manager.Activate("a");
            manager.TransitionTo("b", 1f, CameraBlendStyle.Linear);
            Tick(0f); // 注册生效拍
            Tick(0.5f);
            Assert.AreEqual(5f, _cameraB.transform.position.x, 0.01f);

            manager.TransitionTo("a", 1f, CameraBlendStyle.Linear);

            Assert.AreEqual(5f, _cameraA.transform.position.x, 0.001f,
                "新过渡应从当前视觉状态起步");
            Assert.AreEqual(10f, _cameraB.transform.position.x, 0.001f,
                "旧目标应恢复自己的原始位姿");
            Assert.IsTrue(_cameraA.enabled);
            Assert.IsFalse(_cameraB.enabled);

            Tick(1.5f);
            Assert.AreEqual(0f, _cameraA.transform.position.x, 0.001f);
            Assert.AreEqual("a", manager.ActiveHandle.NamedId);
            Assert.IsFalse(manager.IsTransitioning);
        }

        [Test]
        public void Activate_DuringTransition_CancelsAndRestoresTargetPose()
        {
            var manager = CreateManager();
            manager.Activate("a");
            manager.TransitionTo("b", 1f, CameraBlendStyle.Linear);
            Tick(0f); // 注册生效拍
            Tick(0.5f);
            Assert.AreEqual(5f, _cameraB.transform.position.x, 0.01f);

            manager.Activate("a");

            Assert.IsFalse(manager.IsTransitioning);
            Assert.AreEqual("a", manager.ActiveHandle.NamedId);
            Assert.IsTrue(_cameraA.enabled);
            Assert.IsFalse(_cameraB.enabled);
            Assert.AreEqual(10f, _cameraB.transform.position.x, 0.001f,
                "被取消的目标应恢复自己的原始位姿");
        }

        [Test]
        public void Deactivate_TransitionTarget_CancelsTransition()
        {
            var manager = CreateManager();
            manager.Activate("a");
            Tick(0f);
            manager.TransitionTo("b", 1f, CameraBlendStyle.Linear);

            manager.Deactivate("b");

            Assert.IsFalse(manager.IsTransitioning);
            Assert.IsNull(manager.ActiveHandle.NamedId);
            Assert.IsFalse(_cameraB.enabled);
            Assert.AreEqual(10f, _cameraB.transform.position.x, 0.001f);
        }

        [Test]
        public void TransitionTo_ZeroDuration_Cuts()
        {
            var manager = CreateManager();
            manager.Activate("a");

            manager.TransitionTo("b", 0f);

            Assert.IsFalse(manager.IsTransitioning);
            Assert.IsTrue(_cameraB.enabled);
            Assert.IsFalse(_cameraA.enabled);
            Assert.AreEqual(10f, _cameraB.transform.position.x, 0.001f,
                "cut 不应把目标先搬到源相机的位姿");
        }

        [Test]
        public void TransitionTo_ProjectionMismatch_Cuts()
        {
            _cameraB.orthographic = true;
            var manager = CreateManager();
            manager.Activate("a");

            manager.TransitionTo("b", 1f);

            Assert.IsFalse(manager.IsTransitioning);
            Assert.AreEqual("b", manager.ActiveHandle.NamedId);
            Assert.IsTrue(_cameraB.enabled);
        }

        [Test]
        public void TransitionTo_UnknownId_LogsWarningAndNoOps()
        {
            var manager = CreateManager();
            manager.Activate("a");

            LogAssert.Expect(LogType.Warning, new Regex("未知相机 id.*"));
            manager.TransitionTo("missing", 1f);

            Assert.AreEqual("a", manager.ActiveHandle.NamedId);
            Assert.IsFalse(manager.IsTransitioning);
        }

        #endregion

        #region Registry / Lifetime

        [Test]
        public void RegisterAndUnregister_UpdateRegistry()
        {
            var manager = CreateManager();
            var extraObject = new GameObject("CameraC");
            var extra = extraObject.AddComponent<Camera>();

            try
            {
                manager.Register("c", extra);
                CollectionAssert.Contains(manager.CameraIds, "c");

                manager.Activate("c");
                Assert.AreEqual("c", manager.ActiveHandle.NamedId);
                Assert.AreSame(extra, manager.OutputCamera);

                Assert.IsTrue(manager.Unregister("c"));
                CollectionAssert.DoesNotContain(manager.CameraIds, "c");
                Assert.IsNull(manager.ActiveHandle.NamedId);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(extraObject);
            }
        }

        [Test]
        public void RegisterDynamic_Activate_Unregister()
        {
            var manager = CreateManager();
            var extraObject = new GameObject("CameraC");
            var extra = extraObject.AddComponent<Camera>();
            extra.transform.position = new Vector3(20f, 0f, 0f);

            try
            {
                var handle = manager.Register(extra);
                Assert.IsFalse(handle.IsDefault);
                Assert.IsFalse(extra.enabled);

                manager.Activate(handle);

                Assert.AreEqual(handle, manager.ActiveHandle);
                Assert.IsNull(manager.ActiveHandle.NamedId);
                Assert.AreSame(extra, manager.OutputCamera);
                Assert.IsTrue(manager.IsActive(handle));
                Assert.IsTrue(extra.enabled);
                Assert.IsFalse(_cameraA.enabled);
                Assert.IsFalse(_cameraB.enabled);

                Assert.IsTrue(manager.Unregister(handle));
                Assert.IsNull(manager.ActiveHandle.NamedId);
                Assert.IsFalse(manager.IsActive(handle));
                Assert.IsFalse(extra.enabled);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(extraObject);
            }
        }

        [Test]
        public void TransitionTo_DynamicHandle_InterpolatesAndCompletes()
        {
            var manager = CreateManager();
            var extraObject = new GameObject("CameraC");
            var extra = extraObject.AddComponent<Camera>();
            extra.transform.position = new Vector3(20f, 0f, 0f);

            try
            {
                manager.Activate("a");
                var handle = manager.Register(extra);

                manager.TransitionTo(handle, 1f, CameraBlendStyle.Linear);
                Tick(0f);

                Assert.IsTrue(manager.IsTransitioning);
                Assert.AreEqual(handle, manager.ActiveHandle);
                Assert.IsNull(manager.ActiveHandle.NamedId);
                Assert.AreSame(extra, manager.OutputCamera);

                Tick(0.5f);
                Assert.AreEqual(10f, extra.transform.position.x, 0.01f);

                Tick(1f);
                Assert.AreEqual(20f, extra.transform.position.x, 0.001f);
                Assert.IsFalse(manager.IsTransitioning);
                Assert.AreEqual(handle, manager.ActiveHandle);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(extraObject);
            }
        }

        [Test]
        public void StaleHandle_IsNoOp()
        {
            var manager = CreateManager();
            var extraObject = new GameObject("CameraC");
            var extra = extraObject.AddComponent<Camera>();

            try
            {
                var handle = manager.Register(extra);
                Assert.IsTrue(manager.Unregister(handle));

                LogAssert.Expect(LogType.Warning, new Regex("相机句柄无效或已注销.*"));
                manager.Activate(handle);

                Assert.IsNull(manager.ActiveHandle.NamedId);
                Assert.IsFalse(manager.IsActive(handle));
                Assert.IsFalse(manager.Unregister(handle));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(extraObject);
            }
        }

        [Test]
        public void StaleHandle_AfterSlotReuse_IsNoOp()
        {
            // CAM-004：既有用例只覆盖「槽位空置」；这里覆盖槽位被复用后的 ABA——
            // 旧句柄按代际失配必须失效，不得操作新相机。
            var manager = CreateManager();
            var firstObject = new GameObject("CameraReuseA");
            var secondObject = new GameObject("CameraReuseB");
            var first = firstObject.AddComponent<Camera>();
            var second = secondObject.AddComponent<Camera>();

            try
            {
                var oldHandle = manager.Register(first);
                Assert.IsTrue(manager.Unregister(oldHandle));

                var newHandle = manager.Register(second);
                Assert.AreEqual(oldHandle.Id, newHandle.Id, "前提：新相机应复用刚空出的槽位");
                Assert.AreNotEqual(oldHandle.Generation, newHandle.Generation, "前提：复用时代际必须递增");

                manager.Activate(newHandle);
                Assert.IsTrue(manager.IsActive(newHandle), "前提：新句柄应已成为活动相机");

                LogAssert.Expect(LogType.Warning, new Regex("相机句柄无效或已注销.*"));
                manager.Activate(oldHandle);
                Assert.IsTrue(manager.IsActive(newHandle), "旧句柄 Activate 不得抢占复用槽位上的新相机");
                Assert.IsFalse(manager.IsActive(oldHandle));

                LogAssert.Expect(LogType.Warning, new Regex("相机句柄无效或已注销.*"));
                manager.Deactivate(oldHandle);
                Assert.IsTrue(manager.IsActive(newHandle), "旧句柄 Deactivate 不得停用新相机");

                Assert.IsFalse(manager.Unregister(oldHandle), "旧句柄注销应返回 false");
                Assert.IsTrue(manager.Unregister(newHandle), "新句柄仍有效");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(firstObject);
                UnityEngine.Object.DestroyImmediate(secondObject);
            }
        }

        [Test]
        public void DefaultHandle_IsNoOp()
        {
            var manager = CreateManager();

            LogAssert.Expect(LogType.Warning, new Regex("相机句柄无效或已注销.*"));
            manager.Activate(default(CameraHandle));

            Assert.IsNull(manager.ActiveHandle.NamedId);
            Assert.IsFalse(manager.IsActive(default(CameraHandle)));
            Assert.IsFalse(manager.Unregister(default(CameraHandle)));
        }

        [Test]
        public void RegisterDynamic_WrongType_Throws()
        {
            var manager = CreateManager();

            Assert.Throws<ArgumentException>(() => manager.Register<object>(new object()));
            Assert.Throws<ArgumentNullException>(() => manager.Register<Camera>(null));
        }

        [Test]
        public void Facade_DynamicRegister_ActivatesAndUnregisters()
        {
            var manager = CreateManager();
            CameraManager.Initialize(manager);
            var extraObject = new GameObject("CameraC");
            var extra = extraObject.AddComponent<Camera>();
            extra.transform.position = new Vector3(20f, 0f, 0f);

            try
            {
                var handle = CameraManager.Register(extra);
                CameraManager.Activate(handle);

                Assert.AreEqual(handle, CameraManager.ActiveHandle);
                Assert.IsNull(CameraManager.ActiveCameraId);
                Assert.AreSame(extra, CameraManager.OutputCamera);
                Assert.IsTrue(CameraManager.IsActive(handle));

                Assert.IsTrue(CameraManager.Unregister(handle));
                Assert.IsFalse(CameraManager.IsActive(handle));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(extraObject);
            }
        }

        [Test]
        public void Dispose_ThrowsOnUse()
        {
            var manager = CreateManager();
            manager.Dispose();

            Assert.Throws<ObjectDisposedException>(() => manager.Activate("a"));
            Assert.Throws<ObjectDisposedException>(() => manager.TransitionTo("a", 1f));
        }

        [Test]
        public void NullId_Throws()
        {
            var manager = CreateManager();

            Assert.Throws<ArgumentNullException>(() => manager.Activate(null));
            Assert.Throws<ArgumentNullException>(() => manager.Deactivate(null));
            Assert.Throws<ArgumentNullException>(() => manager.TransitionTo(null, 1f));
        }

        #endregion

        #region 被外部销毁的相机

        [Test]
        public void TransitionTo_NonFiniteDuration_Throws()
        {
            var manager = new UnityCameraManager(new Dictionary<string, Camera>
            {
                ["A"] = _cameraA,
                ["B"] = _cameraB,
            });

            Assert.Throws<ArgumentOutOfRangeException>(
                () => manager.TransitionTo("B", float.NaN),
                "NaN 会让过渡永不结束并逐帧写坏位姿（CAM-003）");
            Assert.Throws<ArgumentOutOfRangeException>(
                () => manager.TransitionTo("B", float.PositiveInfinity),
                "+∞ 会让过渡永久挂起（CAM-003）");
            Assert.Throws<ArgumentOutOfRangeException>(
                () => manager.TransitionTo("B", float.NegativeInfinity),
                "−∞ 同样不是合法的时长（CAM-003）");
        }

        [Test]
        public void DestroyedActiveCamera_IsReportedAsInactive()
        {            var manager = new UnityCameraManager(new Dictionary<string, Camera>
            {
                ["A"] = _cameraA,
                ["B"] = _cameraB,
            });
            manager.Activate("A");
            Assert.AreSame(_cameraA, manager.OutputCamera, "前提校验：A 是活动相机");

            UnityEngine.Object.DestroyImmediate(_objectA);

            Assert.IsNull(manager.OutputCamera,
                "被销毁的活动相机必须按 null 报告——直接返回引用会给出一个非 null 但不可用的对象（CAM-001）");
            Assert.IsTrue(manager.ActiveHandle.IsDefault,
                "ActiveHandle 不得继续报告已销毁的相机（CAM-001）");
            Assert.IsFalse(manager.IsActive("A"),
                "IsActive 不得继续报告已销毁的相机（CAM-001）");
        }

        [Test]
        public void Activate_ExternallyDestroyedCamera_WarnsAndKeepsActiveCamera()
        {
            var manager = CreateManager();
            manager.Activate("b");
            Assert.IsTrue(_cameraB.enabled, "前提校验：b 是活动相机");

            UnityEngine.Object.DestroyImmediate(_objectA);

            LogAssert.Expect(LogType.Warning, new Regex("已被外部销毁"));
            manager.Activate("a");

            Assert.IsTrue(_cameraB.enabled,
                "激活已销毁的相机不得把其它活相机一并关掉（CAM-002）");
            Assert.IsTrue(manager.IsActive("b"), "活动相机必须仍是 b（CAM-002）");
        }

        [Test]
        public void TransitionTo_ExternallyDestroyedCamera_WarnsAndKeepsActiveCamera()
        {
            var manager = CreateManager();
            manager.Activate("b");
            Assert.IsTrue(_cameraB.enabled, "前提校验：b 是活动相机");

            UnityEngine.Object.DestroyImmediate(_objectA);

            LogAssert.Expect(LogType.Warning, new Regex("已被外部销毁"));
            manager.TransitionTo("a", 1f);

            Assert.IsFalse(manager.IsTransitioning, "过渡到已销毁的相机必须整体忽略（CAM-002）");
            Assert.IsTrue(_cameraB.enabled, "其它活相机不得被关掉（CAM-002）");
            Assert.IsTrue(manager.IsActive("b"), "活动相机必须仍是 b（CAM-002）");
        }

        [Test]
        public void HandleChangeSurface_ExternallyDestroyedCamera_WarnsAndKeepsActiveCamera()
        {
            var manager = CreateManager();
            manager.Activate("b");
            Assert.IsTrue(_cameraB.enabled, "前提校验：b 是活动相机");

            var extraObject = new GameObject("CameraC");
            var extra = extraObject.AddComponent<Camera>();
            var handle = manager.Register(extra);

            try
            {
                UnityEngine.Object.DestroyImmediate(extraObject);

                LogAssert.Expect(LogType.Warning, new Regex("已被外部销毁"));
                manager.Activate(handle);

                LogAssert.Expect(LogType.Warning, new Regex("已被外部销毁"));
                manager.TransitionTo(handle, 1f);

                Assert.IsFalse(manager.IsTransitioning, "过渡到已销毁的相机必须整体忽略（CAM-002）");
                Assert.IsTrue(_cameraB.enabled, "其它活相机不得被关掉（CAM-002）");
                Assert.IsTrue(manager.IsActive("b"), "活动相机必须仍是 b（CAM-002）");
            }
            finally
            {
                if (extraObject != null)
                    UnityEngine.Object.DestroyImmediate(extraObject);
            }
        }

        #endregion
    }
}
