<#
.SYNOPSIS
    生成 Example / Cinemachine 示例的启动场景。

.DESCRIPTION
    `Samples~/` 对 Unity 的 AssetDatabase 不可见，场景不能在示例目录里直接生成。
    本脚本把示例脚本（含 .meta，保持 GUID）复制到 `Assets/__Xfw<Sample>Gen__/`，
    用 Unity batchmode 调 `<Sample>SceneGenerator.Generate` 在临时目录里生成，
    再把场景（含 Unity 写出的 .meta）移回 `Samples~/<Sample>/`，最后删除临时目录。

    前置：测试壳已创建（`Tools/run-tests.ps1 -Setup`）；先清理 Samples 菜单的导入副本
    （`Assets/Samples` 存在会让示例类型在 Assembly-CSharp 里重复）。

.PARAMETER Sample
    示例名：Example 或 Cinemachine。

.PARAMETER UnityPath
    显式指定 Unity.exe。留空则按 ProjectSettings/ProjectVersion.txt 的版本自动探测。

.PARAMETER UseRepo
    强制在仓库本体运行（需先关闭编辑器）。

.EXAMPLE
    pwsh -File Tools/generate-sample-scene.ps1 -Sample Example
    pwsh -File Tools/generate-sample-scene.ps1 -Sample Cinemachine
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Example", "Cinemachine")]
    [string]$Sample,
    [string]$UnityPath = "",
    [switch]$UseRepo
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$samples = Join-Path $repoRoot "Assets/XFramework/Samples~/$Sample"
$temp = Join-Path $repoRoot "Assets/__Xfw${Sample}Gen__"
$generated = Join-Path $temp "Generated"
$generatorName = "${Sample}SceneGenerator.cs"
$generatorSource = Join-Path $repoRoot "Tools/SampleGeneration/$generatorName"
$sceneName = "$Sample.unity"
$logPath = Join-Path $repoRoot "Logs/sample-scene-gen-$Sample.log"

if (-not (Test-Path $samples)) {
    throw "示例目录不存在：$samples"
}
if (-not (Test-Path $generatorSource)) {
    throw "生成器源码不存在：$generatorSource"
}
if (Test-Path (Join-Path $repoRoot "Assets/Samples")) {
    throw "存在开发导入副本 Assets/Samples。请先运行菜单 Tools/XFramework/Samples/Clean Imported Samples，避免示例类型重复。"
}

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

Write-Host "Sample:  $Sample"
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
New-Item -ItemType Directory -Force $generated | Out-Null

# 预置场景 .meta：重新生成时保持 GUID 不变（否则引用它的资产会漂移）。
$sceneMeta = Join-Path $samples "$sceneName.meta"
if (Test-Path $sceneMeta) {
    Copy-Item $sceneMeta (Join-Path $generated "$sceneName.meta") -Force
}

# 复制示例脚本与 .meta（保持 GUID，场景里的 m_Script 才不会指向新 GUID）
Get-ChildItem -Path $samples -Filter *.cs -File | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $temp $_.Name) -Force
    if (Test-Path ($_.FullName + ".meta")) {
        Copy-Item ($_.FullName + ".meta") (Join-Path $temp ($_.Name + ".meta")) -Force
    }
}

Copy-Item -Path $generatorSource -Destination (Join-Path $temp "Editor/$generatorName") -Force

# 2. 跑生成器
New-Item -ItemType Directory -Force (Split-Path -Parent $logPath) | Out-Null
if (Test-Path $logPath) {
    Remove-Item -Force $logPath
}

$unityArgs = @(
    "-batchmode",
    "-nographics",
    "-projectPath", $projectPath,
    "-executeMethod", "XFramework.Samples.Generation.${Sample}SceneGenerator.Generate",
    "-quit",
    "-logFile", $logPath
)

Write-Host "运行生成器..."
$proc = Start-Process -FilePath $UnityPath -ArgumentList $unityArgs -NoNewWindow -PassThru -Wait
if ($proc.ExitCode -ne 0) {
    throw "Unity 生成失败（退出码 $($proc.ExitCode)）。日志：$logPath"
}

# 3. 校验产物
$generatedScene = Join-Path $generated $sceneName
$generatedMeta = Join-Path $generated "$sceneName.meta"
if (-not (Test-Path $generatedScene)) {
    throw "生成器没有产出场景：$generatedScene。日志：$logPath"
}
if (-not (Test-Path $generatedMeta)) {
    throw "生成器没有产出场景 meta：$generatedMeta。日志：$logPath"
}

# 4. 移回示例目录
Move-Item -Path $generatedScene -Destination (Join-Path $samples $sceneName) -Force
Move-Item -Path $generatedMeta -Destination (Join-Path $samples "$sceneName.meta") -Force

# 5. 清理临时目录
Remove-Item -Recurse -Force $temp
if (Test-Path "$temp.meta") {
    Remove-Item -Force "$temp.meta"
}

Write-Host "生成完成：$(Join-Path $samples $sceneName)"

Push-Location $repoRoot
try {
    Write-Host "`n git status --short:"
    git status --short
}
finally {
    Pop-Location
}
