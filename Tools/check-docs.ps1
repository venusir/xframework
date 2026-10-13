<#
.SYNOPSIS
    XFramework 包内自检：XML 文档注释的编译告警（按码统计，门禁码见「覆盖范围」）+ 资源 .meta 完整性。

.DESCRIPTION
    **一、文档注释告警。** Unity 生成的 csproj 没有设置 DocumentationFile，因此**默认编译根本
    不检查文档注释**：`<see cref="..."/>` 解析不了也不报错。本脚本显式开启文档生成、强制全量
    重编一次，把这条从未走过的检查通道照亮。

    为什么要看这些告警（前两类最重，逐条详述如下；完整码表与门禁/诊断划分见「覆盖范围」）：
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
      ③ cref 要写全形以消二义：有重载的成员必须写参数列表——无参重载写 `Foo()`、带参写全
         `Foo(int, float)`；只在该名字没有重载时才能写裸名。`InitializeAsync()`（该方法带参）非法、
         `Tick()`（无参重载存在）合法，「cref 不能带 ()」是过度概括；写不全触发 **CS0419**——它
         **在本脚本的统计面与门禁面之内**（见下「覆盖范围」）。
      ④ 本文件缺 using，导致 cref 的**参数类型**解析不了（如 `CancellationToken`、`IUIController`）——
         把该类型写成全名即可，不必为此给文件加 using。
      另有一类是文档指向**根本不存在的东西**（已改名或已删除的成员/类型），例如 `UIHudManager` 这个类
      在仓内并不存在、`ILockable.Acquire` 的真身是 `LockableExtensions.AddLock`。这属于**内容问题**，
      要先确认真实 API 再改，不要机械替换。

    **覆盖范围：** 本脚本统计这条检查通道里的十一个码，各带中文标签（默认视图按码分列，末尾的
    「门禁码合计 / 诊断码合计」两条线标明哪张表管什么；逐条视图按码打标）：CS1570（XML 格式错误）、
    CS1574（cref 无法解析）、CS0419（cref 二义）、CS1591（公开成员缺注释）、CS1573（param 缺说明）、
    CS1571 / CS1572（param tag 重复 / 指向不存在的参数）、CS1734 / CS1735（paramref / typeparamref
    名字不匹配）、CS1584（cref 语法错误）、CS1658（与 CS1584 成对的细节消息）。**其中门禁码进
    -Enforce，其余只诊断、不判失败**——两张表都在脚本顶部，改码只改那里（扩容走「先诊断看曲线、
    清完存量再纳门」，先例见 `Documentation/ModuleAudit.md` §八）。统计面之外的其它编译告警只计
    一行明示、不进任何判定；要看完整分布加 `-p:DocumentationFile=<路径> -t:Rebuild` 从原始输出里筛。

    **遮蔽关系：** CS1570 会掩盖同一块注释里的 CS1574——块解析失败后，块内 cref 一律不再被检查。
    所以「没有 CS1574」推不出「链接都有效」，先清 CS1570 再看 CS1574 的真实数量。

    **二、包完整性（`.meta`）。** 本包以 UPM 形式供第三方引入，资源缺 `.meta` 会让对方导入时
    生成**不同的 GUID**——若该文件被 prefab/scene 引用就是断链。逐个检查几乎零成本，故与文档告警
    一并报告、一并在 `-Enforce` 下判失败。
    注意 Unity 自身不给「点开头的文件/目录」与「`~` 结尾」的条目生成 meta（如
    `Samples~/Example/.sample.json`），这两类一律跳过，否则会永远误报。

    **默认是诊断工具**：只报告数量、不判定通过/失败；加 `-Enforce` 才当门禁（**门禁码**的告警须为 0、
    所有源文件都须参与编译、所有条目都须有 `.meta`）。

.PARAMETER Module
    只看某个模块（如 Config、UI、Update），并逐条列出该模块的 文件:行号 + 告警内容。

.PARAMETER Project
    要检查的生成项目，默认包自身的两个程序集（Runtime 与 Editor），与 Tests 无关——用例的
    文档注释不随包发布，不在本脚本关注范围。PackageCache 下第三方包的告警一律剔除。

    **为什么不用 `.Editor.Tests.csproj` 单入口**（它的引用链一次编译即覆盖三者，能省一次编译）？
    2026-09-27 实测证伪：`-p:DocumentationFile` **不会透传给被引用的工程**——用它编译时只有入口
    工程自己产出文档，包程序集的告警一条也不会出现（在 Runtime 侧注入缺陷验证过；生成的 XML 里
    也只有入口工程的类型）。所以必须**逐工程**编译，别为此改了默认值。

.PARAMETER Enforce
    门禁模式：**门禁码**（脚本顶部的 $GatedCodes）的包内告警数必须为 0，否则退出码 1；未纳门的
    统计码只诊断、不参与判定。供阶段收尾时与全量测试一并跑。
    注意本模式下「有源文件未参与编译」同样判为失败——那种情况下检查结果是残缺的
    （对未编译的文件无从告警），它会给出一个假的「0 条」，比真有告警更危险。

.EXAMPLE
    pwsh -File Tools/check-docs.ps1                    # 全包概览，按模块汇总（诊断）
    pwsh -File Tools/check-docs.ps1 -Module Config    # Config 模块逐条列出
    pwsh -File Tools/check-docs.ps1 -Enforce           # 门禁：0 条才通过
#>
param(
    [string]$Module = "",
    [string[]]$Project = @("Venusy609.Xframework.csproj", "Venusy609.Xframework.Editor.csproj"),
    [switch]$Enforce
)

$ErrorActionPreference = "Stop"

# ---------- 统计面与门禁面（两处唯一真相，改码只改这里） ----------
#
# $CodeLabels：本脚本统计的告警码 → 中文标签。**键集就是统计面**，顺序即汇总列序。
# $GatedCodes：其中进 -Enforce 门禁的码（必须 ⊆ 上表的键）。未列入的码只诊断、不判失败——
#   这是「先诊断看曲线、清完存量再往门禁里挪」的分阶段形态（先例与结论见脚本头部「覆盖范围」）。
# 统计面之外的码（编译器/分析器的其它告警）：只计一行明示、不进任何判定——不制造新的隐形面。
$CodeLabels = [ordered]@{
    "CS1570" = "格式"          # XML 格式错误——整块注释被编译器丢弃（遮蔽源）
    "CS1574" = "cref"          # cref 无法解析
    "CS0419" = "二义"          # cref 重载二义——编译器替你「假设」一个重载
    "CS1591" = "缺注释"        # 公开成员缺 XML 注释
    "CS1573" = "缺 param"      # 部分参数有 param tag、部分没有
    "CS1571" = "重复 param"    # 同一参数的 param tag 出现多次
    "CS1572" = "多余 param"    # param tag 指向不存在的参数
    "CS1734" = "paramref"      # paramref 指向的参数不存在
    "CS1735" = "typeparamref"  # typeparamref 指向的类型参数不存在
    "CS1584" = "cref 语法"     # cref 属性语法错误（与 CS1658 成对）
    "CS1658" = "见上文"        # CS1584 的细节消息，成对出现（文案以实测为准）
}
$GatedCodes = @("CS1570", "CS1574")
$CodeColors = @{ "CS1570" = "Yellow"; "CS0419" = "Magenta" }   # 其余统计码 Gray
$diagCodes = @($CodeLabels.Keys | Where-Object { $GatedCodes -notcontains $_ })

foreach ($c in $GatedCodes) {
    if (-not $CodeLabels.Contains($c)) {
        Write-Error "门禁码 $c 不在统计码表内（GatedCodes 必须 ⊆ CodeLabels 的键）。"
    }
}

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

# ---------- 再查「包内资源有没有漏 .meta」 ----------
#
# 本包以 UPM 形式供第三方引入，缺 .meta 会让对方导入时生成**不同的 GUID**——若该文件被
# prefab/scene 引用就是断链。检查几乎零成本，故与文档告警一并报告、一并在 -Enforce 下判失败。
#
# Unity 自身不给「点开头的文件/目录」与「~ 结尾」的条目生成 meta（如 Samples~/Example/.sample.json），
# 这两类一律跳过，否则会永远误报。

$pkgRoot = Join-Path $repoRoot "Assets\XFramework"
$metaMissing = @()
foreach ($item in (Get-ChildItem $pkgRoot -Recurse -Force -ErrorAction SilentlyContinue)) {
    $name = $item.Name
    if ($name -like '*.meta' -or $name.StartsWith('.') -or $name.EndsWith('~')) { continue }
    if (-not (Test-Path ("$($item.FullName).meta"))) {
        $metaMissing += ($item.FullName.Substring($repoRoot.Length + 1) -replace '\\', '/')
    }
}

# ---------- 开文档生成、强制全量重编 ----------
#
# -t:Rebuild 必须给：增量构建不会重发警告，会得到一个假的「0 告警」。
# 解析只看 warning CSxxxx 与 文件(行,列)，**不依赖告警文案**——文案随编译器 UI 语言变化；
# 哪些码进统计面由上面的 $CodeLabels 决定（表外码只进「不在统计面内」一行，不进门禁）。

$warnings = @()
$other = @()
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

    foreach ($line in ($raw | Select-String -Pattern 'warning CS\d')) {
        $text = $line.Line
        if ($text -notmatch '^(.*?)\((\d+),(\d+)\): warning (CS\d+):') { continue }
        $path = $Matches[1]; $lineNo = [int]$Matches[2]; $code = $Matches[4]

        # 只留包自身：剔除 Library/PackageCache 下的第三方包告警
        $rel = $null
        if ($path -match '(?i)[\\/]Assets[\\/]XFramework[\\/](.+)$') { $rel = ($Matches[1] -replace '\\', '/') }
        if (-not $rel) { continue }

        # 统计面之外的码只记一行明示、永不进门禁——把候选码收进统计面，正是为了不再有
        # 「这条通道产出、脚本看不见」的隐形面（CS0419 的教训）。
        if (-not $CodeLabels.Contains($code)) {
            $other += [pscustomobject]@{ File = $rel; Line = $lineNo; Code = $code }
            continue
        }

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
$other = $other | Sort-Object File, Line, Code -Unique

if ($Module) {
    $sel = @($warnings | Where-Object { $_.Module -like "*$Module*" -or $_.File -like "*$Module*" })
    Write-Host ""
    if ($sel.Count -eq 0) {
        Write-Host "================ $Module：0 条 ================" -ForegroundColor Cyan
        Write-Host "无告警。" -ForegroundColor Green
    } else {
        $byCode = ($sel | Group-Object Code | Sort-Object Name | ForEach-Object { "$($_.Name)×$($_.Count)" }) -join "、"
        Write-Host "================ $Module：$($sel.Count) 条（$byCode） ================" -ForegroundColor Cyan
        $sel | Group-Object File | Sort-Object Name | ForEach-Object {
            Write-Host ""
            Write-Host "  $($_.Name)" -ForegroundColor White
            $_.Group | ForEach-Object {
                $tag = if ($CodeLabels.Contains($_.Code)) { $CodeLabels[$_.Code] } else { $_.Code }
                $color = if ($CodeColors.ContainsKey($_.Code)) { $CodeColors[$_.Code] } else { "Gray" }
                Write-Host ("    {0,5}  [{1}] {2}" -f $_.Line, $tag, $_.Msg) -ForegroundColor $color
            }
        }
    }
} else {
    Write-Host ""
    Write-Host "================ 按模块汇总 ================" -ForegroundColor Cyan

    # 分码计数：哈希桶首次访问返回 $null，一律用 [int] 归一。
    # 列集 = 本跑非零的统计码（所有行共用同一列集保证对齐；稳态全 0 时输出与旧版等宽）。
    $codeCounts = @{}
    foreach ($w in $warnings) { $codeCounts[$w.Code] = 1 + [int]$codeCounts[$w.Code] }
    $activeCodes = @($CodeLabels.Keys | Where-Object { [int]$codeCounts[$_] -gt 0 })

    $warnings | Group-Object Module | Sort-Object { -($_.Group.Count) } | ForEach-Object {
        $group = $_.Group
        $rowCounts = @{}
        foreach ($w in $group) { $rowCounts[$w.Code] = 1 + [int]$rowCounts[$w.Code] }
        $row = "  {0,-24} 合计 {1,4}" -f $_.Name, $group.Count
        foreach ($c in $activeCodes) { $row += "   {0}({1}) {2,4}" -f $c, $CodeLabels[$c], [int]$rowCounts[$c] }
        Write-Host $row
    }
    Write-Host ""
    Write-Host ("  包内合计: {0} 条" -f $warnings.Count) -ForegroundColor Cyan

    # 门禁码 / 诊断码两条合计线——它们本身就是「哪张表管什么」的常驻说明书
    $gatedParts = @(foreach ($c in $GatedCodes) { "{0}({1}) {2}" -f $c, $CodeLabels[$c], [int]$codeCounts[$c] })
    Write-Host ("  门禁码合计: {0}" -f ($gatedParts -join "   ")) -ForegroundColor DarkGray
    if ($diagCodes.Count -gt 0) {
        $diagParts = @(foreach ($c in $diagCodes) { "{0}({1}) {2}" -f $c, $CodeLabels[$c], [int]$codeCounts[$c] })
        Write-Host ("  诊断码合计: {0}（只诊断、不判失败）" -f ($diagParts -join "   ")) -ForegroundColor DarkGray
    }
    if ($other.Count -gt 0) {
        $otherParts = ($other | Group-Object Code | Sort-Object Name | ForEach-Object { "$($_.Name)×$($_.Count)" }) -join "、"
        Write-Host ("  另有 {0} 条包内告警不在统计面内（{1}）——见脚本头部「覆盖范围」" -f $other.Count, $otherParts) -ForegroundColor DarkGray
    }
    if ($metaMissing.Count -eq 0) {
        Write-Host "  包完整性: 全部条目都有 .meta" -ForegroundColor DarkGray
    } else {
        Write-Host ("  包完整性: 缺 .meta 的条目 {0} 个" -f $metaMissing.Count) -ForegroundColor Yellow
        $metaMissing | Select-Object -First 10 | ForEach-Object { Write-Host "      $_" -ForegroundColor Yellow }
        if ($metaMissing.Count -gt 10) { Write-Host "      …共 $($metaMissing.Count) 个" -ForegroundColor Yellow }
    }
    Write-Host "  提示：CS1570 会遮蔽同一块注释里的其它告警（先清它，再看其余码的真实数量）；未纳门的码只诊断、不判失败（清单见脚本头部「覆盖范围」）。逐条查看用 -Module <名字>。" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "说明：包外（Library/PackageCache）的告警已剔除。" -ForegroundColor DarkGray

if (-not $Enforce) {
    Write-Host ("本脚本默认只报告数量、不判定通过/失败；要当门禁用请加 -Enforce（门禁码须为 0：{0}）。" -f ($GatedCodes -join " / ")) -ForegroundColor DarkGray
    exit 0
}

# ---------- 门禁模式 ----------

$problems = @()
$gatedWarnings = @($warnings | Where-Object { $GatedCodes -contains $_.Code })
if ($gatedWarnings.Count -gt 0) {
    $byCode = ($gatedWarnings | Group-Object Code | Sort-Object Name | ForEach-Object { "$($_.Name)×$($_.Count)" }) -join "、"
    $problems += "包内 XML 文档告警 $($gatedWarnings.Count) 条（须为 0）：$byCode"
}
if ($metaMissing.Count -gt 0) { $problems += "包内有 $($metaMissing.Count) 个条目缺 .meta（第三方导入会拿到不同 GUID）" }
if ($missing.Count -gt 0) { $problems += "有 $($missing.Count) 个源文件未参与编译——检查结果残缺，此时报的「0 条」不可信" }

if ($problems.Count -gt 0) {
    Write-Host ""
    Write-Host "文档门禁：不通过" -ForegroundColor Red
    $problems | ForEach-Object { Write-Host "  · $_" -ForegroundColor Red }
    Write-Host "  逐条查看：pwsh -File Tools/check-docs.ps1 -Module <模块名>" -ForegroundColor DarkGray
    exit 1
}

Write-Host ""
# 绿 = 门禁码为 0，**不等于**整条通道干净——未纳门码的存量在通过时也要看得见
$diagLeft = @($warnings | Where-Object { $diagCodes -contains $_.Code })
if ($diagLeft.Count -gt 0) {
    $leftParts = ($diagLeft | Group-Object Code | Sort-Object Name | ForEach-Object { "$($_.Name)×$($_.Count)" }) -join "、"
    Write-Host ("未纳门码（只诊断、不判失败）：{0}" -f $leftParts) -ForegroundColor DarkYellow
}
Write-Host ("文档门禁：通过（门禁码 0 条：{0}，且所有源文件都参与了编译）" -f ($GatedCodes -join " / ")) -ForegroundColor Green
exit 0
