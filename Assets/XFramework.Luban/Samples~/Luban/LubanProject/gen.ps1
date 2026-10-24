# 用法：先 cd 到本目录，再把 -LubanDll 指向你的 Luban.dll。
# 正常流程建议直接用 Editor 菜单 XFramework/Luban/生成配置表；
# 这个脚本是「手动导表」的回退路径。
param(
    [Parameter(Mandatory = $true)]
    [string]$LubanDll
)

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$codeDir = Join-Path $root '..\Generated\Code'
$dataDir = Join-Path $root '..\Data'

dotnet $LubanDll -t client -c cs-simple-json -d json `
    --conf (Join-Path $root 'luban.conf') `
    -x "outputCodeDir=$codeDir" `
    -x "outputDataDir=$dataDir"
