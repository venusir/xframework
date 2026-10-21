<#
.SYNOPSIS
    示例代码编译守卫：把 Samples~/ 下的 .cs 编一遍。

.DESCRIPTION
    Samples~/ 被 Unity 完全忽略（目录名末尾的 ~ 是 UPM 约定），因此示例代码在仓内**没有任何编译通道**。
    本脚本生成一个临时 csproj，把 Samples~/**/*.cs 编进来，引用 Unity 安装目录的托管程序集
    （从包工程 csproj 的 <Reference> 项读取 HintPath）与 Library/ScriptAssemblies 下的包程序集
    （Venusy609.Xframework、UniTask 等，剔除 .Editor 与测试程序集）。

    **前提与现有 dotnet 门禁相同**：先让 Unity 导入一次本工程，生成 csproj 与 Library/ScriptAssemblies。
    没有它们时脚本直接失败并提示，而不是给出假的「0 error」。

    退出码：0 = 编译通过（或示例目录为空）；非 0 = 有 error CS 或前置缺失。
    示例文件不随包编译，但仍属包内文本——它们只能使用公开 API（见包 README 的示例说明）。

.PARAMETER Project
    包工程 csproj，默认 Venusy609.Xframework.csproj（Unity 生成，不入库）。

.PARAMETER SamplesRoot
    示例根目录，默认 Assets/XFramework/Samples~。
#>
param(
    [string]$Project = "Venusy609.Xframework.csproj",
    [string]$SamplesRoot = "Assets/XFramework/Samples~"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot $Project
$samplesPath = Join-Path $repoRoot $SamplesRoot
$scriptAssemblies = Join-Path $repoRoot "Library/ScriptAssemblies"

if (-not (Test-Path $projectPath)) {
    throw "包工程不存在：$projectPath。先让 Unity 导入一次本工程，生成 csproj 后再跑。"
}

$frameworkDll = Join-Path $scriptAssemblies "Venusy609.Xframework.dll"
if (-not (Test-Path $frameworkDll)) {
    throw "未找到 $frameworkDll。先让 Unity 导入并编译一次本工程（Library/ScriptAssemblies 是包程序集的来源）。"
}

$files = @(Get-ChildItem -Path $samplesPath -Recurse -Filter *.cs -File | Sort-Object FullName)
if ($files.Count -eq 0) {
    Write-Host "示例编译守卫：Samples~ 下没有 .cs，跳过。" -ForegroundColor DarkGray
    exit 0
}

# UnityEngine 等托管程序集的路径从 Unity 生成的包工程里读，避免第二份 Unity 安装路径真相。
[xml]$packageProject = Get-Content -LiteralPath $projectPath
$referenceLines = New-Object System.Collections.Generic.List[string]
foreach ($reference in @($packageProject.Project.ItemGroup.Reference)) {
    if ($null -eq $reference) { continue }
    $hintPath = $reference.HintPath
    if ([string]::IsNullOrWhiteSpace($hintPath)) { continue }
    # 生成工程里可能残留已移动的编辑器程序集路径；磁盘上没有的引用不写进临时工程。
    if (-not (Test-Path -LiteralPath $hintPath)) { continue }
    $referenceLines.Add(('    <Reference Include="{0}"><HintPath>{1}</HintPath><Private>false</Private></Reference>' -f $reference.Include, $hintPath))
}

# 包程序集（Runtime 侧；Editor 与测试程序集不参与示例编译）。
foreach ($dll in @(Get-ChildItem -Path $scriptAssemblies -Filter *.dll -File | Sort-Object Name)) {
    if ($dll.Name -match '\.Editor(\.|$)') { continue }
    if ($dll.Name -match 'Tests') { continue }
    if ($dll.Name -eq 'Venusy609.Xframework.dll') { continue } # 下面单独加，保持顺序可读
    $referenceLines.Add(('    <Reference Include="{0}"><HintPath>{1}</HintPath><Private>false</Private></Reference>' -f $dll.BaseName, $dll.FullName))
}
$referenceLines.Insert(0, ('    <Reference Include="Venusy609.Xframework"><HintPath>{0}</HintPath><Private>false</Private></Reference>' -f $frameworkDll))

$compileLines = New-Object System.Collections.Generic.List[string]
foreach ($file in $files) {
    $escaped = $file.FullName.Replace('&', '&amp;').Replace('"', '&quot;')
    $compileLines.Add(('    <Compile Include="{0}" />' -f $escaped))
}

$lines = @(
    '<Project Sdk="Microsoft.NET.Sdk">',
    '  <PropertyGroup>',
    '    <TargetFramework>netstandard2.1</TargetFramework>',
    '    <LangVersion>9.0</LangVersion>',
    '    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>',
    '    <AssemblyName>XFramework.Samples.CompileCheck</AssemblyName>',
    '    <NoWarn>CS0649</NoWarn>',
    '    <MSBuildWarningsAsMessages>MSB3245</MSBuildWarningsAsMessages>',
    '  </PropertyGroup>',
    '  <ItemGroup>'
) + $compileLines + @(
    '  </ItemGroup>',
    '  <ItemGroup>'
) + $referenceLines + @(
    '  </ItemGroup>',
    '</Project>'
)

$temp = Join-Path ([System.IO.Path]::GetTempPath()) ("xfw-samples-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null

try {
    $csprojPath = Join-Path $temp "SamplesCompileCheck.csproj"
    Set-Content -LiteralPath $csprojPath -Value ($lines -join [Environment]::NewLine) -Encoding utf8

    $output = & dotnet build $csprojPath --artifacts-path (Join-Path $temp "artifacts") -v q --nologo 2>&1
    $output | ForEach-Object { Write-Host $_ }

    if ($LASTEXITCODE -ne 0) {
        Write-Host ("示例编译守卫：失败（{0} 个 .cs）" -f $files.Count) -ForegroundColor Red
        exit 1
    }

    Write-Host ("示例编译守卫：通过（{0} 个 .cs）" -f $files.Count) -ForegroundColor Green
    exit 0
}
finally {
    Remove-Item -Recurse -Force $temp -ErrorAction SilentlyContinue
}
