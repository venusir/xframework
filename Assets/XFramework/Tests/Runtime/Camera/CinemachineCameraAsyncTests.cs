using System;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.TestTools;

namespace XFramework.XCamera.Cinemachine.Tests
{
    /// <summary>
    /// 异步切换 / 压栈 / 弹栈的完成、取代、取消与错误结果。
    /// <para>Brain 用 <see cref="CinemachineBrain.UpdateMethods.ManualUpdate"/> 确定性推进；
    /// 等待者用 <c>GetAwaiter().GetResult()</c> 在泵帧后读取。</para>
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

        private void PumpUntilSettled(int maxFrames = 30)
        {
            for (var i = 0; i < maxFrames && CameraManager.GetState().IsTransitioning; i++)
                Pump(0.1f);
        }

        #endregion

        #region Completion

        [Test]
        public void SwitchToAsync_Blend_Completes()
        {
            CameraManager.SwitchTo("a");
            Pump();

            var task = CameraManager.SwitchToAsync("b", 0.3f, CameraBlendStyle.Linear);
            Pump(0.1f);
            Assert.IsTrue(CameraManager.GetState().IsTransitioning);

            PumpUntilSettled();

            Assert.AreEqual(CameraTransitionResult.Completed, task.GetAwaiter().GetResult());
            Assert.AreEqual("b", CameraManager.ActiveCameraId);
        }

        [Test]
        public void SwitchToAsync_Cut_Completes()
        {
            CameraManager.SwitchTo("a");
            Pump();

            var task = CameraManager.SwitchToAsync("b", 0f);
            Pump();

            Assert.AreEqual(CameraTransitionResult.Completed, task.GetAwaiter().GetResult());
            Assert.AreEqual("b", CameraManager.ActiveCameraId);
        }

        [Test]
        public void SameRequest_JoinsSharedPending()
        {
            CameraManager.SwitchTo("a");
            Pump();

            var first = CameraManager.SwitchToAsync("b", 0.3f, CameraBlendStyle.Linear);
            var second = CameraManager.SwitchToAsync("b", 0.3f, CameraBlendStyle.Linear);

            PumpUntilSettled();

            Assert.AreEqual(CameraTransitionResult.Completed, first.GetAwaiter().GetResult());
            Assert.AreEqual(CameraTransitionResult.Completed, second.GetAwaiter().GetResult());
        }

        [Test]
        public void PushAsync_And_PopAsync_Complete()
        {
            CameraManager.SwitchTo("a");
            Pump();

            var push = CameraManager.PushAsync("b", 0.3f, CameraBlendStyle.Linear);
            PumpUntilSettled();
            Assert.AreEqual(CameraTransitionResult.Completed, push.GetAwaiter().GetResult());
            Assert.AreEqual(2, CameraManager.StackDepth);

            var pop = CameraManager.PopAsync(0.3f, CameraBlendStyle.Linear);
            PumpUntilSettled();
            Assert.AreEqual(CameraTransitionResult.Completed, pop.GetAwaiter().GetResult());
            Assert.AreEqual(1, CameraManager.StackDepth);
            Assert.AreEqual("a", CameraManager.ActiveCameraId);
        }

        [Test]
        public void Unregister_UnrelatedCamera_DoesNotCompletePushEarly()
        {
            CameraManager.SwitchTo("a");
            Pump();

            var push = CameraManager.PushAsync("b", 0.4f, CameraBlendStyle.Linear);
            Pump(0.1f);
            Assert.IsTrue(CameraManager.IsBlending, "前提：应处于 A→B 的混合中");

            var thirdObject = new GameObject("VcamC");
            try
            {
                var third = thirdObject.AddComponent<CinemachineCamera>();
                var id = CameraManager.Register(third);
                CameraManager.Unregister(id);

                Pump(0.1f);

                Assert.IsTrue(CameraManager.IsBlending, "注销无关相机不应截断在途混合");
                Assert.IsFalse(push.Status.IsCompleted(), "等待不应提前完成");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(thirdObject);
            }

            PumpUntilSettled();

            Assert.AreEqual(CameraTransitionResult.Completed, push.GetAwaiter().GetResult());
            Assert.AreEqual("b", CameraManager.ActiveCameraId);
        }

        [Test]
        public void PopAllAsync_Completes()
        {
            CameraManager.SwitchTo("a");
            CameraManager.Push("b");
            Pump();

            var task = CameraManager.PopAllAsync(0.3f, CameraBlendStyle.Linear);
            PumpUntilSettled();

            Assert.AreEqual(CameraTransitionResult.Completed, task.GetAwaiter().GetResult());
            Assert.AreEqual(1, CameraManager.StackDepth);
            Assert.AreEqual("a", CameraManager.ActiveCameraId);
        }

        [Test]
        public void PopAsync_NoOverrides_ReturnsCompleted()
        {
            CameraManager.SwitchTo("a");
            Pump();

            var task = CameraManager.PopAsync();

            Assert.AreEqual(CameraTransitionResult.Completed, task.GetAwaiter().GetResult());
        }

        [Test]
        public void PushAsync_SameTop_ReturnsCompleted()
        {
            CameraManager.SwitchTo("a");
            Pump();

            var task = CameraManager.PushAsync("a");

            Assert.AreEqual(CameraTransitionResult.Completed, task.GetAwaiter().GetResult());
            Assert.AreEqual(1, CameraManager.StackDepth);
        }

        [Test]
        public void AlreadyActive_ReturnsCompleted()
        {
            CameraManager.SwitchTo("a");
            Pump();

            var task = CameraManager.SwitchToAsync("a", 0.5f, CameraBlendStyle.Linear);

            Assert.AreEqual(CameraTransitionResult.Completed, task.GetAwaiter().GetResult());
        }

        #endregion

        #region Supersede / missing

        [Test]
        public void SyncCommand_Supersedes()
        {
            CameraManager.SwitchTo("a");
            Pump();

            var task = CameraManager.SwitchToAsync("b", 2f, CameraBlendStyle.Linear);
            Pump(0.1f);

            CameraManager.SwitchTo("a");
            Pump();

            Assert.AreEqual(CameraTransitionResult.Superseded, task.GetAwaiter().GetResult());
        }

        [Test]
        public void DifferentStyle_SupersedesFirst()
        {
            CameraManager.SwitchTo("a");
            Pump();

            var first = CameraManager.SwitchToAsync("b", 2f, CameraBlendStyle.Linear);
            Pump(0.1f);
            var second = CameraManager.SwitchToAsync("b", 2f, CameraBlendStyle.EaseIn);

            Assert.AreEqual(CameraTransitionResult.Superseded, first.GetAwaiter().GetResult());

            PumpUntilSettled(50);
            Assert.AreEqual(CameraTransitionResult.Completed, second.GetAwaiter().GetResult());
        }

        [Test]
        public void ExternalTakeover_Supersedes()
        {
            CameraManager.SwitchTo("a");
            Pump();

            var task = CameraManager.SwitchToAsync("b", 2f, CameraBlendStyle.Linear);
            Pump(0.1f);

            var externalObject = new GameObject("External", typeof(CinemachineCamera));
            var external = externalObject.GetComponent<CinemachineCamera>();
            external.Priority = 100;
            try
            {
                Pump(0.1f);
                Assert.AreEqual(CameraTransitionResult.Superseded, task.GetAwaiter().GetResult());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(externalObject);
            }
        }

        [Test]
        public void UnregisterTarget_ReturnsCameraNotFound()
        {
            CameraManager.SwitchTo("a");
            Pump();

            var task = CameraManager.SwitchToAsync("b", 2f, CameraBlendStyle.Linear);
            Pump(0.1f);

            CameraManager.Unregister("b");

            Assert.AreEqual(CameraTransitionResult.CameraNotFound, task.GetAwaiter().GetResult());
        }

        [Test]
        public void TargetDestroyed_ReturnsCameraNotFound()
        {
            CameraManager.SwitchTo("a");
            Pump();

            var task = CameraManager.SwitchToAsync("b", 2f, CameraBlendStyle.Linear);
            Pump(0.1f);

            UnityEngine.Object.DestroyImmediate(_vcamBObject);
            Pump();

            Assert.AreEqual(CameraTransitionResult.CameraNotFound, task.GetAwaiter().GetResult());
        }

        [Test]
        public void Destroy_ReturnsManagerDisposed()
        {
            CameraManager.SwitchTo("a");
            Pump();

            var task = CameraManager.SwitchToAsync("b", 2f, CameraBlendStyle.Linear);
            Pump(0.1f);

            CameraManager.Destroy();

            Assert.AreEqual(CameraTransitionResult.ManagerDisposed, task.GetAwaiter().GetResult());
        }

        [Test]
        public void SessionReset_ReturnsManagerDisposed()
        {
            CameraManager.SwitchTo("a");
            Pump();

            var task = CameraManager.SwitchToAsync("b", 2f, CameraBlendStyle.Linear);
            Pump(0.1f);

            CameraManager.ResetForNewSession();

            Assert.AreEqual(CameraTransitionResult.ManagerDisposed, task.GetAwaiter().GetResult());
        }

        [Test]
        public void UnknownId_ReturnsCameraNotFound()
        {
            CameraManager.SwitchTo("a");
            Pump();

            LogAssert.Expect(LogType.Warning, new Regex("未知相机 id: missing.*"));
            var task = CameraManager.SwitchToAsync("missing", 1f);

            Assert.AreEqual(CameraTransitionResult.CameraNotFound, task.GetAwaiter().GetResult());
        }

        #endregion

        #region Cancellation / validation

        [Test]
        public void Cancellation_OnlyStopsWaiting()
        {
            CameraManager.SwitchTo("a");
            Pump();

            using var source = new CancellationTokenSource();
            var task = CameraManager.SwitchToAsync("b", 0.3f, CameraBlendStyle.Linear, source.Token);
            Pump(0.1f);
            source.Cancel();

            Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());

            PumpUntilSettled();
            Assert.IsFalse(CameraManager.GetState().IsTransitioning, "取消等待不应中止相机过渡");
            Assert.AreEqual("b", CameraManager.ActiveCameraId);
        }

        [Test]
        public void PreCancelled_DoesNotStart()
        {
            CameraManager.SwitchTo("a");
            Pump();

            using var source = new CancellationTokenSource();
            source.Cancel();

            Assert.Throws<OperationCanceledException>(
                () => CameraManager.SwitchToAsync("b", 2f, CameraBlendStyle.Linear, source.Token));

            Assert.AreEqual("a", CameraManager.ActiveCameraId);
            Assert.AreEqual(1, CameraManager.StackDepth);
        }

        [Test]
        public void NonFiniteDuration_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CameraManager.SwitchToAsync("b", float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CameraManager.PopAsync(float.PositiveInfinity));
        }

        #endregion
    }
}
