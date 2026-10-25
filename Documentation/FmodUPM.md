# FMOD for Unity → UPM 包与私有 GitHub 仓库（配方）

> **收件人**：维护者（在第一个工程里把 FMOD for Unity 打成 UPM 包并上传 GitHub 备用时）。
> **不是**随包发布的使用文档。**本配方尚未在本仓执行**；第一个工程执行后，把实测结果回填到本文件。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。

## 〇、核对状态（先读这一节）

| 标记 | 含义 |
| --- | --- |
| `[官方文档]` | FMOD 官方用户指南 / 官方问答的表述 |
| `[二手资料]` | 社区仓库 / 问答 / 教程，不是 FMOD 官方文档 |
| `[本仓核对]` | 读本仓文件得出 |
| `[未实测]` | 只有文档或推理支撑，**没有在第一个工程里跑过** |

**核对基准**：FMOD for Unity 2.03 官方用户指南；手动 UPM 化的事实来自 FMOD Q&A（2.02.20+）。
**本配方未在第一个工程执行。** 每条外部事实按上面的标记读；执行后请把 `[未实测]` 改成实测结论。

---

## 一、结论与适用场景

- 目标：把第一个工程里已安装的 **FMOD for Unity** 集成目录，变成一个可被 UPM 消费的包，
  包名固定 **`com.firelight.fmod-for-unity`**，上传到**私有** GitHub 仓库并按 tag 锁定版本。
- 为什么必须私有：FMOD 集成源码与原生库有许可证约束，**不能公开再分发**；仓库只邀请持有
  FMOD 许可证的项目/成员。`[官方文档/许可]`
- 谁消费：第一个工程自己、后续游戏工程、以及本仓的可选 FMOD 扩展包
  （`Assets/XFramework.Fmod/`，见 `Documentation/FMOD.md`；**该扩展尚未实现**）。
- 本配方只做「FMOD 集成 → UPM 包 → 私有 Git 仓库」；**不**写框架扩展代码，也**不**在本仓提交
  FMOD SDK。

---

## 二、前置条件

- [ ] FMOD for Unity **2.02.20+**（手动 UPM 化的最低版本；2.03 为当前主线）。`[二手资料]`
- [ ] 第一个工程已用官方方式安装 FMOD（Asset Store / `.unitypackage` / Setup Wizard），
      并能播放一个真实事件。`[官方文档]`
- [ ] 有 FMOD Studio 工程与已构建的 bank；知道至少一个事件路径（如 `event:/Music/BGM`）。
- [ ] 知道第一个工程的 Unity 版本（本仓是 6000.4.5f1；FMOD 2.03 官方支持 2021.3 / 2022.3 / 6.0 / 6.3）。`[官方文档]`
- [ ] 有 GitHub 账号与创建**私有**仓库的权限；本机 Git 可用；决定用 HTTPS(PAT) 还是 SSH。
- [ ] 已阅读 FMOD 的许可条款，确认仓库访问范围符合许可证。

---

## 三、在第一个工程里打成 UPM 包

### 3.1 建包目录

在第一个工程的 `Packages/` 下建：

```text
Packages/com.firelight.fmod-for-unity/
├── package.json
├── FMOD/                  ← 从 Assets/Plugins/FMOD 整体搬来
│   ├── FMODUnity.asmdef   ← 名字必须是 FMODUnity（扩展 asmdef 按这个名字引用）
│   └── ...                ← 原生插件、C# 源码、Resources 等
└── FMOD.meta              ← 必须与 FMOD 文件夹一起搬，保留原 GUID
```

**不要**只搬 `FMOD` 文件夹而丢掉 `FMOD.meta`：FMOD 靠这个 GUID 定位基目录
（`BaseFolderGUID`），丢了 GUID 会找不到插件/原生库。`[二手资料]`

### 3.2 `package.json`

最小内容（版本按实际 FMOD 版本填，例如 `2.03.00`）：

```json
{
  "name": "com.firelight.fmod-for-unity",
  "version": "2.03.00",
  "displayName": "FMOD for Unity",
  "unity": "6000.3",
  "description": "FMOD for Unity integration packaged as a UPM package. Requires a valid FMOD license.",
  "license": "See FMOD license",
  "dependencies": {}
}
```

- **包名必须是 `com.firelight.fmod-for-unity`**：本仓 `Assets/XFramework.Fmod/` 的
  `versionDefines` 按这个名字匹配；改名就要同步改扩展 asmdef 与 README。
- 先查 `FMOD/FMODUnity.asmdef` 的 `references`：
  - 若引用 `Unity.Timeline`，在 `dependencies` 加 `"com.unity.timeline": "<版本>"`；
  - 若引用其它 UPM 包，同样补进 `dependencies`；
  - 否则保持 `{}`。
- `unity` 至少填第一个工程的最低 Unity 版本；本仓扩展要求 6000.3。`[本仓核对]`

### 3.3 搬目录与保 GUID

1. 把 `Assets/Plugins/FMOD/` 下的 `FMOD` 文件夹与 `FMOD.meta` **一起**移动到
   `Packages/com.firelight.fmod-for-unity/`。
2. 保留 `FMOD` 及其内部所有 `.meta` 文件；不要让 Unity 重新生成 GUID。
3. 确认 `Packages/com.firelight.fmod-for-unity/FMOD/FMODUnity.asmdef` 还在，且 `name` 是
   `FMODUnity`。`[二手资料]`
4. 如果 FMOD 的 Setup Wizard 仍然指向 `Assets/Plugins/FMOD`，先跑一次 Setup Wizard 让它指向
   新位置；再打开 Package Manager 确认包被识别。`[未实测]`

### 3.4 项目级设置的处理（重要，易踩）

FMOD 的 `FMODStudioSettings`（Studio Project Path / Build Path 等）通常是项目级配置：

- 先按 Q&A 配方**整体搬**，验证第一个工程还能播放事件；
- 如果多个工程消费同一个包、而 Studio 工程路径不同，考虑把 `FMODStudioSettings.asset` 留在
  **各项目的 `Assets/Resources/`**（或按 FMOD 文档的覆盖方式），只让包携带运行时代码与原生库。
  两种做法都要在第一个工程实测；本配方不假设哪一种一定成立。`[未实测]`
- `FMOD/Cache/`、`FMOD/Build/` 是生成物；不要提交进 Git（见 §4.1）。

### 3.5 冒烟验证

- [ ] Package Manager 里出现 `FMOD for Unity`（或包名），无编译错误。
- [ ] 控制台没有 `DllNotFoundException` / `FMODUnity` 缺失。
- [ ] 播放一个真实事件；音量/暂停/停止正常。
- [ ] 至少一个目标平台（编辑器 + 一个真机/Standalone）能加载原生插件。
- [ ] 记录 FMOD 版本、Unity 版本、平台、事件路径到本文件 §九。

---

## 四、上传到私有 GitHub

### 4.1 `.gitignore` / `.gitattributes`

在包目录（或仓库根）放：

```gitignore
# Unity / IDE 生成物
Library/
Temp/
Logs/
obj/
*.csproj
*.sln
.vs/

# FMOD 生成物（按实际目录名调整）
FMOD/Cache/
FMOD/Build/
```

```gitattributes
* text=auto eol=lf
*.meta text eol=lf
*.dll binary
*.so binary
*.bundle binary
*.a binary
*.dylib binary
*.bank binary
```

- 原生库/大文件可能超过 GitHub 单文件 100MB 限制；先量大小，必要时用 Git LFS。
- **Git LFS 与 UPM Git URL 的兼容性未实测**：Unity Package Manager 用系统 Git 拉取；若消费方
  没装 Git LFS 或没拉 LFS 对象，包会只有指针文件。若遇到，改用「本地包 + tarball」或私有
  registry，不要假设 LFS 一定可用。`[未实测]`

### 4.2 建私有仓库

推荐**一个仓库只放这个包，包在仓库根**（Git URL 最简单）。`[未实测]`

```bash
# 在 Packages/com.firelight.fmod-for-unity/ 里
git init
git add .
git commit -m "FMOD for Unity 2.03.00 UPM package"
git branch -M main
# 先在 GitHub 建私有仓库（网页或 gh repo create）
git remote add origin git@github.com:<account>/<repo>.git   # 或 HTTPS
git push -u origin main
git tag 2.03.00
git push origin 2.03.00
```

- 仓库必须 **Private**；FMOD 集成不可公开再分发。`[官方文档/许可]`
- `package.json` 的 `version` 与 tag 保持一致（示例 `2.03.00`）；消费方用 `#2.03.00` 锁定。
- `gh repo create` 的可用性取决于本机是否装了 GitHub CLI；没有就用网页建。

### 4.3 消费方的 `manifest.json`

在消费工程（第一个工程、后续游戏工程、或本仓本地测试）里：

```json
{
  "dependencies": {
    "com.firelight.fmod-for-unity": "https://github.com/<account>/<repo>.git#2.03.00"
  }
}
```

- **私有仓库需要凭据**：Unity 的 Package Manager 使用系统 Git；HTTPS 需要 PAT / Git Credential
  Manager，SSH 需要已配置的 key。不要把 PAT 写进公开仓库的 URL。`[未实测]`
- 若包在仓库子目录，用 `?path=<子目录>`，例如
  `https://github.com/<account>/<repo>.git?path=packages/fmod#2.03.00`（`?path=` 在 `#` 之前）。
- 更新 FMOD：替换 `FMOD` 目录、保留 `FMOD.meta` GUID、bump `package.json` 的 `version`、
  打新 tag、消费方改 `#新tag`；不要移动已有 tag。

### 4.4 在本仓/框架工程里消费

- **同一工程不要同时存在 `Assets/Plugins/FMOD` 与 `Packages/com.firelight.fmod-for-unity/FMOD`**：
  两条 `FMODUnity` 程序集会重名/重复编译。切到包之前先删掉 `Assets` 那份（或反之）；也不要让
  FMOD Setup Wizard 再往 `Assets/Plugins/FMOD` 重建一份。
- 本仓**不要提交** FMOD SDK、私有 Git URL、`Packages/manifest.json` / `packages-lock.json`
  的本地改动。
- 推荐：把包复制到 `Packages/com.firelight.fmod-for-unity/` 并加入 `.gitignore`；或在本地
  manifest 用 `file:` 依赖（同样不提交）。
- 扩展包 `Assets/XFramework.Fmod/`（设计见 `Documentation/FMOD.md`）的 `versionDefines` 按
  `com.firelight.fmod-for-unity` 匹配；包名不一致时需手动定义 `XFRAMEWORK_FMOD`。
- **`packages-lock.json` 风险**：Unity 可能把嵌入包写进 lock；干净克隆如果没有该包、却从
  lock 里看到它，会解析失败。加包后务必检查 `git status`，不要提交 manifest/lock 的本地改动；
  并用干净克隆跑一次验证。`[未实测]`

---

## 五、升级流程

1. 在第一个工程用官方方式安装新 FMOD 版本，跑 Setup Wizard，确认能播放。
2. 用新的 `FMOD` 文件夹替换包里的 `FMOD`，**保留 `FMOD.meta` 的 GUID**；不要删除/重建。
3. 修改 `package.json` 的 `version`（如 `2.03.06`），检查 `FMODUnity.asmdef` 的引用/依赖变化。
4. 跑 §3.5 冒烟；提交、打新 tag、推送。
5. 消费方把 `#tag` 改成新 tag；本仓扩展的 `versionDefines` 表达式应仍匹配（用 `0.0.0` 或范围，
   按实际验证）。
6. 在 `Documentation/FMOD.md` 的「已核对事实与漂移点」更新版本/API。

---

## 六、常见问题

| 现象 | 先查 |
| --- | --- |
| Package Manager 不显示包 | `package.json` 在包根；`name`/`version` 合法；仓库是否私有、凭据是否可用 |
| 编译报 `FMODUnity` 找不到 | `FMODUnity.asmdef` 是否在包内；名字是否被改；消费工程 asmdef 是否引用 |
| `DllNotFoundException` / 无声音 | 原生插件是否随包；平台是否支持；`FMOD.meta` GUID 是否保留；Setup Wizard |
| 多个工程设置互相污染 | `FMODStudioSettings` 是否应留在各项目 `Assets/Resources`（§3.4，需实测） |
| Unity 拉包失败/空文件 | Git LFS 是否被拉取；私有凭据；tag 是否存在；`?path=` 位置 |
| 干净克隆解析失败 | 是否误提交了 `packages-lock.json` / manifest 的本地 FMOD 条目 |

---

## 七、检查清单（执行时逐项勾）

- [ ] FMOD 版本 ≥ 2.02.20，记录实际版本
- [ ] `Packages/com.firelight.fmod-for-unity/package.json` 包名正确
- [ ] `FMOD` 文件夹 + `FMOD.meta` 一起搬，GUID 保留
- [ ] `FMODUnity.asmdef` 在包内且名字未改
- [ ] FMOD 集成自带的许可/README 随包保留；仓库为 Private
- [ ] 工程内没有同时存在 Assets 版与 Packages 版的两套 `FMODUnity`
- [ ] Package Manager 识别、编译通过、真实事件播放
- [ ] `.gitignore` / `.gitattributes` 就位；大文件策略确定
- [ ] 私有 GitHub 仓库创建、push、tag
- [ ] 消费方 `manifest.json` 用 `#tag` 成功解析
- [ ] 干净克隆（无 FMOD 包）不因 manifest/lock 残留失败
- [ ] 升级流程演练一次（替换目录、保 GUID、bump/tag）

---

## 八、已核对事实与漂移点

| 事实 | 状态 | 漂移点 |
| --- | --- | --- |
| 官方安装 = Asset Store / `.unitypackage` + Setup Wizard | `[官方文档]` | 可能新增官方 UPM 源 |
| 2.02.20+ 可手动 UPM 化；保留 `FMOD.meta` GUID | `[二手资料]` | 上游后续版本可能改变目录定位 |
| 集成 asmdef 名 `FMODUnity` | `[二手资料]` | 以实际安装版本为准 |
| FMOD 2.03 官方支持 Unity 2021.3 / 2022.3 / 6.0 / 6.3 | `[官方文档]` | 版本矩阵随发布变化 |
| 集成源码/原生库不可公开再分发 | `[官方文档/许可]` | 每次升级复核许可 |
| Git LFS 与 UPM Git URL 的兼容性 | `[未实测]` | 消费方环境差异大 |

---

## 九、第一个工程的实测记录（执行后回填）

- FMOD 版本：
- Unity 版本：
- 平台：
- 包路径 / 包名：
- `FMODStudioSettings` 的处理方式：
- Package Manager 是否直接识别：
- 冒烟事件：
- GitHub 仓库（私有）：
- tag：
- 消费方 manifest 是否成功：
- 干净克隆验证：
- 遇到的问题与解决：
