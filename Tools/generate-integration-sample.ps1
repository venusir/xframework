<#
.SYNOPSIS
    生成 Integration 示例的预制体、启动场景与输入配置资产。

.DESCRIPTION
    `Samples~/` 对 Unity 的 AssetDatabase 不可见，预制体/场景不能在示例目录里直接生成。
    本脚本把示例脚本与数据复制到 `Assets/__XfwIntegrationGen__/`（保持 .meta GUID），
    用 Unity batchmode 调 `IntegrationAssetGenerator.Generate` 在临时目录里生成，
    再把产物（含 Unity 写出的 .meta）移回 `Samples~/Integration/`，最后删除临时目录。

    前置：本仓库已经被 Unity 导入过一次（存在 Library/ScriptAssemblies）；建议先跑一次
    `Tools/run-tests.ps1 -Setup` 创建测试壳，本脚本会优先用测试壳，避免与编辑器争 Library 锁。

.PARAMETER UnityPath
    显式指定 Unity.exe。留空则按 ProjectSettings/ProjectVersion.txt 的版本自动探测。

.PARAMETER UseRepo
    强制在仓库本体运行（需先关闭编辑器）。

.EXAMPLE
    pwsh -File Tools/generate-integration-sample.ps1
#>
param(
    [string]$UnityPath = "",
    [switch]$UseRepo
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$samples = Join-Path $repoRoot "Assets/XFramework/Samples~/Integration"
$temp = Join-Path $repoRoot "Assets/__XfwIntegrationGen__"
$generatorSource = Join-Path $repoRoot "Tools/SampleGeneration/IntegrationAssetGenerator.cs"
$logPath = Join-Path $repoRoot "Logs/integration-gen.log"

if (-not (Test-Path $samples)) {
    throw "示例目录不存在：$samples"
}

if (-not (Test-Path $generatorSource)) {
    throw "生成器源码不存在：$generatorSource"
}

# 生成物清单（验证与移动共用；顺序无关）
$prefabs = @(
    "Integration.MainMenu",
    "Integration.Settings",
    "Integration.Language",
    "Integration.Confirm",
    "Integration.ModeSelect",
    "Integration.GamePanel",
    "Integration.Result",
    "Integration.Pause",
    "Integration.Loading"
)
$scenes = @("Integration.Boot.unity", "Integration.Game.unity")

# Unity 版本与路径
$versionFile = Join-Path $repoRoot "ProjectSettings/ProjectVersion.txt"
if (-not (Test-Path $versionFile)) {
    throw "找不到 ProjectSettings/ProjectVersion.txt。"
}

$versionLine = Get-Content $versionFile | Where-Object { $_ -match "^m_EditorVersion:" } | Select-Object -First 1
$version = ($versionLine -split ":", 2)[1].Trim()

if (-not $UnityPath) {
    $candidates = @(
        "$env:ProgramFiles\Unity\Hub\Editor\$version\Editor\Unity.exe",
        "$env:LOCALAPPDATA\Unity\Hub\Editor\$version\Editor\Unity.exe",
        "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe",
        "D:\Program Files\Unity\$version\Editor\Unity.exe",
        "E:\Program Files\Unity\$version\Editor\Unity.exe"
    )
    $UnityPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $UnityPath) {
        throw "未找到 Unity $version 的编辑器。请用 -UnityPath 显式指定。"
    }
}

# 优先用测试壳，避免与正在打开的编辑器争 Library 锁
$shellPath = Join-Path (Split-Path -Parent $repoRoot) ((Split-Path -Leaf $repoRoot) + ".TestRun")
$projectPath = $repoRoot
if (-not $UseRepo -and (Test-Path (Join-Path $shellPath "ProjectSettings/ProjectVersion.txt"))) {
    $projectPath = $shellPath
}

Write-Host "Unity:   $UnityPath"
Write-Host "Project: $projectPath"

# 1. 清理并搭建临时工作区
if (Test-Path $temp) {
    Remove-Item -Recurse -Force $temp
}
if (Test-Path "$temp.meta") {
    Remove-Item -Force "$temp.meta"
}

New-Item -ItemType Directory -Force (Join-Path $temp "Editor") | Out-Null
New-Item -ItemType Directory -Force (Join-Path $temp "Data") | Out-Null
New-Item -ItemType Directory -Force (Join-Path $temp "Generated/Prefabs") | Out-Null
New-Item -ItemType Directory -Force (Join-Path $temp "Generated/Scenes") | Out-Null

# 预置已有 .meta：生成器覆盖资源内容，但 GUID 保持不变（否则每次重生成都会换 GUID，
# 引用这些资源的场景/预制体会跟着漂移）。
foreach ($name in $prefabs) {
    $meta = Join-Path $samples "Prefabs/$name.prefab.meta"
    if (Test-Path $meta) {
        Copy-Item $meta (Join-Path $temp "Generated/Prefabs/$name.prefab.meta") -Force
    }
}
foreach ($name in $scenes) {
    $meta = Join-Path $samples "Scenes/$name.meta"
    if (Test-Path $meta) {
        Copy-Item $meta (Join-Path $temp "Generated/Scenes/$name.meta") -Force
    }
}
$inputMeta = Join-Path $samples "Data/Integration.InputOptions.asset.meta"
if (Test-Path $inputMeta) {
    Copy-Item $inputMeta (Join-Path $temp "Generated/Integration.InputOptions.asset.meta") -Force
}

Copy-Item -Path (Join-Path $samples "Scripts") -Destination $temp -Recurse -Force
Copy-Item -Path $generatorSource -Destination (Join-Path $temp "Editor/IntegrationAssetGenerator.cs") -Force

$dataFiles = @(
    "Integration.Lang_zh_Hans.json",
    "Integration.Lang_zh_Hans.json.meta",
    "Integration.Actions.inputactions",
    "Integration.Actions.inputactions.meta"
)
foreach ($file in $dataFiles) {
    Copy-Item -Path (Join-Path $samples "Data/$file") -Destination (Join-Path $temp "Data/$file") -Force
}

# 2. 跑生成器
New-Item -ItemType Directory -Force (Split-Path -Parent $logPath) | Out-Null
if (Test-Path $logPath) {
    Remove-Item -Force $logPath
}

$unityArgs = @(
    "-batchmode",
    "-nographics",
    "-projectPath", $projectPath,
    "-executeMethod", "XFramework.Samples.Integration.Editor.IntegrationAssetGenerator.Generate",
    "-quit",
    "-logFile", $logPath
)

Write-Host "运行生成器..."
# Unity.exe 会派生工作进程；& 立即返回、拿不到真实退出码。用 Start-Process -Wait（连子进程一起等），
# 与 Tools/run-tests.ps1 的做法一致。
$proc = Start-Process -FilePath $UnityPath -ArgumentList $unityArgs -NoNewWindow -PassThru -Wait
if ($proc.ExitCode -ne 0) {
    throw "Unity 生成失败（退出码 $($proc.ExitCode)）。日志：$logPath"
}

# 3. 校验产物
foreach ($name in $prefabs) {
    $path = Join-Path $temp "Generated/Prefabs/$name.prefab"
    if (-not (Test-Path $path)) {
        throw "生成器没有产出 $path。日志：$logPath"
    }
}

foreach ($name in $scenes) {
    $path = Join-Path $temp "Generated/Scenes/$name"
    if (-not (Test-Path $path)) {
        throw "生成器没有产出场景 $path。日志：$logPath"
    }
}

$inputOptions = Join-Path $temp "Generated/Integration.InputOptions.asset"
if (-not (Test-Path $inputOptions)) {
    throw "生成器没有产出输入配置资产。日志：$logPath"
}

# 4. 移回示例目录
$prefabTarget = Join-Path $samples "Prefabs"
$sceneTarget = Join-Path $samples "Scenes"
New-Item -ItemType Directory -Force $prefabTarget | Out-Null
New-Item -ItemType Directory -Force $sceneTarget | Out-Null

foreach ($name in $prefabs) {
    foreach ($suffix in @("$name.prefab", "$name.prefab.meta")) {
        Move-Item -Path (Join-Path $temp "Generated/Prefabs/$suffix") -Destination (Join-Path $prefabTarget $suffix) -Force
    }
}

foreach ($name in $scenes) {
    foreach ($suffix in @($name, "$name.meta")) {
        Move-Item -Path (Join-Path $temp "Generated/Scenes/$suffix") -Destination (Join-Path $sceneTarget $suffix) -Force
    }
}

foreach ($suffix in @("Integration.InputOptions.asset", "Integration.InputOptions.asset.meta")) {
    Move-Item -Path (Join-Path $temp "Generated/$suffix") -Destination (Join-Path $samples "Data/$suffix") -Force
}

# 5. 清理临时目录（含 Unity 为临时根目录写出的 .meta）
Remove-Item -Recurse -Force $temp
if (Test-Path "$temp.meta") {
    Remove-Item -Force "$temp.meta"
}

Write-Host "生成完成："
Write-Host "  $prefabTarget"
Write-Host "  $sceneTarget"
Write-Host "  $(Join-Path $samples 'Data/Integration.InputOptions.asset')"

Push-Location $repoRoot
try {
    Write-Host "`n git status --short:"
    git status --short
}
finally {
    Pop-Location
}
