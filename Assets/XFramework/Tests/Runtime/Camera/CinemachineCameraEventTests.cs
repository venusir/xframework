using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using XFramework.XCamera.Messages;
using XFramework.XMessage;

namespace XFramework.XCamera.Cinemachine.Tests
{
    /// <summary>
    /// 相机状态消息：cut / blend 时序、外部 incoming 的 null id、其他 Brain 过滤与销毁静默。
    /// </summary>
    [TestFixture]
    public class CinemachineCameraEventTests
    {
        #region Fixture

        private GameObject _cameraObject;
        private Camera _camera;
        private CinemachineBrain _brain;
        private GameObject _vcamAObject;
        private GameObject _vcamBObject;
        private CinemachineCamera _vcamA;
        private CinemachineCamera _vcamB;
        private readonly List<string> _events = new List<string>();
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>();
        private readonly List<GameObject> _objects = new List<GameObject>();
        private int _frame;

        [SetUp]
        public void SetUp()
        {
            CameraManager.Destroy();
            CinemachineCore.GetBlendOverride = null;
            _events.Clear();

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

            _subscriptions.Add(CameraManager.Subscribe((CameraActivatedMessage message) =>
                _events.Add("activated:" + (message.CameraId ?? "null") + ":" +
                            (message.IsCut ? "cut" : "blend") + ":" +
                            (message.PreviousCameraId ?? "null"))));
            _subscriptions.Add(CameraManager.Subscribe((CameraDeactivatedMessage message) =>
                _events.Add("deactivated:" + (message.CameraId ?? "null"))));
            _subscriptions.Add(CameraManager.Subscribe((CameraBlendCompletedMessage message) =>
                _events.Add("completed:" + (message.CameraId ?? "null"))));

            _frame = 0;
        }

        [TearDown]
        public void TearDown()
        {
            for (var i = 0; i < _subscriptions.Count; i++)
                _subscriptions[i]?.Dispose();
            _subscriptions.Clear();

            CameraManager.Destroy();
            CinemachineCore.GetBlendOverride = null;

            for (var i = 0; i < _objects.Count; i++)
            {
                if (_objects[i] != null)
                    UnityEngine.Object.DestroyImmediate(_objects[i]);
            }

            _objects.Clear();
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

        private CinemachineCamera CreateExternal(string name, int priority)
        {
            var obj = new GameObject(name);
            var camera = obj.AddComponent<CinemachineCamera>();
            camera.Priority = priority;
            _objects.Add(obj);
            return camera;
        }

        #endregion

        #region Event order

        [Test]
        public void Cut_PublishesDeactivatedThenActivatedThenCompleted()
        {
            CameraManager.SwitchTo("a");
            Pump();
            _events.Clear();

            CameraManager.SwitchTo("b");
            Pump();

            CollectionAssert.AreEqual(
                new[] { "deactivated:a", "activated:b:cut:a", "completed:b" },
                _events);
        }

        [Test]
        public void Blend_PublishesActivatedAtStart_DeactivatedAndCompletedAtEnd()
        {
            CameraManager.SwitchTo("a");
            Pump();
            _events.Clear();

            CameraManager.SwitchTo("b", 0.2f, CameraBlendStyle.Linear);
            Pump(0.1f);

            CollectionAssert.AreEqual(
                new[] { "activated:b:blend:a" },
                _events);

            for (var i = 0; i < 8 && CameraManager.GetState().IsTransitioning; i++)
                Pump(0.05f);

            CollectionAssert.AreEqual(
                new[] { "activated:b:blend:a", "deactivated:a", "completed:b" },
                _events);
        }

        [Test]
        public void ExternalIncoming_PublishesNullId()
        {
            CameraManager.SwitchTo("a");
            Pump();
            _events.Clear();

            _brain.DefaultBlend = new CinemachineBlendDefinition(
                CinemachineBlendDefinition.Styles.Cut, 0f);
            CreateExternal("External", 100);
            Pump();

            CollectionAssert.AreEqual(
                new[] { "deactivated:a", "activated:null:cut:a", "completed:null" },
                _events);
        }

        [Test]
        public void OtherBrain_DoesNotPublish()
        {
            var otherCameraObject = new GameObject("OtherCamera");
            _objects.Add(otherCameraObject);
            otherCameraObject.AddComponent<Camera>();
            var otherBrain = otherCameraObject.AddComponent<CinemachineBrain>();
            otherBrain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;

            var otherVcamObject = new GameObject("OtherVcam");
            _objects.Add(otherVcamObject);
            var otherVcam = otherVcamObject.AddComponent<CinemachineCamera>();
            otherVcam.Priority = 10;

            otherBrain.ManualUpdate(0, 0f);

            Assert.IsEmpty(_events, "其他 Brain 的激活不应经本后端发布");
        }

        #endregion

        #region Silence

        [Test]
        public void Dispose_ThenExternalActivation_NoMessages()
        {
            CameraManager.SwitchTo("a");
            Pump();
            _events.Clear();

            CameraManager.Destroy();

            _brain.DefaultBlend = new CinemachineBlendDefinition(
                CinemachineBlendDefinition.Styles.Cut, 0f);
            CreateExternal("ExternalAfterDispose", 100);
            Pump();

            Assert.IsEmpty(_events, "后端 Dispose 后不应再发布消息");
        }

        [Test]
        public void SessionReset_ThenExternalActivation_NoMessages()
        {
            CameraManager.SwitchTo("a");
            Pump();
            _events.Clear();

            CameraManager.ResetForNewSession();

            _brain.DefaultBlend = new CinemachineBlendDefinition(
                CinemachineBlendDefinition.Styles.Cut, 0f);
            CreateExternal("ExternalAfterReset", 100);
            Pump();

            Assert.IsEmpty(_events, "会话复位后不应再发布消息");
        }

        [Test]
        public void Subscribe_WithoutInitialize_Forwards()
        {
            CameraManager.Destroy();

            var received = 0;
            using var subscription = CameraManager.Subscribe(
                (CameraDeactivatedMessage _) => received++);

            MessageManager.Publish(new CameraDeactivatedMessage("x"));

            Assert.AreEqual(1, received);
        }

        #endregion
    }
}
