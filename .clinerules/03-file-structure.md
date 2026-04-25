## 文件结构与资产组织 (File Structure & Asset Organization)

项目的文件夹组织遵循 Unity 最佳实践:

- **Scripts/**: 存放所有 C# 脚本，按功能模块划分子文件夹，如 `Scripts/Player`, `Scripts/UI`, `Scripts/Managers`。
- **Prefabs/**: 存放所有预制体，并按照功能分类。
- **Scenes/**: 存放游戏场景文件，如 `Scenes/MainMenu.unity`, `Scenes/Gameplay.unity`。
- **Art/**: 存放美术资产，包括 `Animations`, `Materials`, `Models`, `Textures` 等。
- **Audio/**: 存放音频文件。
- **Resources/**: 存放需要通过 `Resources.Load` 动态加载的资源。
- **Editor/**: 存放编辑器扩展脚本。

### 资产命名规范:
- 所有资产遵循 PascalCase 并使用描述性名称。
- 纹理: `TX_Name_UsageType.ext`，例如 `TX_Player_Diffuse.png`。
- 材质: `M_Name.ext`，例如 `M_PlayerMat.mat`。
- 模型: `MOD_Name.ext`，例如 `MOD_Player.fbx`。
- 预制体: `PF_Name.ext`，例如 `PF_Player.prefab`。
- 脚本: `CS_Name.cs`，例如 `CS_PlayerController.cs`。 //可选
- 动画控制器: `AC_Name.controller`，例如 `AC_Player.controller`。
- 动画片段: `AN_Name.anim`，例如 `AN_Walk.anim`。