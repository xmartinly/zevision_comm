<#
.SYNOPSIS
    生成 XTC/3S 中间层发布包 (见 docs/RELEASE.md "中间层打包"):dist\Zevision.Xtc3Bridge-<版本>\、zip、SHA256 和调试符号。
.DESCRIPTION
    1. Release 重新构建 (0 警告) 并运行全部单元测试 (-SkipBuild 跳过)。
    2. 核对 Zevision.Xtc3、Zevision.Bridge、Zevision.Bridge.Hosting 和 Zevision.Xtc3Bridge.exe 的版本相同。
    3. 组装发布文件夹:独立程序 Zevision.Xtc3Bridge.exe、7 个 dll 及其 XML 文档 (进程内使用时引用)、Texts.json、
       编程手册 (ZEV-CPM-03) 和 sample\Program.cs (独立程序的源代码,即进程内使用的示例)。
    4. 压缩为 zip 并输出 SHA256;各 pdb 存到 dist\symbols\Zevision.Xtc3Bridge-<版本>\ (留档,不发布)。
    5. 把 zip 解压到临时目录检查:不带参数运行显示用法,init 写出设置文件,只引用解压出的 dll 编译 sample\Program.cs。
       不连接设备;与设备的联调见 ZEV-STR-01。
.PARAMETER SkipBuild
    不构建、不运行测试,直接用 samples\Zevision.Xtc3Bridge\bin\Release 中的文件。
.PARAMETER OutDir
    输出目录,默认为仓库的 dist。
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\package-bridge.ps1
#>
param(
    [switch]$SkipBuild,
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $OutDir) { $OutDir = Join-Path $root 'dist' }
$msbuild = 'C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe'
$csc = 'C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$xunit = Join-Path $env:USERPROFILE '.nuget\packages\xunit.runner.console\2.9.3\tools\net472\xunit.console.exe'
$bin = Join-Path $root 'samples\Zevision.Xtc3Bridge\bin\Release'

if (-not $SkipBuild) {
    & $msbuild (Join-Path $root 'Zevision.sln') -restore -t:Rebuild -p:Configuration=Release -nologo -v:minimal
    if ($LASTEXITCODE -ne 0) { throw '构建失败' }
    & $xunit (Join-Path $root 'Zevision.Tests\bin\Release\Zevision.Tests.dll') -nologo
    if ($LASTEXITCODE -ne 0) { throw '单元测试失败' }
}

# 中间层的三个库和独立程序版本相同;依赖的通信库、指令集库、业务层、存储层按各自的版本。
$exeName = 'Zevision.Xtc3Bridge.exe'
$version = (Get-Item (Join-Path $bin $exeName)).VersionInfo.ProductVersion
foreach ($file in 'Zevision.Xtc3.dll', 'Zevision.Bridge.dll', 'Zevision.Bridge.Hosting.dll') {
    $v = (Get-Item (Join-Path $bin $file)).VersionInfo.ProductVersion
    if ($v -ne $version) { throw "$file 的版本 $v 与 $exeName 的 $version 不同" }
}

$name = "Zevision.Xtc3Bridge-$version"
$pkg = Join-Path $OutDir $name
$zip = $pkg + '.zip'
foreach ($existing in $pkg, $zip) {
    if (Test-Path $existing) { throw "$existing 已存在:版本号没有提高,或先删除旧的输出" }
}

$libraries = 'Zevision.Xtc3', 'Zevision.Bridge', 'Zevision.Bridge.Hosting', 'Zevision.Core', 'Zevision.Storage', 'Zevision.Communication', 'Zevision.Commands'
New-Item -ItemType Directory (Join-Path $pkg 'sample') | Out-Null
Copy-Item (Join-Path $bin $exeName), (Join-Path $bin 'Texts.json') $pkg
foreach ($library in $libraries) {
    Copy-Item (Join-Path $bin "$library.dll"), (Join-Path $bin "$library.xml") $pkg
}
Copy-Item (Join-Path $root 'docs\XTC3中间层编程手册.md') $pkg
Copy-Item (Join-Path $root 'samples\Zevision.Xtc3Bridge\Program.cs') (Join-Path $pkg 'sample')

# 调试符号:留档,用于分析用户反馈的异常堆栈。
$symbols = Join-Path $OutDir "symbols\$name"
New-Item -ItemType Directory $symbols -Force | Out-Null
Get-ChildItem $bin -Filter 'Zevision*.pdb' | Copy-Item -Destination $symbols

Compress-Archive -Path $pkg -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash

# 解压到临时目录检查 (不在发布文件夹中运行:init 会写出设置文件)。
$check = Join-Path ([IO.Path]::GetTempPath()) ('xtc3bridge_check_' + [Guid]::NewGuid().ToString('N'))
Expand-Archive $zip -DestinationPath $check
$dir = Join-Path $check $name
$exe = Join-Path $dir $exeName

$usage = & $exe 2>&1 | Out-String
if ($LASTEXITCODE -ne 2 -or $usage -notmatch 'Zevision XTC/3S bridge') { throw "不带参数运行没有显示用法 (退出码 $LASTEXITCODE): $usage" }

$settings = Join-Path $check 'check.settings.json'
& $exe init --host COM2 --device 127.0.0.1 --settings $settings | Out-Null
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $settings)) { throw "init 没有写出设置文件 (退出码 $LASTEXITCODE)" }
if ((Get-Content $settings -Raw) -notmatch '"device"\s*:\s*"127\.0\.0\.1"') { throw "设置文件的内容不对: $settings" }

# 只引用解压出的 dll 编译示例,确认发布包中的库可以单独使用。
$references = $libraries | ForEach-Object { '/reference:' + (Join-Path $dir "$_.dll") }
& $csc /nologo /langversion:9 /nullable:enable /target:exe ('/out:' + (Join-Path $check 'sample.exe')) @references /reference:System.dll /reference:System.Core.dll (Join-Path $dir 'sample\Program.cs')
if ($LASTEXITCODE -ne 0) { throw '用发布包中的 dll 编译 sample\Program.cs 失败' }
Remove-Item $check -Recurse -Force
Write-Host "发布包检查通过: 用法、init、示例编译"

Write-Host "发布包: $zip"
Write-Host "SHA256: $hash"
Write-Host "SHA256 填入 docs/RELEASE.md 的中间层发布包表"
Write-Host "调试符号: $symbols"
