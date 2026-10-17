# Cinemachine Camera —— `ICameraManager` 的 Cinemachine 3.x 实现

## 概述

`CinemachineCameraManager` 把 Cinemachine 的 vcam 优先级与 `CinemachineBrain` 混合接进核心包的
`ICameraManager`：游戏代码只调 `CameraManager`，不直接依赖 Cinemachine 类型。

**本包是适配器，不是行为库**：跟随 / 瞄准 / 震屏 / 缩放 / 碰撞 / 约束 / Dolly / Target Group / Timeline
由 Cinemachine 提供并在 Cinemachine 里配置；本包只负责注册、优先级 / Brain 混合与状态查询。

**命名 / 动态双键**：常驻 vcam 用 string id；随预制创建 / 销毁的动态 vcam 用 `CameraHandle`（`Register<T>`）。
**Timeline / 过场相机**由过场系统接管 Brain，不经过本管理器；Brain 的活动相机未注册时，激活 / 过渡会告警
「可能与过场冲突」。

**命名空间**：`XFramework.XCamera.Cinemachine`。**只支持 Cinemachine 3.x**（按 3.1.7 编写）；2.x 的类型名与
字段名不同，需要自己的 `ICameraManager` 实现。

## 快速使用

包级安装（先核心包、后本包、Cinemachine 3.1.7）见同包的 `README.md`。接线：

```csharp
using System.Collections.Generic;
using Unity.Cinemachine;
using XFramework.XCamera;
using XFramework.XCamera.Cinemachine;

var manager = new CinemachineCameraManager(brain, new Dictionary<string, CinemachineVirtualCameraBase>
{
    ["main"] = mainVcam,
    ["battle"] = battleVcam,
});
CameraManager.Initialize(manager);

CameraManager.Activate("main");
CameraManager.TransitionTo("battle", 0.8f, CameraBlendStyle.EaseInOut);

// 动态 vcam（随预制创建 / 销毁）：用句柄，不生成 string id
var handle = CameraManager.Register(dynamicVcam);
CameraManager.Activate(handle);
// 预制销毁时
CameraManager.Unregister(handle); // 或 handle.Dispose()
```

## 模块结构

```text
Runtime/
├── CinemachineCameraManager.cs      # 公开：ICameraManager 的 Cinemachine 3.x 实现
├── Venusy609.Xframework.Cinemachine.asmdef  # 引用核心程序集与 Unity.Cinemachine
└── README.md
```

## 行为契约

- 仅主线程；注册表、反查表与场景状态不是线程安全的。
- `Activate`：目标 `Priority = int.MaxValue`、其余已注册 vcam `Priority = 0`；目标 GameObject 激活；
  `brain.DefaultBlend = Cut`。**若 Brain 正在混合且目标已是当前活动相机**，短暂失活 / 激活目标 GameObject
  强制重新入队，确保是 cut（副作用：该 vcam 的 `PreviousStateIsValid` 重置）。
- `Deactivate`：目标 GameObject 失活；Brain 按优先级队列选下一个；回退使用的 `DefaultBlend` 是当前值。
- `TransitionTo`：设置 `brain.DefaultBlend`（style / duration），调整优先级并激活目标；Brain 负责混合。
  `duration <= 0` 或 `Cut` 等价于 `Activate`。
- **命名 / 动态双键**：常驻 vcam 用 string id；动态 vcam 用 `CameraHandle`（`Register<T>` 返回）。
  `ActiveHandle` 对命名与动态都有效；动态 vcam 活动时 `ActiveHandle.NamedId` / `CameraManager.ActiveCameraId`
  为 `null`；`CameraIds` 只列命名 vcam。
- `Register<T>(vcam)` 注册动态 vcam（**不激活**、不改场景）；类型不符抛 `ArgumentException`，`null` 抛
  `ArgumentNullException`；`Unregister(handle)` 只更新注册表与反查表，不改变场景。
- 无效 / 已注销 / `default` 句柄：激活 / 过渡 / 反激活告警并 no-op；`IsActive` 返回 `false`；
  `Unregister` 返回 `false`。
- 未知 id：告警并 no-op，不抛；`null` id 抛 `ArgumentNullException`。
- `ActiveHandle` = `brain.ActiveVirtualCamera` 反查槽位句柄（`NamedId` 即命名 id，动态为 `null`）；
  `CameraManager.ActiveCameraId` 是门面便利（= `ActiveHandle.NamedId`）；`IsTransitioning` =
  `brain.IsBlending`；`OutputCamera` = `brain.OutputCamera`；`CameraIds` = 命名注册表键视图。
- `Register` / `Unregister` 只更新注册表与反查表，不改变场景；替换 / 注销活动相机前请先 `Deactivate`。
- **Timeline / 过场**：过场相机不注册；若 Brain 的活动相机未注册（Timeline / 外来 vcam），
  `Activate` / `TransitionTo`（非 cut）会告警「可能与过场冲突」。`CameraManager.ActiveCameraId` /
  `ActiveHandle` 对这种未注册活动相机返回 `null` / `default`；`OutputCamera` / `IsTransitioning` 仍可用。

## 架构设计

`CinemachineCameraManager` 是核心包 `ICameraManager` 的一个后端实现：它不驱动任何调度器，Brain 自己按
`UpdateMethod` 混合；管理器只负责优先级、默认混合与状态反查。

## API 参考

| 成员 | 行为 |
|---|---|
| `CinemachineCameraManager(CinemachineBrain, IReadOnlyDictionary<string, CinemachineVirtualCameraBase>)` | 构造；复制命名注册表 |
| `Register(string, CinemachineVirtualCameraBase)` | 命名注册 / 替换（不动场景） |
| `Register<T>(T)` | 动态注册，返回 `CameraHandle`（不激活、不动场景） |
| `Unregister(string)` / `Unregister(CameraHandle)` | 注销（不动场景） |
| `OutputCamera` / `CameraIds` / `ActiveHandle`（`NamedId`）/ `IsTransitioning` | 查询（命名 id 走 `CameraManager.ActiveCameraId`） |
| `Activate` / `Deactivate` / `TransitionTo` / `IsActive`（string 与 `CameraHandle` 重载） | 契约操作 |
| `Dispose()` | 清空注册表与反查表；不改变场景 |

## 典型场景

### 1. 进入战斗

```csharp
CameraManager.TransitionTo("battle", 0.8f, CameraBlendStyle.EaseInOut);
await UniTask.WaitUntil(() => !CameraManager.IsTransitioning);
```

### 2. 立即切镜（演出 / 传送）

```csharp
CameraManager.Activate("cutscene");
```

### 3. 回到主相机

```csharp
CameraManager.TransitionTo("main", 0.4f, CameraBlendStyle.EaseOut);
```

### 4. 动态 vcam（随预制创建 / 销毁）

```csharp
var handle = CameraManager.Register(dynamicVcam);
CameraManager.Activate(handle);
// 预制销毁时
handle.Dispose(); // 门面已销毁也安全
```

### 5. Timeline / 过场

过场 vcam 不注册；Timeline 接管 Brain。过场期间不要调用 `Activate` / `TransitionTo`；Brain 的活动相机
未注册时本包会告警。过场结束后由调用方恢复玩法 vcam。

## 自定义实现

本包本身就是核心包 `ICameraManager` 的一个实现。要接别的后端（2.x、Timeline、自研相机系统）：

- 实现 `ICameraManager`；注册在实现类自己的构造 / 注册方法里完成。
- 义务：仅主线程；未知 id 告警并 no-op，不抛；`null` id 抛 `ArgumentNullException`；
  `ActiveHandle`（`NamedId` 为命名 id）/ `IsTransitioning` / `OutputCamera` 与过渡目标一致；`Activate`
  取消在途过渡。
- 义务与边界的完整说明在核心包 `Runtime/Camera/README.md` 的「自定义实现」与「行为契约」。

## 内部机制

- 激活由 vcam 的 `Priority`（`PrioritySettings`，隐式 int 赋值会同时置 `Enabled`）与 Brain 的优先级队列决定。
- 过渡由 `brain.DefaultBlend` 决定；`CinemachineBlendDefinition.Styles` 映射核心包的 `CameraBlendStyle`。
- `Deactivate` / 活动变化由 Brain 的重选逻辑处理；管理器只读 `ActiveVirtualCamera` / `IsBlending`。

## 生命周期与清理

- 管理器不拥有 Brain / vcam；`Dispose` 只清空注册表与反查表。
- `CameraManager.Destroy` 会调用本实现的 `Dispose`（它实现了 `IDisposable`）。
- 场景卸载前无需特殊处理；下次会话重新构造即可。

## 线程契约

**全部 API 仅在主线程调用。** Brain 与 vcam 都是 Unity 场景对象；管理器不做跨线程同步。

## 性能代价

- `Activate` / `TransitionTo` 遍历注册表设置优先级（vcam 数量通常个位数），O(n)，无分配。
- 每帧成本归 Brain（优先级队列 + 混合），本管理器不在每帧路径上。
- `CameraIds` 返回字典键视图，不复制。

## 已知限制

- **只支持 Cinemachine 3.x**（3.1.7 编写）；2.x 需要自己的实现。
- **动态 vcam 的 `ActiveHandle.NamedId` / `CameraManager.ActiveCameraId` 为 `null`**：用 `ActiveHandle` /
  `IsActive(handle)`；`CameraIds` 只列命名 vcam。
- **Timeline / 过场不接管**：过场 vcam 不注册；Brain 被未注册的过场 vcam 控制时，`Activate` / `TransitionTo`
  会告警「可能与过场冲突」，`CameraManager.ActiveCameraId` / `ActiveHandle` 返回 `null` / `default`（`OutputCamera` /
  `IsTransitioning` 仍可用）。
- **`Unregister` 不改变场景**：动态 vcam 的 GameObject 由预制 / 场景销毁；`Unregister` 只移除槽位与反查表。
- **`brain.CustomBlends` 会覆盖** `TransitionTo` 请求的 style / duration；本包不强行清空项目配置。
- **未注册 vcam 的优先级不受管理**；不要让它们用高于管理器的优先级。
- **`Activate` 的强制 cut 会短暂失活 / 激活目标 GameObject**，重置该 vcam 的 `PreviousStateIsValid`。
- **`Deactivate` 的回退**由 Brain 决定，使用的 `DefaultBlend` 是当前值。
- 过渡到「已是活动相机」时不重启当前混合（Brain 的公开面没有强制完成 / 重开混合的入口）。

## 设计取舍

- **优先用 Brain 自己的能力**：管理器只切优先级与 `DefaultBlend`，不自己插值、不另建调度器。
- **不替 Cinemachine 做行为**：跟随 / 震屏 / 缩放在 Cinemachine 里配置，本包只做优先级与混合的适配；
  需要这些行为的项目装本包，但行为本身仍归 Cinemachine。
- **不劫持 Brain 的 custom blends**：项目配置优先；管理器只写 `DefaultBlend`。
- **动态 vcam 用 `CameraHandle` 而不是 string id**：随预制实例创建 / 销毁的 vcam 没有稳定 authoring id；
  句柄带代际，旧句柄自然失效。
- **过场不接管**：Timeline 已经接管 Brain；管理器再插一手只会冲突。未注册活动相机告警是提醒，不是阻断。
- **为什么在独立扩展包**：核心包保持零 Cinemachine 依赖，2.x / 2D / 自定义后端不被版本锁排除。

## 接口承诺到哪为止

- 核心包承诺的 `ActiveHandle`（`NamedId`）/ `IsTransitioning` / `OutputCamera` 语义在这里成立；过渡曲线、回退与
  中断的细节由 Cinemachine Brain 决定。
- `TransitionTo` 请求的 style / duration 是**建议**：`CustomBlends` 命中时会以项目配置为准。
- 不承诺「过渡到已是活动相机会重启混合」。

## 与相邻模块的关系

- **核心 Camera**：本包实现 `ICameraManager`；门面、查询与取消语义在核心包。
- **Update**：本包不注册 ticker；过渡由 Brain 的 `UpdateMethod` 驱动。
- **Scene / UI**：不自动联动；场景切换后由调用方调 `CameraManager`。

## 设计原则

- 只做「把 Cinemachine 接进 `ICameraManager`」这一件事；不做相机编排、不重写 Brain。
- 版本与依赖归本包；核心包保持中立。

## 依赖

- `com.unity.cinemachine` **3.1.7**（Unity 注册表包；由本包 `package.json` 声明）。
- 核心包 `com.venusy609.xframework`（需先安装；UPM 不支持 Git URL 依赖，由包级 README 的顺序兜底）。
