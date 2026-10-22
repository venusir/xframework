using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XCamera.Messages;

namespace XFramework.XCamera.Cinemachine.Tests
{
    /// <summary>
    /// 相机状态事件：发布时机 / 顺序 / 过滤 / 退订。
    /// <para>Brain 用 <see cref="CinemachineBrain.UpdateMethods.ManualUpdate"/> 确定性推进。</para>
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
        private CinemachineCameraManager _manager;
        private CameraHandle _handleA;
        private CameraHandle _handleB;
        private int _frame;
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>();

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
            for (var i = 0; i < _subscriptions.Count; i++)
                _subscriptions[i].Dispose();
            _subscriptions.Clear();

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

        private void SubscribeAll(List<string> events)
        {
            _subscriptions.Add(CameraManager.Subscribe(
                (CameraActivatedMessage m) => events.Add("activated:" + m.Handle.NamedId)));
            _subscriptions.Add(CameraManager.Subscribe(
                (CameraDeactivatedMessage m) => events.Add("deactivated:" + m.Handle.NamedId)));
            _subscriptions.Add(CameraManager.Subscribe(
                (CameraBlendCompletedMessage m) => events.Add("completed:" + m.Handle.NamedId)));
        }

        private static CinemachineCamera CreateVcam(string name, int priority)
        {
            var obj = new GameObject(name);
            var vcam = obj.AddComponent<CinemachineCamera>();
            vcam.Priority = priority;
            return vcam;
        }

        #endregion

        #region Timing

        [Test]
        public void Cut_PublishesDeactivatedThenActivatedThenCompleted()
        {
            _manager.Activate(_handleA);
            Pump();

            var events = new List<string>();
            SubscribeAll(events);

            _manager.Activate(_handleB);
            Pump();

            CollectionAssert.AreEqual(
                new[] { "deactivated:a", "activated:b", "completed:b" }, events);
        }

        [Test]
        public void Blend_PublishesActivatedAtStart_DeactivatedAndCompletedAtEnd()
        {
            _manager.Activate(_handleA);
            Pump();

            var events = new List<string>();
            SubscribeAll(events);

            _manager.TransitionTo(_handleB, 0.2f, CameraBlendStyle.Linear);
            Pump(0.1f);

            CollectionAssert.Contains(events, "activated:b");
            CollectionAssert.DoesNotContain(events, "completed:b");

            for (var i = 0; i < 6 && _manager.IsTransitioning; i++)
                Pump(0.05f);

            CollectionAssert.AreEqual(
                new[] { "activated:b", "deactivated:a", "completed:b" }, events);
        }

        [Test]
        public void UnregisteredIncoming_DoesNotPublishActivated_PublishesDeactivatedForOld()
        {
            _manager.Activate(_handleA);
            Pump();

            var events = new List<string>();
            SubscribeAll(events);

            var external = CreateVcam("External", 10);
            try
            {
                external.Prioritize();
                // 外部接管使用项目 DefaultBlend，时长可能大于 1 秒；推进到混合真正结束。
                for (var i = 0; i < 40; i++)
                    Pump(0.1f);

                CollectionAssert.DoesNotContain(events, "activated:External");
                CollectionAssert.Contains(events, "deactivated:a",
                    "旧的已注册相机失去 live 时必须发布反激活消息");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(external.gameObject);
            }
        }

        [Test]
        public void OtherBrain_DoesNotPublish()
        {
            var events = new List<string>();
            SubscribeAll(events);

            var otherCameraObject = new GameObject("OtherCamera");
            var otherBrainObject = new GameObject("OtherBrain");
            otherCameraObject.AddComponent<Camera>();
            var otherBrain = otherBrainObject.AddComponent<CinemachineBrain>();
            otherBrain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
            var otherVcamObject = new GameObject("OtherVcam");
            var otherVcam = otherVcamObject.AddComponent<CinemachineCamera>();
            otherVcam.Priority = 10;

            try
            {
                otherVcam.Prioritize();
                otherBrain.ManualUpdate(0, 0);

                Assert.IsEmpty(events, "其他 Brain 的事件必须按 Origin / mixer 过滤掉");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(otherVcamObject);
                UnityEngine.Object.DestroyImmediate(otherBrainObject);
                UnityEngine.Object.DestroyImmediate(otherCameraObject);
            }
        }

        #endregion

        #region Unsubscribe

        [Test]
        public void Dispose_ThenInvokeEvents_NoMessages()
        {
            _manager.Dispose();

            var events = new List<string>();
            SubscribeAll(events);

            _vcamB.Priority = 10;
            _vcamB.Prioritize();
            Pump();

            Assert.IsEmpty(events, "Dispose 后旧管理器不得再发布消息");
        }

        [Test]
        public void UnbindSessionEvents_ThenInvokeEvents_NoMessages()
        {
            ((ICameraManagerSessionReset)_manager).UnbindSessionEvents();

            var events = new List<string>();
            SubscribeAll(events);

            _vcamB.Priority = 10;
            _vcamB.Prioritize();
            Pump();

            Assert.IsEmpty(events, "会话复位后旧管理器不得再发布消息");
            Assert.Throws<ObjectDisposedException>(() => _manager.Activate(_handleA));
        }

        #endregion
    }
}
