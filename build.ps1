# 编译 PeStubGen：.NET Framework 4.8 + 系统自带 csc.exe，不需要安装任何 SDK。
# 用法：
#   powershell -ExecutionPolicy Bypass -File build.ps1            # 全部
#   powershell -ExecutionPolicy Bypass -File build.ps1 -Target Cli
param(
    [ValidateSet('All', 'Gui', 'Cli')]
    [string]$Target = 'All',
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'

$root   = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc    = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$refDir = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'

if (-not (Test-Path $csc))    { throw "找不到 csc.exe：$csc" }
if (-not (Test-Path $refDir)) { throw "找不到 .NET Framework 4.8 引用程序集：$refDir" }

$outDir = Join-Path $root 'out'
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

$refs = @(
    'mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Xml.dll',
    'WindowsBase.dll', 'PresentationCore.dll', 'PresentationFramework.dll', 'System.Xaml.dll', 'System.Windows.Forms.dll'
) | ForEach-Object { '/r:' + (Join-Path $refDir $_) }

$core = @(
    'src\Models.cs',
    'src\PeFile.cs',
    'src\PeImageReader.cs',
    'src\PeImports.cs',
    'src\PeExports.cs',
    'src\DllClassifier.cs',
    'src\StubGenerator.cs',
    'src\Toolchain.cs',
    'src\Json.cs',
    'src\Settings.cs',
    'src\Generator.cs'
) | ForEach-Object { Join-Path $root $_ }

$common = @('/nologo', '/noconfig', '/nostdlib+', '/langversion:5', '/optimize+', '/utf8output', '/codepage:65001') + $refs

function Invoke-Csc {
    param([string[]]$Extra, [string]$Name)
    $argv = $common + $Extra
    & $csc $argv
    if ($LASTEXITCODE -ne 0) { throw "编译失败（退出码 $LASTEXITCODE）：$Name" }
    if (-not $Quiet) { Write-Host "已生成 $Name" -ForegroundColor Green }
}

if ($Target -eq 'All' -or $Target -eq 'Cli') {
    $extra = @('/target:exe', '/main:PeStubGen.CliProgram', ('/out:' + (Join-Path $outDir 'PeStubGen.Cli.exe'))) + $core + @((Join-Path $root 'src\Cli.cs'))
    Invoke-Csc -Extra $extra -Name 'out\PeStubGen.Cli.exe'
}

if ($Target -eq 'All' -or $Target -eq 'Gui') {
    $gui = $core + @((Join-Path $root 'src\App.cs'), (Join-Path $root 'src\MainWindow.cs'))
    $extra = @('/target:winexe', '/main:PeStubGen.App', ('/out:' + (Join-Path $outDir 'PeStubGen.exe'))) + $gui
    Invoke-Csc -Extra $extra -Name 'out\PeStubGen.exe'
}

if (-not $Quiet) { Write-Host '完成。' -ForegroundColor Green }
