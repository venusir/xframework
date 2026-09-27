<#
.SYNOPSIS
    统计 XFramework 包内 XML 文档注释的编译告警（CS1570 格式错误 / CS1574 cref 无法解析）。

.DESCRIPTION
    Unity 生成的 csproj 没有设置 DocumentationFile，因此**默认编译根本不检查文档注释**：
    `<see cref="..."/>` 解析不了也不报错。本脚本显式开启文档生成、强制全量重编一次，
    把这条从未走过的检查通道照亮。

    为什么要看这两类告警：
    - **CS1570（XML 格式错误）**：一块注释的 XML 解析失败后，**编译器丢弃整条注释**——该成员的
      文档条目会从生成的 XML 中整条消失（实测：`ConfigManager` 的 33 条 CS1570 使其类型条目
      完全不存在，而同模块另 22 个类型条目正常）。典型成因是泛型尖括号未转义：`<c>Get<T>()</c>`
      里的 `<T>` 被当成 XML 标签。
    - **CS1574（cref 无法解析）**：链接指向不存在的东西，IDE 里不能跳转。实测下来绝大多数属下面四类，
      按此顺序排查：
      ① 「带类型限定的 cref **只找该类型自己声明的成员，不找继承来的**」——这是最容易踩的一条：
         `YooAsset.AssetHandle.Release` 匹配不上（Release 声明在基类 `HandleBase`），
         `RectTransform.position` 属 `Transform.position`，`IUpdateable.OnDisable` 属 `IUpdateLifecycle`，
         `UIPanelBase.OnUpdate` 属 `UIViewBase`，`IPhaseStage.ExecuteAsync` 属 `IPipelineStage`。
         改法：把限定名换成**声明它的那个类型**。
      ② 参数列表不完整：`CloseAsync(UIPanelBase, bool)` 匹配不上三参的真实签名——
         带默认值的参数也必须写全。（同理 `Register(IUpdateable, int, UpdateTier)` 少了第四个参数。）
      ③ cref 里不能带 `()`：`InitializeAsync()` 非法，写 `InitializeAsync`。
      ④ 本文件缺 using，导致 cref 的**参数类型**解析不了（如 `CancellationToken`、`IUIController`）——
         把该类型写成全名即可，不必为此给文件加 using。
      另有一类是文档指向**根本不存在的东西**（已改名或已删除的成员/类型），例如 `UIHudManager` 这个类
      在仓内并不存在、`ILockable.Acquire` 的真身是 `LockableExtensions.AddLock`。这属于**内容问题**，
      要先确认真实 API 再改，不要机械替换。

    **遮蔽关系：** CS1570 会掩盖同一块注释里的 CS1574——块解析失败后，块内 cref 一律不再被检查。
    所以「没有 CS1574」推不出「链接都有效」，先清 CS1570 再看 CS1574 的真实数量。

    **本脚本是诊断工具，不是门禁**：只报告数量，不设通过/失败。离零还远时设成门禁只会被无视。

.PARAMETER Module
    只看某个模块（如 Config、UI、Update），并逐条列出该模块的 文件:行号 + 告警内容。

.PARAMETER Project
    要检查的生成项目，默认包自身的两个程序集（Runtime 与 Editor），与 Tests 无关——用例的
    文档注释不随包发布，不在本脚本关注范围。PackageCache 下第三方包的告警一律剔除。

.EXAMPLE
    pwsh -File Tools/check-docs.ps1                    # 全包概览，按模块汇总
    pwsh -File Tools/check-docs.ps1 -Module Config    # Config 模块逐条列出
#>
param(
    [string]$Module = "",
    [string[]]$Project = @("Venusy609.Xframework.csproj", "Venusy609.Xframework.Editor.csproj")
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $repoRoot "Assets\XFramework"))) {
    Write-Error "找不到仓库根（本脚本应位于 <仓库>/Tools/ 下）：$repoRoot"
}

# ---------- 先查「有没有文件根本没被编译」 ----------
#
# csproj 用显式 <Compile Include> 列表而非通配，新建的 .cs 在 Unity 导入并重新生成 csproj
# 之前不在列表里——它会**静默地不参与编译**，于是本脚本对该文件报「0 告警」，看着像干净。
# 这是本项目栽过的坑，故每次先自查一遍，避免给出假的干净结果。

$declared = @()
foreach ($p in $Project) {
    $projPath = Join-Path $repoRoot $p
    if (-not (Test-Path $projPath)) { continue }
    $declared += (Select-String -Path $projPath -Pattern '<Compile Include="([^"]+)"' -AllMatches).Matches |
        ForEach-Object { ($_.Groups[1].Value -replace '\\', '/') }
}
$declaredSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($d in $declared) { [void]$declaredSet.Add($d) }

$onDisk = Get-ChildItem (Join-Path $repoRoot "Assets\XFramework\Runtime"), (Join-Path $repoRoot "Assets\XFramework\Editor") `
    -Recurse -Filter *.cs -ErrorAction SilentlyContinue |
    ForEach-Object { ($_.FullName.Substring($repoRoot.Length + 1) -replace '\\', '/') }
$missing = @($onDisk | Where-Object { -not $declaredSet.Contains($_) })

if ($missing.Count -gt 0) {
    Write-Warning "以下源文件在磁盘上存在但不在任何被检查的 csproj 里——它们没有参与编译，本脚本对它们无话可说："
    $missing | Select-Object -First 10 | ForEach-Object { Write-Host "    $_" -ForegroundColor Yellow }
    if ($missing.Count -gt 10) { Write-Host "    …共 $($missing.Count) 个" -ForegroundColor Yellow }
    Write-Host "  处置：让 Unity 重新生成 csproj（切回编辑器触发一次重编），或临时手插一行 <Compile Include>。" -ForegroundColor Yellow
}

# ---------- 开文档生成、强制全量重编 ----------
#
# -t:Rebuild 必须给：增量构建不会重发警告，会得到一个假的「0 告警」。
# 解析只看 warning CS1570/CS1574 与 文件(行,列)，**不依赖告警文案**——文案随编译器 UI 语言变化。

$warnings = @()
foreach ($p in $Project) {
    $projPath = Join-Path $repoRoot $p
    if (-not (Test-Path $projPath)) {
        Write-Warning "找不到 $p —— Unity 尚未生成该工程？先在编辑器里打开一次工程再跑本脚本。"
        continue
    }

    $docFile = Join-Path $env:TEMP ("xfw-doc-check-" + [IO.Path]::GetFileNameWithoutExtension($p) + ".xml")
    Write-Host "编译 $p …" -ForegroundColor DarkGray
    $raw = dotnet build $projPath --artifacts-path (Join-Path $env:TEMP "xfw-doc-artifacts") `
        -p:DocumentationFile=$docFile -t:Rebuild -v:q --nologo 2>&1

    if ($LASTEXITCODE -ne 0) {
        Write-Error "编译失败（exit=$LASTEXITCODE）。文档告警无从谈起，先修编译错误：`n$($raw | Select-String -Pattern 'error CS' | Select-Object -First 10 | ForEach-Object { $_.Line })"
    }

    foreach ($line in ($raw | Select-String -Pattern 'warning CS1(570|574)')) {
        $text = $line.Line
        if ($text -notmatch '^(.*?)\((\d+),(\d+)\): warning (CS\d+):') { continue }
        $path = $Matches[1]; $lineNo = [int]$Matches[2]; $code = $Matches[4]

        # 只留包自身：剔除 Library/PackageCache 下的第三方包告警
        $rel = $null
        if ($path -match '(?i)[\\/]Assets[\\/]XFramework[\\/](.+)$') { $rel = ($Matches[1] -replace '\\', '/') }
        if (-not $rel) { continue }

        # 文案只用于显示，不参与解析：它随编译器 UI 语言变化，而行号与代码是稳定的。
        # （CS1570 的文案本身含嵌套引号，按引号片段切会切出半截字符串，故原样展示。）
        $msg = ($text -replace '^.*?: warning CS\d+: ', '') -replace '\s*\[[^\]]*\]\s*$', ''

        # 归属项目取告警行尾的 [xxx.csproj]，而不是本循环发起的那个项目：程序集之间有引用关系，
        # 一次 dotnet build 会把被引用者一并编译并重发它的告警（如 Editor 引用 Runtime），
        # 按「我发起了谁」归属会把同一条告警记两遍。
        $owner = $p
        if ($text -match '\[([^\]]+\.csproj)\]\s*$') { $owner = [IO.Path]::GetFileName($Matches[1]) }

        $warnings += [pscustomobject]@{
            Project = $owner
            File    = $rel
            Line    = $lineNo
            Code    = $code
            Msg     = $msg.Trim()
            Module  = if ($rel -match '^(Runtime|Editor)/([^/]+)') { "$($Matches[1])/$($Matches[2])" } else { $rel }
        }
    }
}

# ---------- 报告 ----------

# 身份 = 源文件 + 行号 + 代码 + 文案，**不含 Project**：同一条告警可能由两次编译产生
# （依赖链上的项目各编一次），归属不同但本质是同一条，含 Project 会重复计数。
# 同时也不能只用「文件+行号」——那会把同一行上的不同告警折叠掉（少报）。
# 这两个方向都踩过：先少报（Config 87，实为 107），后重复计数（总数 420，实为约 210）。
$warnings = $warnings | Sort-Object File, Line, Code, Msg -Unique

if ($Module) {
    $sel = @($warnings | Where-Object { $_.Module -like "*$Module*" -or $_.File -like "*$Module*" })
    Write-Host ""
    Write-Host "================ $Module：$($sel.Count) 条 ================" -ForegroundColor Cyan
    if ($sel.Count -eq 0) { Write-Host "无告警。" -ForegroundColor Green }
    else {
        $sel | Group-Object File | Sort-Object Name | ForEach-Object {
            Write-Host ""
            Write-Host "  $($_.Name)" -ForegroundColor White
            $_.Group | ForEach-Object {
                $tag = if ($_.Code -eq "CS1570") { "格式" } else { "cref" }
                Write-Host ("    {0,5}  [{1}] {2}" -f $_.Line, $tag, $_.Msg) -ForegroundColor $(if ($_.Code -eq "CS1570") { "Yellow" } else { "Gray" })
            }
        }
    }
} else {
    Write-Host ""
    Write-Host "================ 按模块汇总 ================" -ForegroundColor Cyan
    $warnings | Group-Object Module | Sort-Object { -($_.Group.Count) } | ForEach-Object {
        $c70 = @($_.Group | Where-Object Code -eq "CS1570").Count
        $c74 = @($_.Group | Where-Object Code -eq "CS1574").Count
        Write-Host ("  {0,-24} 合计 {1,4}   CS1570(格式) {2,4}   CS1574(cref) {3,4}" -f $_.Name, $_.Group.Count, $c70, $c74)
    }
    Write-Host ""
    Write-Host ("  包内合计: {0} 条" -f $warnings.Count) -ForegroundColor Cyan
    Write-Host "  提示：先清 CS1570（它是遮蔽源），再看 CS1574 的真实数量。逐条查看用 -Module <名字>。" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "说明：本脚本只报告数量，不判定通过/失败；包外（Library/PackageCache）的告警已剔除。" -ForegroundColor DarkGray
exit 0
