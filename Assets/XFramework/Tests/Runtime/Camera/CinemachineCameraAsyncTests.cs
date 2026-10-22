using System;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.TestTools;

namespace XFramework.XCamera.Cinemachine.Tests
{
    /// <summary>
    /// <c>TransitionToAsync</c> 的完成 / 取消 / 取代 / 注销 / 销毁语义。
    /// <para>Brain 用 <see cref="CinemachineBrain.UpdateMethods.ManualUpdate"/> 确定性推进。</para>
    /// </summary>
    [TestFixture]
    public class CinemachineCameraAsyncTests
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

        #endregion

        #region Completion

        [Test]
        public void TransitionToAsync_CompletesOnBlendFinished()
        {
            _manager.Activate(_handleA);
            Pump();

            var task = _manager.TransitionToAsync(_handleB, 0.2f, CameraBlendStyle.Linear);
            Pump(0.1f);

            for (var i = 0; i < 10 && _manager.IsTransitioning; i++)
                Pump(0.05f);

            Assert.IsFalse(_manager.IsTransitioning);
            Assert.AreEqual(CameraTransitionResult.Completed, task.GetAwaiter().GetResult());
            Assert.IsTrue(_manager.IsActive(_handleB));
        }

        [Test]
        public void TransitionToAsync_Cut_CompletesOnActivation()
        {
            _manager.Activate(_handleA);
            Pump();

            var task = _manager.TransitionToAsync(_handleB, 0f);
            Pump();

            Assert.AreEqual(CameraTransitionResult.Completed, task.GetAwaiter().GetResult());
            Assert.IsTrue(_manager.IsActive(_handleB));
        }

        [Test]
        public void TransitionToAsync_SameRequest_JoinsSharedPending()
        {
            _manager.Activate(_handleA);
            Pump();

            var first = _manager.TransitionToAsync(_handleB, 0.2f, CameraBlendStyle.Linear);
            var second = _manager.TransitionToAsync(_handleB, 0.2f, CameraBlendStyle.Linear);

            for (var i = 0; i < 10 && _manager.IsTransitioning; i++)
                Pump(0.05f);

            Assert.AreEqual(CameraTransitionResult.Completed, first.GetAwaiter().GetResult());
            Assert.AreEqual(CameraTransitionResult.Completed, second.GetAwaiter().GetResult());
        }

        #endregion

        #region Supersede / Unregister / Dispose

        [Test]
        public void TransitionToAsync_SupersededByActivate_ReturnsSuperseded()
        {
            _manager.Activate(_handleA);
            Pump();

            var task = _manager.TransitionToAsync(_handleB, 2f, CameraBlendStyle.Linear);
            Pump(0.1f);

            _manager.Activate(_handleA);
            Pump();

            Assert.AreEqual(CameraTransitionResult.Superseded, task.GetAwaiter().GetResult());
        }

        [Test]
        public void TransitionToAsync_TargetUnregistered_ReturnsTargetUnregistered()
        {
            _manager.Activate(_handleA);
            Pump();

            var task = _manager.TransitionToAsync(_handleB, 2f, CameraBlendStyle.Linear);
            Pump(0.1f);

            Assert.IsTrue(_manager.Unregister(_handleB));
            Assert.AreEqual(CameraTransitionResult.TargetUnregistered, task.GetAwaiter().GetResult());
        }

        [Test]
        public void TransitionToAsync_Disposed_ReturnsManagerDisposed()
        {
            _manager.Activate(_handleA);
            Pump();

            var task = _manager.TransitionToAsync(_handleB, 2f, CameraBlendStyle.Linear);
            Pump(0.1f);

            _manager.Dispose();
            Assert.AreEqual(CameraTransitionResult.ManagerDisposed, task.GetAwaiter().GetResult());
        }

        [Test]
        public void TransitionToAsync_TargetDestroyedBeforeActivation_ReturnsTargetUnregistered()
        {
            _manager.Activate(_handleA);
            Pump();

            var task = _manager.TransitionToAsync(_handleB, 2f, CameraBlendStyle.Linear);
            Pump(0.1f);

            UnityEngine.Object.DestroyImmediate(_vcamBObject);
            Pump();

            Assert.AreEqual(CameraTransitionResult.TargetUnregistered, task.GetAwaiter().GetResult());
        }

        [Test]
        public void TransitionToAsync_DifferentStyle_Supersedes()
        {
            _manager.Activate(_handleA);
            Pump();

            var first = _manager.TransitionToAsync(_handleB, 2f, CameraBlendStyle.Linear);
            Pump(0.1f);
            var second = _manager.TransitionToAsync(_handleB, 2f, CameraBlendStyle.EaseIn);

            Assert.AreEqual(CameraTransitionResult.Superseded, first.GetAwaiter().GetResult());

            for (var i = 0; i < 30 && _manager.IsTransitioning; i++)
                Pump(0.1f);

            Assert.AreEqual(CameraTransitionResult.Completed, second.GetAwaiter().GetResult());
        }

        #endregion

        #region Cancellation

        [Test]
        public void TransitionToAsync_Cancellation_OnlyStopsWaiting()
        {
            _manager.Activate(_handleA);
            Pump();

            using var source = new CancellationTokenSource();
            var task = _manager.TransitionToAsync(_handleB, 0.2f, CameraBlendStyle.Linear, source.Token);
            Pump(0.1f);
            source.Cancel();

            Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());

            for (var i = 0; i < 10 && _manager.IsTransitioning; i++)
                Pump(0.05f);

            Assert.IsFalse(_manager.IsTransitioning, "取消等待不应中止相机过渡");
            Assert.IsTrue(_manager.IsActive(_handleB));
        }

        [Test]
        public void TransitionToAsync_PreCancelled_DoesNotStart()
        {
            _manager.Activate(_handleA);
            Pump();

            using var source = new CancellationTokenSource();
            source.Cancel();

            Assert.Throws<OperationCanceledException>(
                () => _manager.TransitionToAsync(_handleB, 2f, CameraBlendStyle.Linear, source.Token));

            Assert.IsTrue(_manager.IsActive(_handleA));
            Assert.IsFalse(_manager.IsTransitioning);
        }

        #endregion

        #region Invalid Input

        [Test]
        public void TransitionToAsync_InvalidHandle_ReturnsTargetUnregistered()
        {
            LogAssert.Expect(LogType.Warning, new Regex("相机句柄无效或已注销.*"));

            var task = _manager.TransitionToAsync(new CameraHandle(99, 1), 1f);

            Assert.AreEqual(CameraTransitionResult.TargetUnregistered, task.GetAwaiter().GetResult());
        }

        [Test]
        public void TransitionToAsync_NonFiniteDuration_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _manager.TransitionToAsync(_handleB, float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _manager.TransitionToAsync(_handleB, float.PositiveInfinity));
        }

        #endregion
    }
}
