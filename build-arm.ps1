# HyperViewer ARM / ARM64 打包 + 签名脚本
#
# 背景: VS2022 17.x 在 TargetPlatformVersion>=22621 时会拒绝 ARM 配置的校验 (_ValidateConfiguration),
# 但 .NET Native 编译在此之前已完成。本脚本利用该产物, 手动组装布局 + makeappx 打包 + signtool 签名。
#
# 用法:
#   .\build-arm.ps1                                  # 打包 arm + arm64, 无证书时自动生成自签名测试证书
#   .\build-arm.ps1 -Arch arm                        # 只打 arm
#   .\build-arm.ps1 -CertPath my.pfx -CertPassword xxx
#   .\build-arm.ps1 -NoTimestamp                     # 跳过时间戳服务器 (离线环境)
#
# 产物: dist\HyperViewer_<版本>_arm.appx / _arm64.appx (已签名)
# 证书: dist\HyperViewerTestCert.pfx (自签名, 密码见 dist\HyperViewerTestCert.pwd)
#        部署到目标设备前, 需先把该证书安装到设备的"受信任的根证书颁发机构"。
param(
    [string]$Arch = "arm,arm64",
    [string]$CertPath = "",
    [string]$CertPassword = "",
    [switch]$NoTimestamp
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root "dist"

$msbuild = "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
if (-not (Test-Path $msbuild)) {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" 2>$null
        if ($found) { $msbuild = $found | Select-Object -First 1 }
    }
}
if (-not (Test-Path $msbuild)) { Write-Error "未找到 MSBuild.exe"; exit 1 }

$kitsRoot = "C:\Program Files (x86)\Windows Kits\10\bin"
$kits = Join-Path $kitsRoot "10.0.26100.0\x86"
$makeappx = Join-Path $kits "makeappx.exe"
$signtool = Join-Path $kits "signtool.exe"
foreach ($t in @($makeappx, $signtool)) {
    if (-not (Test-Path $t)) { Write-Error "未找到 $t (请确认 Windows SDK 10.0.26100 已安装)"; exit 1 }
}

# ---- 版本号 / 发布者 (AppX 签名要求证书 Subject 与 Publisher 一致) ----
[xml]$manifest = Get-Content (Join-Path $root "Package.appxmanifest") -Encoding UTF8
$ver = $manifest.Package.Identity.Version
$publisher = $manifest.Package.Identity.Publisher

# ---- 证书: 指定 pfx 优先, 否则复用/生成自签名测试证书 ----
if (-not $CertPath) {
    $pfx = Join-Path $dist "HyperViewerTestCert.pfx"
    $pwdFile = Join-Path $dist "HyperViewerTestCert.pwd"
    if ((Test-Path $pfx) -and (Test-Path $pwdFile)) {
        $CertPath = $pfx
        $CertPassword = (Get-Content $pwdFile -Raw).Trim()
        Write-Host "[证书] 复用已有测试证书: $pfx"
    } else {
        if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }
        Write-Host "[证书] 生成自签名代码签名证书 (Subject 必须等于 manifest Publisher: $publisher)..."
        $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $publisher `
            -CertStoreLocation Cert:\CurrentUser\My -KeyExportPolicy Exportable -NotAfter (Get-Date).AddYears(3)
        $pwd = -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 16 | ForEach-Object { [char]$_ })
        Export-PfxCertificate -Cert $cert -FilePath $pfx -Password (ConvertTo-SecureString $pwd -AsPlainText -Force) | Out-Null
        Set-Content -Path $pwdFile -Value $pwd -NoNewline -Encoding ASCII
        $CertPath = $pfx
        $CertPassword = $pwd
        Write-Host "[证书] 已生成: $pfx (密码: $pwd)"
    }
    Write-Host "       部署到设备前, 需先将该证书安装到设备的'受信任的根证书颁发机构'。"
}

# ---- 逐架构: 编译 -> 组装 -> 打包 -> 签名 ----
$results = @()
foreach ($a in ($Arch -split ',')) {
    $a = $a.Trim()
    if ($a -notin @("arm", "arm64")) { Write-Error "不支持的架构: $a (仅支持 arm / arm64)"; exit 1 }
    $platform = if ($a -eq "arm") { "ARM" } else { "ARM64" }
    $binDir = Join-Path $root "bin\$platform\Release"
    $stage = Join-Path $dist "_staging\$a"
    $pkg = Join-Path $dist "HyperViewer_${ver}_$a.appx"

    Write-Host ""
    Write-Host "==> [1/3] 编译 $platform (Release, .NET Native) ..."
    & $msbuild (Join-Path $root "HyperViewer.csproj") /t:Build /p:Configuration=Release /p:Platform=$platform /v:m /nologo 2>&1 | Out-Null
    # _ValidateConfiguration 的 ARM/ARM64 校验失败是预期的 (TargetPlatformVersion=26100 > 22621),
    # 但 .NET Native 编译已完成。以 exe 产物判断真实成败。
    if (-not (Test-Path (Join-Path $binDir "HyperViewer.exe"))) {
        Write-Error "编译失败: 未生成 $binDir\HyperViewer.exe"; exit 1
    }
    Write-Host "      编译完成 (校验报错可忽略)。"

    Write-Host "==> [2/3] 组装布局 + makeappx 打包 ..."
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Path $stage | Out-Null
    Copy-Item (Join-Path $binDir "*") $stage -Recurse -Force
    Copy-Item (Join-Path $root "Assets") (Join-Path $stage "Assets") -Recurse -Force

    # 规范化 manifest: 优先取 x86 Release (VS 生成, 已替换 exe 名), 缺失则用 x86 Debug
    $srcManifest = Join-Path $root "bin\x86\Release\AppxManifest.xml"
    if (-not (Test-Path $srcManifest)) { $srcManifest = Join-Path $root "bin\x86\Debug\AppxManifest.xml" }
    if (-not (Test-Path $srcManifest)) { Write-Error "未找到规范化 AppxManifest.xml (请先构建一次 x86)"; exit 1 }
    $text = [System.IO.File]::ReadAllText($srcManifest)
    $text = $text -replace 'ProcessorArchitecture="x86"', "ProcessorArchitecture=`"$a`""
    $text = $text -replace ' xmlns:build="http://schemas\.microsoft\.com/developer/appx/2015/build"', ''
    $text = $text -replace ' build', ''
    $text = $text -replace '<build:Metadata>[\s\S]*?</build:Metadata>', ''
    [System.IO.File]::WriteAllText((Join-Path $stage "AppxManifest.xml"), $text, (New-Object System.Text.UTF8Encoding($true)))

    # resources.pri: 优先本架构产物, 缺失则用 x86 的 (pri 与平台无关)
    if (-not (Test-Path (Join-Path $stage "resources.pri"))) {
        $srcPri = Join-Path $root "bin\x86\Release\resources.pri"
        if (-not (Test-Path $srcPri)) { $srcPri = Join-Path $root "bin\x86\Debug\resources.pri" }
        Copy-Item $srcPri (Join-Path $stage "resources.pri") -Force
    }
    Remove-Item (Join-Path $stage "HyperViewer.pdb") -Force -ErrorAction SilentlyContinue

    & $makeappx pack /d $stage /p $pkg /o /l | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Error "makeappx 打包失败 ($LASTEXITCODE)"; exit 1 }
    Write-Host "      打包完成: $pkg"

    Write-Host "==> [3/3] signtool 签名 ..."
    $signed = $false
    if (-not $NoTimestamp) {
        & $signtool sign /fd SHA256 /f $CertPath /p $CertPassword /tr "http://timestamp.digicert.com" /td SHA256 $pkg
        if ($LASTEXITCODE -eq 0) { $signed = $true }
    }
    if (-not $signed) {
        & $signtool sign /fd SHA256 /f $CertPath /p $CertPassword $pkg
        if ($LASTEXITCODE -ne 0) { Write-Error "签名失败 ($LASTEXITCODE)"; exit 1 }
        if (-not $NoTimestamp) { Write-Host "      时间戳服务器不可达, 已改用无时间戳签名。" }
    }
    Write-Host "      签名完成。"
    $results += $pkg
}

# ---- 汇总 + 部署提示 ----
Write-Host ""
Write-Host "=========================================="
Write-Host "完成! 产物:"
foreach ($p in $results) {
    $item = Get-Item $p
    Write-Host ("  {0}  ({1:N1} MB)" -f $item.FullName, ($item.Length / 1MB))
}
Write-Host "=========================================="
Write-Host "部署到目标设备 (桌面):"
Write-Host "  1. 安装证书 (一次):  Import-PfxCertificate -FilePath <HyperViewerTestCert.pfx> -CertStoreLocation Cert:\LocalMachine\Root"
Write-Host "  2. 安装应用:          Add-AppxPackage -Path <HyperViewer_..._arm.appx>"
Write-Host "(若使用自己的证书, 请安装对应的根证书; 商店提交需使用商店发布证书。)"

exit 0