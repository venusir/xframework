using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.TestTools;

namespace XFramework.XCamera.Tests
{
    /// <summary>
    /// 中立导演层的栈 / 注册 / 快照语义；用 <see cref="FakeCameraBackend"/> 隔离场景。
    /// </summary>
    [TestFixture]
    public class CameraDirectorTests
    {
        #region Fixture

        private GameObject _cameraObject;
        private CinemachineCamera _camera;
        private GameObject _cameraObject2;
        private CinemachineCamera _camera2;
        private FakeCameraBackend _backend;
        private CameraDirector _director;

        [SetUp]
        public void SetUp()
        {
            _cameraObject = new GameObject("DirectorCameraA");
            _camera = _cameraObject.AddComponent<CinemachineCamera>();
            _cameraObject2 = new GameObject("DirectorCameraB");
            _camera2 = _cameraObject2.AddComponent<CinemachineCamera>();

            _backend = new FakeCameraBackend();
            _director = new CameraDirector(_backend);
        }

        [TearDown]
        public void TearDown()
        {
            _director?.Dispose();
            if (_cameraObject != null)
                UnityEngine.Object.DestroyImmediate(_cameraObject);
            if (_cameraObject2 != null)
                UnityEngine.Object.DestroyImmediate(_cameraObject2);
        }

        #endregion

        #region Registry / ids

        [Test]
        public void Register_ExplicitAndGeneratedIds()
        {
            Assert.AreEqual("a", _director.Register("a", _camera));
            Assert.IsFalse(_backend.LastRegisteredIsGenerated);

            var first = _director.Register(_camera2);
            Assert.AreEqual("@auto/1", first);
            Assert.IsTrue(_backend.LastRegisteredIsGenerated);

            var thirdObject = new GameObject("DirectorCameraC");
            try
            {
                var second = _director.Register(thirdObject.AddComponent<CinemachineCamera>());
                Assert.AreEqual("@auto/2", second);
                Assert.AreNotEqual(first, second);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(thirdObject);
            }
        }

        [Test]
        public void Register_InvalidIds_Throw()
        {
            Assert.Throws<ArgumentException>(() => _director.Register("", _camera));
            Assert.Throws<ArgumentException>(() => _director.Register("   ", _camera));
            Assert.Throws<ArgumentException>(() => _director.Register("@auto/1", _camera));
            Assert.Throws<ArgumentNullException>(() => _director.Register((string)null, _camera));
        }

        [Test]
        public void TryGetCamera_TypeMatches()
        {
            _director.Register("a", _camera);

            Assert.IsTrue(_director.TryGetCamera("a", out CinemachineVirtualCameraBase found));
            Assert.AreSame(_camera, found);
            Assert.IsFalse(_director.TryGetCamera("unknown", out CinemachineVirtualCameraBase _));
            Assert.Throws<ArgumentNullException>(
                () => _director.TryGetCamera(null, out CinemachineVirtualCameraBase _));
        }

        #endregion

        #region Register & activate

        [Test]
        public void RegisterAndSwitchTo_RegistersGeneratedIdThenActivates()
        {
            var id = _director.RegisterAndSwitchTo(_camera, 0f, CameraBlendStyle.Cut);

            Assert.AreEqual("@auto/1", id);
            Assert.AreEqual(id, _backend.LastRegisteredId);
            Assert.IsTrue(_backend.LastRegisteredIsGenerated);
            Assert.AreSame(_camera, _backend.LastRegistered);
            Assert.AreEqual(id, _backend.LastActivatedId);
            Assert.AreEqual(1, _backend.ActivateCount);
            Assert.AreEqual(id, _director.BaseCameraId);
            Assert.AreEqual(1, _director.StackDepth);
        }

        [Test]
        public void RegisterAndPush_RegistersGeneratedIdThenActivates()
        {
            var id = _director.RegisterAndPush(_camera, 0.3f, CameraBlendStyle.Linear);

            Assert.AreEqual("@auto/1", id);
            Assert.IsTrue(_backend.LastRegisteredIsGenerated);
            Assert.AreEqual(id, _backend.LastActivatedId);
            Assert.AreEqual(0.3f, _backend.LastActivateDuration, 0.0001f);
            Assert.AreEqual(CameraBlendStyle.Linear, _backend.LastActivateStyle);
            Assert.AreEqual(1, _backend.ActivateCount);
            Assert.AreEqual(id, _director.BaseCameraId, "空栈 Push 成为基础相机");
            Assert.AreEqual(1, _director.StackDepth);
        }

        [Test]
        public void RegisterAndSwitchTo_InvalidDuration_DoesNotRegisterOrActivate()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _director.RegisterAndSwitchTo(_camera, float.NaN, CameraBlendStyle.Cut));

            Assert.AreEqual(0, _backend.CameraCount);
            Assert.AreEqual(0, _backend.ActivateCount);
            Assert.AreEqual(0, _director.StackDepth);
        }

        #endregion

        #region Stack

        [Test]
        public void SwitchTo_SetsBaseAndActivates()
        {
            _director.Register("a", _camera);
            _director.Register("b", _camera2);

            _director.SwitchTo("a", 0f, CameraBlendStyle.Cut);

            Assert.AreEqual("a", _backend.LastActivatedId);
            Assert.AreEqual("a", _director.BaseCameraId);
            Assert.AreEqual("a", _director.ActiveCameraId);
            Assert.AreEqual(1, _director.StackDepth);
        }

        [Test]
        public void Push_Pop_RestoresPrevious()
        {
            _director.Register("a", _camera);
            _director.Register("b", _camera2);
            _director.SwitchTo("a", 0f, CameraBlendStyle.Cut);

            _director.Push("b", 0.2f, CameraBlendStyle.Linear);
            Assert.AreEqual("b", _backend.LastActivatedId);
            Assert.AreEqual(2, _director.StackDepth);

            _director.Pop(0.25f, CameraBlendStyle.EaseIn);
            Assert.AreEqual("a", _backend.LastActivatedId);
            Assert.AreEqual(0.25f, _backend.LastActivateDuration, 0.0001f);
            Assert.AreEqual(CameraBlendStyle.EaseIn, _backend.LastActivateStyle);
            Assert.AreEqual(1, _director.StackDepth);
        }

        [Test]
        public void SwitchTo_ClearsOverrides()
        {
            _director.Register("a", _camera);
            _director.Register("b", _camera2);
            _director.SwitchTo("a", 0f, CameraBlendStyle.Cut);
            _director.Push("b", 0f, CameraBlendStyle.Cut);

            _director.SwitchTo("a", 0f, CameraBlendStyle.Cut);

            Assert.AreEqual(1, _director.StackDepth);
            Assert.AreEqual("a", _director.BaseCameraId);
        }

        [Test]
        public void PopAll_KeepsOnlyBase()
        {
            _director.Register("a", _camera);
            _director.Register("b", _camera2);
            _director.SwitchTo("a", 0f, CameraBlendStyle.Cut);
            _director.Push("b", 0f, CameraBlendStyle.Cut);

            _director.PopAll(0.4f, CameraBlendStyle.Linear);

            Assert.AreEqual(1, _director.StackDepth);
            Assert.AreEqual("a", _backend.LastActivatedId);
            Assert.AreEqual(0.4f, _backend.LastActivateDuration, 0.0001f);
        }

        [Test]
        public void Pop_WithOnlyBase_Warns()
        {
            _director.Register("a", _camera);
            _director.SwitchTo("a", 0f, CameraBlendStyle.Cut);
            var count = _backend.ActivateCount;

            LogAssert.Expect(LogType.Warning, new Regex("没有可弹出的覆盖层.*"));
            _director.Pop(0f, CameraBlendStyle.Cut);

            Assert.AreEqual(count, _backend.ActivateCount);
        }

        [Test]
        public void Pop_DeadTop_DoesNotPopNextLiveLayer()
        {
            var thirdObject = new GameObject("DirectorCameraC");
            try
            {
                var third = thirdObject.AddComponent<CinemachineCamera>();
                _director.Register("a", _camera);
                _director.Register("b", _camera2);
                _director.Register("c", third);
                _director.SwitchTo("a", 0f, CameraBlendStyle.Cut);
                _director.Push("b", 0f, CameraBlendStyle.Cut);
                _director.Push("c", 0f, CameraBlendStyle.Cut);

                UnityEngine.Object.DestroyImmediate(thirdObject);

                _director.Pop(0f, CameraBlendStyle.Cut);

                Assert.AreEqual(2, _director.StackDepth,
                    "死栈顶已经离开，Pop 不应再弹掉存活的下一层");
                Assert.AreEqual("b", _director.ActiveCameraId);
            }
            finally
            {
                if (thirdObject != null)
                    UnityEngine.Object.DestroyImmediate(thirdObject);
            }
        }

        [Test]
        public void PopAsync_DeadTop_DoesNotPopNextLiveLayer()
        {
            var thirdObject = new GameObject("DirectorCameraC");
            try
            {
                var third = thirdObject.AddComponent<CinemachineCamera>();
                _director.Register("a", _camera);
                _director.Register("b", _camera2);
                _director.Register("c", third);
                _director.SwitchTo("a", 0f, CameraBlendStyle.Cut);
                _director.Push("b", 0f, CameraBlendStyle.Cut);
                _director.Push("c", 0f, CameraBlendStyle.Cut);

                UnityEngine.Object.DestroyImmediate(thirdObject);

                var result = _director.PopAsync(0f, CameraBlendStyle.Cut, default)
                    .GetAwaiter().GetResult();

                Assert.AreEqual(CameraTransitionResult.Completed, result);
                Assert.AreEqual(2, _director.StackDepth,
                    "死栈顶已经离开，PopAsync 不应再弹掉存活的下一层");
                Assert.AreEqual("b", _director.ActiveCameraId);
            }
            finally
            {
                if (thirdObject != null)
                    UnityEngine.Object.DestroyImmediate(thirdObject);
            }
        }

        [Test]
        public void Clear_EmptiesStack()
        {
            _director.Register("a", _camera);
            _director.SwitchTo("a", 0f, CameraBlendStyle.Cut);

            _director.Clear();

            Assert.AreEqual(0, _director.StackDepth);
            Assert.IsNull(_director.BaseCameraId);
            Assert.AreEqual(1, _backend.ClearActiveCount);
        }

        [Test]
        public void UnknownId_WarnsAndNoOps()
        {
            var count = _backend.ActivateCount;

            LogAssert.Expect(LogType.Warning, new Regex("未知相机 id: missing.*"));
            _director.SwitchTo("missing", 0f, CameraBlendStyle.Cut);

            Assert.AreEqual(count, _backend.ActivateCount);
        }

        [Test]
        public void NonFiniteDuration_Throws()
        {
            _director.Register("a", _camera);

            Assert.Throws<ArgumentOutOfRangeException>(
                () => _director.SwitchTo("a", float.NaN, CameraBlendStyle.Cut));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _director.Push("a", float.PositiveInfinity, CameraBlendStyle.Cut));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _director.PopAll(float.NegativeInfinity, CameraBlendStyle.Cut));
        }

        #endregion

        #region Unregister

        [Test]
        public void Unregister_Top_ActivatesNewTop()
        {
            _director.Register("a", _camera);
            _director.Register("b", _camera2);
            _director.SwitchTo("a", 0f, CameraBlendStyle.Cut);
            _director.Push("b", 0f, CameraBlendStyle.Cut);

            Assert.IsTrue(_director.Unregister("b"));

            Assert.AreEqual("a", _backend.LastActivatedId);
            Assert.AreEqual(1, _director.StackDepth);
        }

        [Test]
        public void Unregister_Base_PromotesNext()
        {
            _director.Register("a", _camera);
            _director.Register("b", _camera2);
            _director.SwitchTo("a", 0f, CameraBlendStyle.Cut);
            _director.Push("b", 0f, CameraBlendStyle.Cut);

            Assert.IsTrue(_director.Unregister("a"));

            Assert.AreEqual(1, _director.StackDepth);
            Assert.AreEqual("b", _director.BaseCameraId);
        }

        [Test]
        public void Unregister_Middle_DoesNotChangeActive()
        {
            _director.Register("a", _camera);
            _director.Register("b", _camera2);
            var thirdObject = new GameObject("DirectorCameraC");
            try
            {
                _director.Register("c", thirdObject.AddComponent<CinemachineCamera>());
                _director.SwitchTo("a", 0f, CameraBlendStyle.Cut);
                _director.Push("b", 0f, CameraBlendStyle.Cut);
                _director.Push("c", 0f, CameraBlendStyle.Cut);
                var count = _backend.ActivateCount;

                Assert.IsTrue(_director.Unregister("b"));

                Assert.AreEqual(count, _backend.ActivateCount);
                Assert.AreEqual(2, _director.StackDepth);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(thirdObject);
            }
        }

        [Test]
        public void Unregister_Unknown_ReturnsFalse()
        {
            Assert.IsFalse(_director.Unregister("missing"));
        }

        [Test]
        public void Deactivated_DeadTop_IsPrunedAndFallsBack()
        {
            _director.Register("a", _camera);
            _director.Register("b", _camera2);
            _director.SwitchTo("a", 0f, CameraBlendStyle.Cut);
            _director.Push("b", 0f, CameraBlendStyle.Cut);
            var count = _backend.ActivateCount;

            UnityEngine.Object.DestroyImmediate(_cameraObject2);
            _backend.RaiseDeactivated("b");

            Assert.AreEqual(1, _director.StackDepth,
                "已销毁的栈顶应在反激活事件后被剪除");
            Assert.AreEqual("a", _director.BaseCameraId);
            Assert.AreEqual(count + 1, _backend.ActivateCount,
                "剪除栈顶后应回到新栈顶");
            Assert.AreEqual("a", _backend.LastActivatedId);
        }

        [Test]
        public void DeadCamera_IsTreatedAsUnavailable()
        {
            var deadObject = new GameObject("DirectorDead");
            var deadCamera = deadObject.AddComponent<CinemachineCamera>();
            _director.Register("dead", deadCamera);
            UnityEngine.Object.DestroyImmediate(deadObject);

            var count = _backend.ActivateCount;
            LogAssert.Expect(LogType.Warning, new Regex("已被外部销毁.*"));
            _director.SwitchTo("dead", 0f, CameraBlendStyle.Cut);
            Assert.AreEqual(count, _backend.ActivateCount);

            var buffer = new List<CameraInfo>();
            _director.CopyCameras(buffer);
            var info = buffer.Find(item => item.CameraId == "dead");
            Assert.IsFalse(info.IsAlive);

            Assert.IsTrue(_director.Unregister("dead"));
        }

        #endregion

        #region Queries / diagnostics

        [Test]
        public void IsExternallyControlled_WhenActiveIsNotTop()
        {
            _director.Register("a", _camera);
            _director.SwitchTo("a", 0f, CameraBlendStyle.Cut);

            _backend.ActiveCameraId = "external";
            _backend.HasActiveCamera = true;
            Assert.IsTrue(_director.IsExternallyControlled);

            _backend.TransitionTargetId = "a";
            Assert.IsFalse(_director.IsExternallyControlled);
        }

        [Test]
        public void GetState_ComposesBackendAndStack()
        {
            _director.Register("a", _camera);
            _director.Register("b", _camera2);
            _director.SwitchTo("a", 0f, CameraBlendStyle.Cut);
            _director.Push("b", 0f, CameraBlendStyle.Cut);

            _backend.IsTransitioning = true;
            _backend.IsBlending = true;
            _backend.TransitionTargetId = "b";
            _backend.TransitionProgress = 0.5f;

            var state = _director.GetState();

            Assert.AreEqual(2, state.RegisteredCount);
            Assert.AreEqual(2, state.NamedCount);
            Assert.AreEqual(0, state.GeneratedCount);
            Assert.AreEqual("a", state.BaseCameraId);
            Assert.AreEqual("b", state.ActiveCameraId);
            Assert.AreEqual(2, state.StackDepth);
            Assert.IsTrue(state.IsTransitioning);
            Assert.AreEqual("b", state.TransitionTargetId);
            Assert.IsTrue(state.IsBrainBlending);
            Assert.AreEqual(0.5f, state.TransitionProgress, 0.0001f);
            Assert.IsFalse(state.IsExternallyControlled);
        }

        [Test]
        public void CopyCameras_MarksBaseStackActiveAndGenerated()
        {
            _director.Register("a", _camera);
            var generated = _director.Register(_camera2);
            _director.SwitchTo("a", 0f, CameraBlendStyle.Cut);
            _director.Push(generated, 0f, CameraBlendStyle.Cut);

            var buffer = new List<CameraInfo>();
            Assert.AreEqual(2, _director.CopyCameras(buffer));

            var baseInfo = buffer[0];
            Assert.AreEqual("a", baseInfo.CameraId);
            Assert.IsTrue(baseInfo.IsBase);
            Assert.IsTrue(baseInfo.IsInStack);
            Assert.IsFalse(baseInfo.IsActive);
            Assert.IsFalse(baseInfo.IsGenerated);

            var generatedInfo = buffer[1];
            Assert.AreEqual(generated, generatedInfo.CameraId);
            Assert.IsTrue(generatedInfo.IsGenerated);
            Assert.IsFalse(generatedInfo.IsBase);
            Assert.IsTrue(generatedInfo.IsInStack);
            Assert.IsTrue(generatedInfo.IsActive);
            Assert.IsTrue(generatedInfo.IsAlive);
        }

        [Test]
        public void CopyCameras_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _director.CopyCameras(null));
        }

        #endregion

        #region Lifecycle

        [Test]
        public void Dispose_DisposesBackend()
        {
            _director.Dispose();

            Assert.IsTrue(_backend.Disposed);
        }

        [Test]
        public void ResetForNewSession_UnbindsWithoutDisposing()
        {
            _director.ResetForNewSession();

            Assert.AreEqual(1, _backend.UnbindCount);
            Assert.IsFalse(_backend.Disposed);
        }

        #endregion
    }
}
