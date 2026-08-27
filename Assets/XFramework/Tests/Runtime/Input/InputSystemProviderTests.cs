using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.TestTools;
using XFramework.XInput.Default;

namespace XFramework.XInput.Tests
{
    /// <summary>
    /// InputSystemProvider 单元测试。
    /// <para>经 InternalsVisibleTo 直接构造 internal 实现,以 InputTestFixture 模拟设备与按键;
    /// 覆盖:C5 Action 缓存跨 Map 修复、C6 raw 语义、C7 重绑定语义、绑定覆盖持久化、
    /// Initialize 异常契约、C11 多玩家设备隔离、C12 GamepadType 纯函数。</para>
    /// <para>注:Resources 加载失败路径(InvalidOperationException)依赖真实资源缺失,编辑器测试环境
    /// 下项目内存在真实资源无法复现,留手工验证(删除 Resources/InputSystem_Actions.inputactions 后
    /// Initialize 应抛 [Input] 前缀异常)。</para>
    /// </summary>
    [TestFixture]
    public class InputSystemProviderTests : InputTestFixture
    {
        #region Private

        /// <summary>构造单 map 单 action 的程序化资产(测试专用)。</summary>
        private static InputActionAsset CreateSingleActionAsset(string actionName, InputActionType type, string binding, string processors = null)
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = new InputActionMap("Player");
            map.AddAction(actionName, type, binding, processors: processors);
            asset.AddActionMap(map);
            return asset;
        }

        #endregion

        #region Initialize 契约

        [Test]
        public void Initialize_NullAsset_Throws()
        {
            var provider = new InputSystemProvider();
            Assert.Throws<ArgumentNullException>(() => provider.Initialize((InputActionAsset)null));
        }

        [Test]
        public void Initialize_AssetInjected_ReadsWork()
        {
            InputSystem.AddDevice<Keyboard>();
            var asset = CreateSingleActionAsset("Jump", InputActionType.Button, "<Keyboard>/space");
            var provider = new InputSystemProvider();
            provider.Initialize(asset);

            Press(Keyboard.current.spaceKey);
            Assert.IsTrue(provider.WasPressedThisFrame("Jump"), "注入资产后应能正常读取");
            Release(Keyboard.current.spaceKey);

            provider.Dispose();
        }

        #endregion

        #region C5 Action 缓存跨 Map 修复

        [Test]
        public void GetAction_SameNameAcrossMaps_SwitchMapRefreshesCache()
        {
            InputSystem.AddDevice<Keyboard>();
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            var playerMap = new InputActionMap("Player");
            playerMap.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
            var uiMap = new InputActionMap("UI");
            uiMap.AddAction("Jump", InputActionType.Button, "<Keyboard>/a");
            asset.AddActionMap(playerMap);
            asset.AddActionMap(uiMap);

            var provider = new InputSystemProvider();
            provider.Initialize(asset); // 默认启用 Player map

            // Player map:空格触发
            Press(Keyboard.current.spaceKey);
            Assert.IsTrue(provider.WasPressedThisFrame("Jump"), "Player map 下空格应触发 Jump");
            Release(Keyboard.current.spaceKey);

            // 切到 UI map:缓存必须失效,否则读到已禁用 Player map 的同名 Action
            provider.SwitchActionMap("UI");
            Press(Keyboard.current.aKey);
            Assert.IsTrue(provider.WasPressedThisFrame("Jump"), "UI map 下 A 键应触发 Jump");
            Release(Keyboard.current.aKey);

            // 切回 Player map
            provider.SwitchActionMap("Player");
            Press(Keyboard.current.spaceKey);
            Assert.IsTrue(provider.WasPressedThisFrame("Jump"), "切回 Player map 后空格恢复触发");
            Release(Keyboard.current.spaceKey);

            provider.Dispose();
        }

        #endregion

        #region C6 raw 语义

        [Test]
        public void ReadFloatRaw_BypassesDeadzoneProcessor()
        {
            var gamepad = InputSystem.AddDevice<Gamepad>();
            // 死区下限 0.5:0.3 低于死区 → 平滑值归零,raw 直读控件保留 0.3。
            // 注意:内置处理器注册名为 AxisDeadzone(float)/StickDeadzone(Vector2),不存在 "deadzone" 这个名字
            var asset = CreateSingleActionAsset("Throttle", InputActionType.Value, "<Gamepad>/rightTrigger", processors: "AxisDeadzone(min=0.5)");
            var provider = new InputSystemProvider();
            provider.Initialize(asset);

            Set(gamepad.rightTrigger, 0.3f);

            Assert.AreEqual(0.3f, provider.ReadFloatRaw("Throttle"), 0.001f, "raw 直读控件值,绕过绑定处理器");
            Assert.AreEqual(0f, provider.ReadFloat("Throttle"), 0.001f, "平滑值经死区处理被归零");

            provider.Dispose();
        }

        #endregion

        #region C7 重绑定语义

        [Test]
        public void StartRebinding_InvalidBindingId_ReturnsNull()
        {
            var asset = CreateSingleActionAsset("Jump", InputActionType.Button, "<Keyboard>/space");
            var provider = new InputSystemProvider();
            provider.Initialize(asset);

            // 非法 id:显式报错返回 null,不得静默回退覆盖第一个绑定(防止持久化数据与资产失配时改错键位)。
            // 固定 id 使报错消息可断言;LogAssert.Expect 先注册预期,否则 Test Runner 将未预期 Error 日志判失败
            const string invalidId = "not-a-real-binding-id";
            LogAssert.Expect(LogType.Error, $"[Input] StartRebinding failed: action 'Jump' has no binding with id '{invalidId}'.");
            Assert.IsNull(provider.StartRebinding("Jump", invalidId));

            provider.Dispose();
        }

        [Test]
        public void StartRebinding_EmptyBindingId_FallsBackToFirstBindable()
        {
            var asset = CreateSingleActionAsset("Jump", InputActionType.Button, "<Keyboard>/space");
            var provider = new InputSystemProvider();
            provider.Initialize(asset);

            // null/空串:回退覆盖第一个可绑定索引,返回可取消的操作句柄。
            // 此时 Player map 处于启用态——重绑定要求 action 禁用(否则包内 WithAction 抛异常),
            // 内部临时禁用并在操作结束后恢复,测试隐式覆盖这条路径
            var op = provider.StartRebinding("Jump", null);
            Assert.IsNotNull(op, "bindingId 为空时应回退第一个可绑定索引并返回操作句柄");
            Assert.IsTrue(op.IsActive);
            op.Cancel();
            Assert.IsTrue(asset.FindAction("Jump", true).enabled, "重绑定取消后 action 应恢复启用态");

            provider.Dispose();
        }

        #endregion

        #region 绑定覆盖持久化

        [Test]
        public void BindingOverrides_RoundTrip_ReloadRestores()
        {
            InputSystem.AddDevice<Keyboard>();
            var asset = CreateSingleActionAsset("Jump", InputActionType.Button, "<Keyboard>/space");
            var provider = new InputSystemProvider();
            provider.Initialize(asset);

            Assert.AreEqual(string.Empty, provider.SaveBindingOverrides(), "无覆盖时序列化为空串");

            // 程序化应用覆盖(等价于重绑定后的结果)
            asset.FindAction("Jump", true).ApplyBindingOverride(0, "<Keyboard>/q");
            var data = provider.SaveBindingOverrides();
            Assert.IsFalse(string.IsNullOrEmpty(data), "存在覆盖时应序列化出数据");

            // 模拟"保存后丢弃内存覆盖":恢复默认空格绑定
            provider.ResetAllBindingOverrides();
            Press(Keyboard.current.spaceKey);
            Assert.IsTrue(provider.WasPressedThisFrame("Jump"), "覆盖清除后恢复默认空格绑定");
            Release(Keyboard.current.spaceKey);
            provider.Dispose();

            // 新 provider 从序列化数据恢复覆盖
            var provider2 = new InputSystemProvider();
            provider2.Initialize(asset);
            provider2.LoadBindingOverrides(data);

            Press(Keyboard.current.qKey);
            Assert.IsTrue(provider2.WasPressedThisFrame("Jump"), "恢复覆盖后 Q 键应触发 Jump");
            Release(Keyboard.current.qKey);
            Press(Keyboard.current.spaceKey);
            Assert.IsFalse(provider2.WasPressedThisFrame("Jump"), "默认空格绑定已被覆盖,不再触发");
            Release(Keyboard.current.spaceKey);

            provider2.Dispose();
        }

        #endregion

        #region C11 多玩家设备隔离

        [Test]
        public void MultiPlayer_GamepadIsolation_ByConnectionOrder()
        {
            var gamepadA = InputSystem.AddDevice<Gamepad>();
            var gamepadB = InputSystem.AddDevice<Gamepad>();
            var asset = CreateSingleActionAsset("Jump", InputActionType.Button, "<Gamepad>/buttonSouth");
            var provider = new InputSystemProvider();
            provider.Initialize(asset);

            // 输入驱动 current:最后按下的手柄成为 player 0;playerId 1 起跳过 current、按连接顺序取另一只手柄
            Set(gamepadA.buttonSouth, 1f);
            Assert.IsTrue(provider.WasPressedThisFrame("Jump", 0), "手柄 A 成为 current,player 0 应读到手柄 A 的按下");
            Assert.IsFalse(provider.WasPressedThisFrame("Jump", 1), "player 1 跳过 current(A) 取手柄 B,B 未按下");
            Assert.IsFalse(provider.WasPressedThisFrame("Jump", 2), "只有两只手柄,playerId 2 无设备,返回默认值");
            Set(gamepadA.buttonSouth, 0f);

            // current 随最后输入翻转:手柄 B 成为 player 0,player 1 互斥映射到手柄 A
            Set(gamepadB.buttonSouth, 1f);
            Assert.IsTrue(provider.WasPressedThisFrame("Jump", 0), "手柄 B 成为 current,player 0 应读到手柄 B 的按下");
            Assert.IsFalse(provider.WasPressedThisFrame("Jump", 1), "player 1 跳过 current(B) 取手柄 A,A 未按下");
            Set(gamepadB.buttonSouth, 0f);

            // 互斥不变量:玩家 0/1 永不映射同一只手柄(同帧只按 A 时,仅玩家 0 响应)
            Set(gamepadA.buttonSouth, 1f);
            Assert.IsTrue(provider.WasPressedThisFrame("Jump", 0), "A 为 current,玩家 0 响应");
            Assert.IsFalse(provider.WasPressedThisFrame("Jump", 1), "玩家 1 的手柄(B)未按下,不响应");
            Set(gamepadA.buttonSouth, 0f);

            provider.Dispose();
        }

        [Test]
        public void MultiPlayer_KeyboardOnlyServesPlayerZero()
        {
            InputSystem.AddDevice<Keyboard>();
            var asset = CreateSingleActionAsset("Jump", InputActionType.Button, "<Keyboard>/space");
            var provider = new InputSystemProvider();
            provider.Initialize(asset);

            Press(Keyboard.current.spaceKey);
            Assert.IsTrue(provider.WasPressedThisFrame("Jump", 0), "0 号玩家响应键鼠");
            Assert.IsFalse(provider.WasPressedThisFrame("Jump", 1), "playerId > 0 无 Gamepad 绑定,不响应键鼠");
            Release(Keyboard.current.spaceKey);

            provider.Dispose();
        }

        #endregion

        #region C12 GamepadType 纯函数

        [Test]
        public void DetectGamepadType_ByCapabilityIds()
        {
            Assert.AreEqual(GamepadType.Xbox, Detect("{\"vendorId\":1118,\"productId\":654}"), "Xbox 厂商 ID");
            Assert.AreEqual(GamepadType.PlayStation4, Detect("{\"vendorId\":1356,\"productId\":1476}"), "DualShock 4");
            Assert.AreEqual(GamepadType.PlayStation5, Detect("{\"vendorId\":1356,\"productId\":3302}"), "DualSense");
            Assert.AreEqual(GamepadType.SwitchPro, Detect("{\"vendorId\":1406,\"productId\":8201}"), "Switch Pro Controller");
        }

        [Test]
        public void DetectGamepadType_HexCapabilities_NotParsed_FallsBackToProduct()
        {
            // 包内 JsonParser 的 ParseNumber 仅按 char.IsDigit 解析,不支持 0x 十六进制前缀;
            // 真实 HID 描述符经 JsonUtility.ToJson 序列化为十进制(HIDDeviceDescriptor.ToJson),hex 不会在实际数据中出现。
            // 断言 hex 输入不命中查表、按产品名优雅降级,防止有人误以 hex 数据驱动此逻辑。
            Assert.AreEqual(GamepadType.Generic, Detect("{\"vendorId\":0x54C,\"productId\":0x5C4}"), "hex 无法解析且无产品名可回退 → Generic");
            // 回退匹配读 description.product 字段(与 capabilities JSON 无关):模拟真实设备同时带 hex 能力串与产品名
            Assert.AreEqual(GamepadType.PlayStation4,
                InputSystemProvider.DetectGamepadTypeFrom(new InputDeviceDescription
                {
                    capabilities = "{\"vendorId\":0x54C,\"productId\":0x5C4}",
                    product = "DUALSHOCK4 Wireless Controller",
                }),
                "hex 无法解析时回退产品名关键词匹配");
        }

        [Test]
        public void DetectGamepadType_UnknownIds_FallbackToProductName()
        {
            Assert.AreEqual(GamepadType.PlayStation5, DetectProduct("DualSense Wireless Controller"));
            Assert.AreEqual(GamepadType.PlayStation4, DetectProduct("DUALSHOCK4 Wireless Controller"));
            Assert.AreEqual(GamepadType.Xbox, DetectProduct("Xbox Wireless Controller"));
            Assert.AreEqual(GamepadType.SwitchPro, DetectProduct("Nintendo Switch Pro Controller"));
            // 无 ID 且无关键词 → Generic;「Wireless Controller」不再被长度启发式误判为 PS4
            Assert.AreEqual(GamepadType.Generic, DetectProduct("Wireless Controller"));
            Assert.AreEqual(GamepadType.Generic, InputSystemProvider.DetectGamepadTypeFrom(new InputDeviceDescription()));
        }

        private static GamepadType Detect(string capabilities)
            => InputSystemProvider.DetectGamepadTypeFrom(new InputDeviceDescription { capabilities = capabilities });

        private static GamepadType DetectProduct(string product)
            => InputSystemProvider.DetectGamepadTypeFrom(new InputDeviceDescription { product = product });

        #endregion
    }
}
