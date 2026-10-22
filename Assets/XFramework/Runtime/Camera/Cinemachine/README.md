# Cinemachine Camera —— `ICameraManager` 的 Cinemachine 3.x 实现

## 概述

`CinemachineCameraManager` 把 Cinemachine 的 vcam 优先级与 `CinemachineBrain` 混合接进核心包的
`ICameraManager`：游戏代码只调 `CameraManager`，不直接依赖 Cinemachine 类型。

**本后端是适配器，不是行为库**：跟随 / 瞄准 / 震屏 / 缩放 / 碰撞 / 约束 / Dolly / Target Group / Timeline
由 Cinemachine 提供并在 Cinemachine 里配置；本后端只负责注册、优先级 / Brain 混合与状态查询。

**统一句柄**：命名 vcam 与随预制创建 / 销毁的动态 vcam 都经 `Register` 返回 `CameraHandle`；命名 id 在
`CameraHandle.NamedId` 里（动态为 `null`）。**Timeline / 过场相机**由过场系统接管 Brain，不经过本管理器；
Brain 的活动相机未注册时，激活 / 过渡会告警「可能与过场冲突」。

**命名空间**：`XFramework.XCamera.Cinemachine`。**只支持 Cinemachine 3.x**（按 3.1.7 编写）；2.x 的类型名与
字段名不同，需要自己的 `ICameraManager` 实现。

## 快速使用

本后端随核心包发布，无需单独安装（核心 asmdef 已硬引用 `Unity.Cinemachine` 3.1.7）。接线：

```csharp
using Unity.Cinemachine;
using XFramework.XCamera;
using XFramework.XCamera.Cinemachine;

var manager = new CinemachineCameraManager(brain);
CameraManager.Initialize(manager);
CameraManager.Register("main", mainVcam);
CameraManager.Register("battle", battleVcam);

CameraManager.Activate("main");
CameraManager.TransitionTo("battle", 0.8f, CameraBlendStyle.EaseInOut);

// 动态 vcam（随预制创建 / 销毁）：注册拿句柄，不生成 string id
var handle = CameraManager.Register(dynamicVcam);
CameraManager.Activate(handle);
// 预制销毁时
CameraManager.Unregister(handle); // 或 handle.Dispose()
```

## 模块结构

```text
Runtime/Camera/Cinemachine/
├── CinemachineCameraManager.cs  # 公开：ICameraManager 的 Cinemachine 3.x 实现
└── README.md
```

## 行为契约

- 仅主线程；注册表与场景状态不是线程安全的。
- **注册统一**：`Register(string, vcam)` 与 `Register(vcam)` 都返回句柄；注册不激活、不改场景。同一 id 同一
  实例幂等；id 冲突 / 实例已注册为另一 id / 已销毁对象 / 类型不符抛 `ArgumentException`。
- **激活**：目标优先级提升到「场景内同通道最大优先级 + 1」（包含未注册 vcam），只改目标；切换时恢复上一个
  目标由管理器改过的优先级 / GameObject / 组件启用状态。目标 GameObject / 组件在需要时由管理器启用，并在
  注销 / Dispose 时恢复。
- **cut**：`Activate` 与 `duration <= 0.0001` / `Cut` 的 `TransitionTo` 通过 `GetBlendOverride` 返回
  `Styles.Cut`；若目标已在混合中，直接把 `brain.ActiveBlend = null` 强制完成当前混合（R3-CIN-001）。
- **过渡**：非 cut 请求写入 `PendingTransition`，`CinemachineCore.GetBlendOverride` 在 Brain 创建混合时返回
  请求的 `CinemachineBlendDefinition`；**不修改** `Brain.DefaultBlend`。项目 `CustomBlends` 先于
  `GetBlendOverride` 求值，因此本次管理器请求优先。
- `GetBlendOverride` 是全局单委托：构造时保存前值并安装自己，`Dispose` 时仅当当前委托仍是自己才恢复前值；
  一个进程建议只创建一个相机管理器。
- `Deactivate`：抑制该 vcam 优先级（`int.MinValue`）并让 Brain 重选；若管理器曾启用 GameObject 且原状态
  关闭，则恢复关闭。不发布状态事件；没有其它候选时 Brain 可能继续输出它。
- **`ActiveHandle`** 读 `brain.ActiveVirtualCamera` 并按槽位解析；过渡期间是 incoming；未注册 / 已销毁 /
  无活动为 `default`。`OutputCamera` = `brain.OutputCamera`，可能为 null、不保证等于目标。
- **`IsTransitioning`** 只表示本管理器发起的非 cut 过渡；`CameraUpdatedEvent` 在混合结束后清理 pending。
  Brain 自己的 `IsBlending`（含 Timeline / 外部混合）不进入该属性。
- **注销**：恢复作者优先级 / 启用状态，必要时强制完成在途混合，移除槽位；**不销毁 vcam**。若作者优先级仍最高，
  Brain 可能继续输出它，此时 `ActiveHandle` 为 `default`；下一次 `Activate` 会以「场景最大 + 1」压过它。
- **Dispose**：恢复仍存活槽位的作者状态、退订 `CameraUpdatedEvent`、恢复 `GetBlendOverride` 前值、清表。
- **Timeline / 过场**：过场 vcam 不注册；若 Brain 的活动相机未注册，`Activate` / `TransitionTo` 会告警
  「可能与过场冲突」；`ActiveHandle` / `ActiveCameraId` 返回 `default` / `null`，`OutputCamera` 仍可读。

## 状态事件

后端订阅 `CinemachineCore` 的四个静态 UnityEvent，按 `Origin` / mixer 过滤本 Brain 后经 `MessageManager`
发布：

| Cinemachine 事件 | 发布消息 | 条件 |
|---|---|---|
| `CameraActivatedEvent` | `CameraActivatedMessage(handle, previous, IsCut)` | incoming 能解析到已注册槽位 |
| `CameraActivatedEvent` + `IsCut` | 再发 `CameraBlendCompletedMessage(handle)` | 同上；cut 视为零时长完成 |
| `CameraDeactivatedEvent` | `CameraDeactivatedMessage(handle)` | 被停 live 的相机仍能解析到注册槽位 |
| `BlendFinishedEvent` | `CameraBlendCompletedMessage(handle)` | 完成的相机能解析到注册槽位 |
| `CameraUpdatedEvent` | 不发消息 | 清理 pending / 检测目标销毁兜底 |

- cut A→B：`Deactivated(A)` → `Activated(B, IsCut=true)` → `Completed(B)`。
- blend A→B：开始帧 `Activated(B)`；完成帧 `Deactivated(A)` → `Completed(B)`。
- 未注册 incoming 不发布 `Activated`，旧的已注册相机失去 live 时仍发布 `Deactivated`。
- 其他 Brain 的事件按 `Origin` / mixer 过滤。
- `Dispose` 与 `ICameraManagerSessionReset.UnbindSessionEvents` 都会 `RemoveListener`；关闭域重载时静态事件
  跨会话存活，不退订会让旧管理器被钉住。

## 架构设计

`CinemachineCameraManager` 是核心包 `ICameraManager` 的一个后端实现：它不驱动任何调度器，Brain 自己按
`UpdateMethod` 混合；管理器只负责注册表、优先级、过渡请求、状态解析与事件转发。

## API 参考

| 成员 | 行为 |
|---|---|
| `CinemachineCameraManager(CinemachineBrain)` | 构造；安装 `GetBlendOverride`、订阅 `CameraUpdatedEvent` |
| `Register<T>(string, T)` / `Register<T>(T)` | 命名 / 动态注册，返回 `CameraHandle`（不激活、不动场景） |
| `Unregister(CameraHandle)` | 恢复作者状态、移除槽位（不销毁 vcam） |
| `OutputCamera` / `ActiveHandle` / `IsTransitioning` / `CameraCount` | 查询 |
| `TryGetHandle(string, out CameraHandle)` | 按命名 id 查句柄 |
| `Activate` / `Deactivate` / `TransitionTo` / `IsActive`（仅句柄） | 契约操作 |
| `Dispose()` | 恢复场景状态、退订静态事件、清表 |

字符串操作在门面；后端只认句柄。

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

本后端本身就是核心包 `ICameraManager` 的一个实现。要接别的后端（2.x、Timeline、自研相机系统）：

- 实现 `ICameraManager`；命名 / 动态注册都返回 `CameraHandle`。
- 义务：仅主线程；未知 id / 无效句柄告警并 no-op，不抛；`null` id 抛 `ArgumentNullException`；
  `ActiveHandle`（`NamedId` 为命名 id）/ `IsTransitioning`；**`OutputCamera` 可能为 `null`、不保证等于
  过渡目标**；`Activate` 取消在途过渡。
- 义务与边界的完整说明在核心包 `Runtime/Camera/README.md` 的「自定义实现」与「行为契约」。

## 内部机制

- 激活由 vcam 的 `Priority`（`PrioritySettings`，隐式 int 赋值会置 `Enabled`）与 Brain 的优先级队列决定。
- 槽位表 + 线性扫描：活动 vcam → 句柄、vcam → 槽位、id → 槽位；不再维护反查字典，避免外部销毁 / 替换 /
  注销时的陈旧键分支。
- `ComputeBoostPriority` 取已注册槽位的作者优先级与同通道未注册 vcam 的最大值 + 1，`int.MaxValue` 封顶。
- 过渡由 `GetBlendOverride` 请求；`CameraUpdatedEvent` 在混合结束后清理 pending，`CameraActivatedEvent` /
  `CameraDeactivatedEvent` / `BlendFinishedEvent` 负责状态消息与等待者完成。
- `CinemachineBlendDefinition.Styles` 映射核心包的 `CameraBlendStyle`；非法枚举值抛
  `ArgumentOutOfRangeException`。

## 生命周期与清理

- 管理器不拥有 Brain / vcam；`Dispose` 恢复管理器改过的优先级 / 启用状态、退订静态事件、清空注册表。
- `CameraManager.Destroy` 会调用本实现的 `Dispose`（它实现了 `IDisposable`）。
- `CameraManager.Initialize` 重复注入时，被拒绝的第二个实例**不会被释放**：本后端在构造时订阅静态事件，
  调用方必须自行 `Dispose` 被拒绝的实例。

## 线程契约

**全部 API 仅在主线程调用。** Brain 与 vcam 都是 Unity 场景对象；管理器不做跨线程同步。

## 性能代价

- 激活 / 过渡 / 注册查询遍历槽位（vcam 数量通常个位数），O(n)，无分配。
- 每帧成本归 Brain（优先级队列 + 混合）；本管理器只在 `CameraUpdatedEvent` 里做一次引用比较。
- `GetBlendOverride` 每次混合创建时调用一次，只做引用比较。

## 已知限制

- **只支持 Cinemachine 3.x**（3.1.7 编写）；2.x 需要自己的实现。
- **事件只对已注册 vcam 发布**：未注册 incoming 不发 `CameraActivatedMessage`；订阅是全局 `MessageManager`
  通道，多个管理器 / 多个 Brain 时应按 `Handle` 过滤。事件反映 Brain 真实状态，不是命令回执。
- **动态 vcam 的 `ActiveHandle.NamedId` / `CameraManager.ActiveCameraId` 为 `null`**：用 `ActiveHandle` /
  `IsActive(handle)`；`TryGetHandle` 只查命名 id。
- **Timeline / 过场不接管**：过场 vcam 不注册；Brain 被未注册的过场 vcam 控制时，`Activate` /
  `TransitionTo` 会告警「可能与过场冲突」，`ActiveCameraId` / `ActiveHandle` 返回 `null` / `default`
  （`OutputCamera` 仍可读）。
- **`GetBlendOverride` 是全局单委托**：一个进程建议只创建一个相机管理器；项目若也使用它，以最后安装者为
  准；管理器 `Dispose` 只在当前委托仍是自己时恢复前值。
- **Timeline 混合不经过 `GetBlendOverride`**：管理器请求的 duration / style 对 Timeline 无效。
- **`Deactivate` 不保证停用**：它只抑制优先级；没有其它候选时 Brain 可能继续输出该 vcam。
- **注销活动 vcam 后可能仍在输出**：恢复作者优先级后若它仍是场景最高，Brain 会继续用它；`ActiveHandle`
  为 `default`，下一次 `Activate` 会压过它。
- **未注册 vcam 的优先级不受管理**：管理器会把目标提升到它之上，但不会改写它的值。
- **`Activate` 的强制 cut** 通过 `GetBlendOverride` + `ActiveBlend = null` 完成；若项目额外安装了自己的
  override，以最后安装者为准。

## 设计取舍

- **优先用 Brain 自己的能力**：管理器只切优先级与请求混合，不自己插值、不另建调度器。
- **用 `GetBlendOverride` 而不是粘滞改 `DefaultBlend`**：请求只作用于本次混合，不污染后续过渡；代价是全局单
  委托。
- **不替 Cinemachine 做行为**：跟随 / 震屏 / 缩放在 Cinemachine 里配置，本后端只做优先级与混合的适配；
  行为本身仍归 Cinemachine。
- **动态 vcam 用 `CameraHandle` 而不是 string id**：随预制实例创建 / 销毁的 vcam 没有稳定 authoring id；
  句柄带代际，旧句柄自然失效。
- **过场不接管**：Timeline 已经接管 Brain；管理器再插一手只会冲突。未注册活动相机告警是提醒，不是阻断。
- **为什么并入核心包**：Input System 先例（核心 asmdef 早已无条件引用它）、核心最低 Unity 6000.3 +
  Cinemachine 3 默认、自研相机后端不是真实受众；单包单程序集消除独立扩展包的安装顺序、版本错位与门禁缺口。
  门面与契约仍保持后端中立，Cinemachine 类型只出现在本目录。

## 接口承诺到哪为止

- 核心包承诺的 `ActiveHandle`（`NamedId`）/ `IsTransitioning` / `OutputCamera` 语义在这里成立；过渡曲线、
  回退与中断的细节由 Cinemachine Brain 决定。
- `TransitionTo` 请求的 style / duration 通过 `GetBlendOverride` 优先于 `CustomBlends`；Timeline 混合不受
  控制。
- 不承诺「过渡到已是活动相机会重启混合」。

## 与相邻模块的关系

- **本模块根层的门面与契约**：本后端实现 `ICameraManager`；门面、查询与取消语义在模块根层。
- **Update**：本包不注册 ticker；过渡由 Brain 的 `UpdateMethod` 驱动。
- **Scene / UI**：不自动联动；场景切换后由调用方调 `CameraManager`。

## 设计原则

- 只做「把 Cinemachine 接进 `ICameraManager`」这一件事；不做相机编排、不重写 Brain。
- 作为核心包 `Runtime/Camera/Cinemachine/` 的内置后端；门面与契约保持后端中立。

## 依赖

- `com.unity.cinemachine` **3.1.7**：核心 asmdef 的硬依赖（与 Input System 同型）；版本真值在安装器清单 /
  包 README / 开发 manifest 三处，由 `DependencyDeclarationTests` 守卫。
