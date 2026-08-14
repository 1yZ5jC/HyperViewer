# 构建 HyperViewer (UWP)。
# 先 Clean 再 Build: 避免 MakeAppx 增量打包损坏 (obj\...\PackageLayout\entrypoint 缺失导致 0x80070003)。
param(
    [string]$Configuration = "Debug",
    [string]$Platform = "x86"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$msbuild = "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
if (-not (Test-Path $msbuild)) {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" 2>$null
        if ($found) { $msbuild = $found | Select-Object -First 1 }
    }
}
if (-not (Test-Path $msbuild)) {
    Write-Error "未找到 MSBuild.exe"
    exit 1
}

Write-Host "==> Clean ($Configuration|$Platform)"
& $msbuild "$root\HyperViewer.csproj" /t:Clean /p:Configuration=$Configuration /p:Platform=$Platform /v:m /nologo
if ($LASTEXITCODE -ne 0) { Write-Error "Clean 失败 ($LASTEXITCODE)"; exit $LASTEXITCODE }

Write-Host "==> Build ($Configuration|$Platform)"
& $msbuild "$root\HyperViewer.csproj" /t:Build /p:Configuration=$Configuration /p:Platform=$Platform /v:m /nologo
if ($LASTEXITCODE -ne 0) { Write-Error "Build 失败 ($LASTEXITCODE)"; exit $LASTEXITCODE }

Write-Host "==> OK"
exit 0