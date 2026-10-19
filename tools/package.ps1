<#
.SYNOPSIS
    生成 Zevision 发布包 (见 docs/RELEASE.md "打包"):dist\Zevision-<版本>\、两个 zip、SHA256 和调试符号。
.DESCRIPTION
    1. Release 重新构建 (0 警告) 并运行全部单元测试 (-SkipBuild 跳过)。
    2. 组装发布文件夹:程序目录只有 Zevision.exe、Zevision.exe.config (含 probing privatePath="lib")、Texts.json 和使用说明;
       全部 dll 在 lib\;.NET Framework 4.8 离线安装包在 dotnet\ (先核对 Microsoft 数字签名)。
    3. 压缩为两个 zip 并输出各自的 SHA256:Zevision-<版本>_wDotnet.zip (含 dotnet\) 和 Zevision-<版本>_woDotnet.zip
       (不含 dotnet\,用于已有 .NET Framework 4.7.2 或更高版本的计算机);核对两者只差 dotnet\。
       各 pdb 存到 dist\symbols\Zevision-<版本>\ (留档,不发布)。
    4. 把不含 dotnet\ 的 zip 解压到临时目录启动一次,确认标题栏的版本,再关闭。程序已在运行时跳过 (单实例:新启动的会直接退出)。
.PARAMETER SkipBuild
    不构建、不运行测试,直接用 bin\Release 中的文件。
.PARAMETER DotNetInstaller
    .NET Framework 4.8 离线安装包 (ndp48-x86-x64-allos-enu.exe);默认取 dist 中最近的一个。
.PARAMETER OutDir
    输出目录,默认为仓库的 dist。
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\package.ps1
#>
param(
    [switch]$SkipBuild,
    [string]$DotNetInstaller,
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $OutDir) { $OutDir = Join-Path $root 'dist' }
$msbuild = 'C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe'
$xunit = Join-Path $env:USERPROFILE '.nuget\packages\xunit.runner.console\2.9.3\tools\net472\xunit.console.exe'
$bin = Join-Path $root 'bin\Release'

if (-not $SkipBuild) {
    & $msbuild (Join-Path $root 'Zevision.sln') -restore -t:Rebuild -p:Configuration=Release -nologo -v:minimal
    if ($LASTEXITCODE -ne 0) { throw '构建失败' }
    & $xunit (Join-Path $root 'Zevision.Tests\bin\Release\Zevision.Tests.dll') -nologo
    if ($LASTEXITCODE -ne 0) { throw '单元测试失败' }
}

$version = (Get-Item (Join-Path $bin 'Zevision.exe')).VersionInfo.ProductVersion
$name = "Zevision-$version"
$pkg = Join-Path $OutDir $name
$zipFull = $pkg + '_wDotnet.zip'
$zipLite = $pkg + '_woDotnet.zip'
foreach ($existing in $pkg, $zipFull, $zipLite) {
    if (Test-Path $existing) { throw "$existing 已存在:版本号没有提高,或先删除旧的输出" }
}

if (-not $DotNetInstaller) {
    $DotNetInstaller = Get-ChildItem (Join-Path $root 'dist') -Recurse -Filter 'ndp48-x86-x64-allos-enu.exe' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $DotNetInstaller -or -not (Test-Path $DotNetInstaller)) { throw '找不到 .NET Framework 4.8 离线安装包,请用 -DotNetInstaller 指定' }
$signature = Get-AuthenticodeSignature $DotNetInstaller
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
    throw ".NET 安装包的数字签名无效: $($signature.Status) $($signature.SignerCertificate.Subject)"
}

# 发布文件夹:程序目录只放 exe、配置、翻译文件和使用说明,dll 在 lib\ (由 Zevision.exe.config 的 probing 找到)。
New-Item -ItemType Directory (Join-Path $pkg 'lib'), (Join-Path $pkg 'dotnet') | Out-Null
Copy-Item (Join-Path $bin 'Zevision.exe'), (Join-Path $bin 'Zevision.exe.config'), (Join-Path $bin 'Texts.json') $pkg
Get-ChildItem $bin -Filter '*.dll' | Copy-Item -Destination (Join-Path $pkg 'lib')
Copy-Item (Join-Path $root 'docs\USER_GUIDE.md') (Join-Path $pkg '使用说明.md')
Copy-Item (Join-Path $root 'docs\USER_GUIDE.en.md') (Join-Path $pkg 'User Guide.md')
Copy-Item $DotNetInstaller (Join-Path $pkg 'dotnet')
if (-not (Select-String -Path (Join-Path $pkg 'Zevision.exe.config') -Pattern 'privatePath="lib"' -SimpleMatch -Quiet)) {
    throw 'Zevision.exe.config 中没有 probing privatePath="lib",程序会找不到 lib 中的 dll'
}

# 调试符号:留档,用于分析用户反馈的异常堆栈。
$symbols = Join-Path $OutDir "symbols\$name"
New-Item -ItemType Directory $symbols -Force | Out-Null
Get-ChildItem $bin -Filter 'Zevision*.pdb' | Copy-Item -Destination $symbols

Compress-Archive -Path $pkg -DestinationPath $zipFull

# 不含 .NET 安装包的版本:在临时目录中组装同名文件夹 (zip 中的顶层文件夹名相同),去掉 dotnet\ 后压缩。
$stage = Join-Path ([IO.Path]::GetTempPath()) ('zevision_pkg_' + [Guid]::NewGuid().ToString('N'))
Copy-Item $pkg (Join-Path $stage $name) -Recurse
Remove-Item (Join-Path $stage "$name\dotnet") -Recurse -Force
Compress-Archive -Path (Join-Path $stage $name) -DestinationPath $zipLite
Remove-Item $stage -Recurse -Force

# 两个 zip 只差 dotnet\ 中的文件。
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Get-ZipEntries([string]$path) {
    $archive = [IO.Compression.ZipFile]::OpenRead($path)
    try { return @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') } | Sort-Object) }
    finally { $archive.Dispose() }
}
$fullEntries = Get-ZipEntries $zipFull
$liteEntries = Get-ZipEntries $zipLite
$expected = @($fullEntries | Where-Object { $_ -notlike "$name/dotnet/*" })
if ($expected.Count -eq $fullEntries.Count) { throw "$zipFull 中没有 dotnet\" }
if (Compare-Object $expected $liteEntries) { throw "$zipLite 的内容与 $zipFull 去掉 dotnet\ 后不同" }

$hashFull = (Get-FileHash $zipFull -Algorithm SHA256).Hash
$hashLite = (Get-FileHash $zipLite -Algorithm SHA256).Hash

# 把不含 .NET 安装包的 zip 解压到临时目录启动一次 (不在发布文件夹中启动:会生成 settings.json、logs\)。
$check = Join-Path ([IO.Path]::GetTempPath()) ('zevision_check_' + [Guid]::NewGuid().ToString('N'))
Expand-Archive $zipLite -DestinationPath $check
$exe = Join-Path $check "$name\Zevision.exe"
if (Get-Process Zevision -ErrorAction SilentlyContinue) {
    Write-Warning ('Zevision 正在运行 (单实例),跳过启动检查。关闭后手动启动一次: ' + $exe)
}
else {
    $p = Start-Process $exe -PassThru
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($p.MainWindowHandle -eq 0 -and -not $p.HasExited -and $sw.Elapsed.TotalSeconds -lt 30) {
        Start-Sleep -Milliseconds 200
        $p.Refresh()
    }
    if ($p.HasExited) { throw "程序启动后退出 (退出码 $($p.ExitCode)),见 $check\$name\logs\error.txt" }
    $title = $p.MainWindowTitle
    $p.CloseMainWindow() | Out-Null
    if (-not $p.WaitForExit(15000)) { $p.Kill() }
    if ($title -notlike "*Zevision $version") { throw "标题栏不是预期的版本: $title" }
    Write-Host "启动检查通过: $title"
    Remove-Item $check -Recurse -Force
}

Write-Host "发布包 (含 .NET 安装包): $zipFull"
Write-Host "SHA256: $hashFull"
Write-Host "发布包 (不含 .NET 安装包): $zipLite"
Write-Host "SHA256: $hashLite"
Write-Host "两个 SHA256 填入 docs/RELEASE.md 的版本表"
Write-Host "调试符号: $symbols"
