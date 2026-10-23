using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.TestTools;

namespace XFramework.XCamera.Cinemachine.Tests
{
    /// <summary>
    /// 预制体 / 动态相机自注册组件：启用时注册、销毁时注销、池化幂等、未初始化时序。
    /// </summary>
    [TestFixture]
    public class CinemachineCameraRegistrationTests
    {
        #region Fixture

        private GameObject _cameraObject;
        private Camera _camera;
        private CinemachineBrain _brain;
        private readonly List<GameObject> _spawned = new List<GameObject>();
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

            CameraManager.Initialize(new CinemachineCameraBackend(_brain));
            _frame = 0;
        }

        [TearDown]
        public void TearDown()
        {
            for (var i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null)
                    UnityEngine.Object.DestroyImmediate(_spawned[i]);
            }

            _spawned.Clear();
            CameraManager.Destroy();
            CinemachineCore.GetBlendOverride = null;

            if (_cameraObject != null)
                UnityEngine.Object.DestroyImmediate(_cameraObject);
        }

        private CinemachineCamera CreateVcam(string name, bool addRegistration)
        {
            var obj = new GameObject(name);
            var vcam = obj.AddComponent<CinemachineCamera>();
            _spawned.Add(obj);

            if (addRegistration)
                obj.AddComponent<CinemachineCameraRegistration>();

            return vcam;
        }

        private void Pump(float delta = 0f)
        {
            _brain.ManualUpdate(_frame++, delta);
        }

        #endregion

        #region Registration / lifetime

        [Test]
        public void OnEnable_WhenInitialized_Registers()
        {
            var vcam = CreateVcam("Dynamic", addRegistration: true);
            var registration = vcam.GetComponent<CinemachineCameraRegistration>();

            Assert.IsNotNull(registration.CameraId);
            StringAssert.StartsWith("@auto/", registration.CameraId);
            Assert.IsTrue(registration.IsRegistered);

            Assert.IsTrue(CameraManager.TryGetCamera(registration.CameraId, out CinemachineCamera found));
            Assert.AreSame(vcam, found);
        }

        [Test]
        public void TwoInstances_GetDistinctIds()
        {
            var first = CreateVcam("First", true).GetComponent<CinemachineCameraRegistration>();
            var second = CreateVcam("Second", true).GetComponent<CinemachineCameraRegistration>();

            Assert.AreNotEqual(first.CameraId, second.CameraId);
            Assert.IsTrue(first.IsRegistered);
            Assert.IsTrue(second.IsRegistered);
        }

        [Test]
        public void OnDestroy_UnregistersAndStackFallsBack()
        {
            var baseVcam = CreateVcam("Base", false);
            CameraManager.Register("base", baseVcam);
            CameraManager.SwitchTo("base");
            Pump();

            var dynamicVcam = CreateVcam("Dynamic", true);
            var registration = dynamicVcam.GetComponent<CinemachineCameraRegistration>();
            var id = registration.CameraId;
            CameraManager.Push(id);
            Pump();
            Assert.AreEqual(2, CameraManager.StackDepth);

            UnityEngine.Object.DestroyImmediate(dynamicVcam.gameObject);

            Assert.IsFalse(CameraManager.TryGetCamera(id, out CinemachineVirtualCameraBase _));
            Assert.AreEqual(1, CameraManager.StackDepth);
            Assert.AreEqual("base", CameraManager.BaseCameraId);

            Pump();
            Assert.AreEqual("base", CameraManager.ActiveCameraId);
        }

        [Test]
        public void PooledDeactivate_DoesNotUnregister_ReenableReusesId()
        {
            var vcam = CreateVcam("Pooled", true);
            var registration = vcam.GetComponent<CinemachineCameraRegistration>();
            var id = registration.CameraId;

            vcam.gameObject.SetActive(false);
            Assert.IsTrue(CameraManager.TryGetCamera(id, out CinemachineVirtualCameraBase _),
                "池化对象禁用不应注销");

            vcam.gameObject.SetActive(true);
            Assert.AreEqual(id, registration.Register());
            Assert.IsTrue(registration.IsRegistered);
        }

        [Test]
        public void Register_WhenCameraAlreadyRegisteredElsewhere_WarnsAndLeavesUnregistered()
        {
            var vcam = CreateVcam("Conflict", false);
            CameraManager.Register("manual", vcam);

            LogAssert.Expect(LogType.Warning, new Regex("自动注册失败.*"));
            var registration = vcam.gameObject.AddComponent<CinemachineCameraRegistration>();

            Assert.IsNull(registration.CameraId);
            Assert.IsFalse(registration.IsRegistered);
        }

        [UnityTest]
        public IEnumerator Uninitialized_WarnsThenExplicitRegisterWorks()
        {
            CameraManager.Destroy();

            var vcam = CreateVcam("Later", false);
            LogAssert.Expect(LogType.Warning, new Regex("无法自动注册.*尚未初始化.*"));
            var registration = vcam.gameObject.AddComponent<CinemachineCameraRegistration>();

            yield return null; // Start 重试并告警一次

            Assert.IsNull(registration.CameraId);

            CameraManager.Initialize(new CinemachineCameraBackend(_brain));
            var id = registration.Register();

            Assert.IsNotNull(id);
            Assert.IsTrue(registration.IsRegistered);
            Assert.IsTrue(CameraManager.TryGetCamera(id, out CinemachineCamera found));
            Assert.AreSame(vcam, found);
        }

        [Test]
        public void OnDestroy_WhenManagerDestroyed_IsSafe()
        {
            var vcam = CreateVcam("Safe", true);
            var registration = vcam.GetComponent<CinemachineCameraRegistration>();
            Assert.IsNotNull(registration.CameraId);

            CameraManager.Destroy();

            Assert.DoesNotThrow(() => UnityEngine.Object.DestroyImmediate(vcam.gameObject));
        }

        [Test]
        public void ClearShotParent_RegistersParent()
        {
            var parentObject = new GameObject("ClearShot");
            _spawned.Add(parentObject);
            var clearShot = parentObject.AddComponent<CinemachineClearShot>();

            var childObject = new GameObject("Child");
            childObject.transform.SetParent(parentObject.transform);
            childObject.AddComponent<CinemachineCamera>();

            var registration = parentObject.AddComponent<CinemachineCameraRegistration>();

            Assert.AreSame(clearShot, registration.Camera);
            Assert.IsNotNull(registration.CameraId);
            Assert.IsTrue(CameraManager.TryGetCamera(
                registration.CameraId, out CinemachineVirtualCameraBase found));
            Assert.AreSame(clearShot, found);
        }

        #endregion
    }
}
