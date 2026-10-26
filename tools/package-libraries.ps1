<#
.SYNOPSIS
    生成链接库发布包 (见 docs/RELEASE.md "链接库打包"):dist\Zevision.Libraries-<版本>\、zip、SHA256 和调试符号。
.DESCRIPTION
    1. Release 重新构建 (0 警告) 并运行全部单元测试 (-SkipBuild 跳过)。
    2. 组装发布文件夹:Zevision.Communication.dll、Zevision.Commands.dll 及其 XML 文档,LIBRARY.md (ZEV-CPM-01),
       sample\Program.cs (samples\Zevision.Sample)。
    3. 压缩为 zip 并输出 SHA256;两个库的 pdb 存到 dist\symbols\Zevision.Libraries-<版本>\ (留档,不发布)。
    4. 把 zip 解压到临时目录,只引用解压出的两个 dll 编译 sample\Program.cs。不连接设备。
.PARAMETER Version
    发布包的版本。默认取两个库中较高的版本;该版本的发布包已存在 (较高的版本未变,另一个库有变化) 时,
    指定在其后加修订号的版本,如 1.2.1 = 通信库 1.2.0 + 指令集库 1.1.1。
.PARAMETER SkipBuild
    不构建、不运行测试,直接用各库 bin\Release 中的文件。
.PARAMETER OutDir
    输出目录,默认为仓库的 dist。
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\package-libraries.ps1 -Version 1.2.1
#>
param(
    [string]$Version,
    [switch]$SkipBuild,
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $OutDir) { $OutDir = Join-Path $root 'dist' }
$msbuild = 'C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe'
$csc = 'C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$xunit = Join-Path $env:USERPROFILE '.nuget\packages\xunit.runner.console\2.9.3\tools\net472\xunit.console.exe'

if (-not $SkipBuild) {
    & $msbuild (Join-Path $root 'Zevision.sln') -restore -t:Rebuild -p:Configuration=Release -nologo -v:minimal
    if ($LASTEXITCODE -ne 0) { throw '构建失败' }
    & $xunit (Join-Path $root 'Zevision.Tests\bin\Release\Zevision.Tests.dll') -nologo
    if ($LASTEXITCODE -ne 0) { throw '单元测试失败' }
}

$libraries = 'Zevision.Communication', 'Zevision.Commands'
$versions = @{}
foreach ($library in $libraries) {
    $versions[$library] = [Version](Get-Item (Join-Path $root "$library\bin\Release\$library.dll")).VersionInfo.ProductVersion
}
$highest = ($versions.Values | Sort-Object -Descending | Select-Object -First 1).ToString()
if (-not $Version) { $Version = $highest }
if ([Version]$Version -lt [Version]$highest) { throw "发布包的版本 $Version 低于库的版本 $highest" }

$name = "Zevision.Libraries-$Version"
$pkg = Join-Path $OutDir $name
$zip = $pkg + '.zip'
foreach ($existing in $pkg, $zip) {
    if (Test-Path $existing) { throw "$existing 已存在:版本号没有提高 (较高的库版本未变时用 -Version 加修订号),或先删除旧的输出" }
}

New-Item -ItemType Directory (Join-Path $pkg 'sample') | Out-Null
foreach ($library in $libraries) {
    $bin = Join-Path $root "$library\bin\Release"
    Copy-Item (Join-Path $bin "$library.dll"), (Join-Path $bin "$library.xml") $pkg
}
Copy-Item (Join-Path $root 'docs\LIBRARY.md') $pkg
Copy-Item (Join-Path $root 'samples\Zevision.Sample\Program.cs') (Join-Path $pkg 'sample')

# 调试符号:留档,用于分析用户反馈的异常堆栈。
$symbols = Join-Path $OutDir "symbols\$name"
New-Item -ItemType Directory $symbols -Force | Out-Null
foreach ($library in $libraries) {
    Copy-Item (Join-Path $root "$library\bin\Release\$library.pdb") $symbols
}

Compress-Archive -Path $pkg -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash

# 解压到临时目录,只引用解压出的两个 dll 编译示例,确认发布包中的库可以单独使用。
$check = Join-Path ([IO.Path]::GetTempPath()) ('zevision_libs_check_' + [Guid]::NewGuid().ToString('N'))
Expand-Archive $zip -DestinationPath $check
$dir = Join-Path $check $name
$sample = Join-Path $check 'Zevision.Sample.exe'
$references = $libraries | ForEach-Object { '/reference:' + (Join-Path $dir "$_.dll") }
& $csc /nologo /langversion:9 /nullable:enable /target:exe ('/out:' + $sample) @references /reference:System.dll /reference:System.Core.dll (Join-Path $dir 'sample\Program.cs')
if ($LASTEXITCODE -ne 0) { throw '用发布包中的 dll 编译 sample\Program.cs 失败' }
Remove-Item $check -Recurse -Force
Write-Host "发布包检查通过: 示例编译"

Write-Host ("通信库 " + $versions['Zevision.Communication'] + ",指令集库 " + $versions['Zevision.Commands'])
Write-Host "发布包: $zip"
Write-Host "SHA256: $hash"
Write-Host "SHA256 填入 docs/RELEASE.md 的链接库发布包表"
Write-Host "调试符号: $symbols"
