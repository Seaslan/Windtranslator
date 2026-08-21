[CmdletBinding()]
param(
    [ValidateSet("x64", "arm64")]
    [string]$Architecture = "x64",

    [string]$SourceDirectory = (Join-Path $env:TEMP "mozilla-translations-build"),

    [string]$CMakePath = "cmake",

    [string]$VcpkgRoot = $(
        if (-not [string]::IsNullOrWhiteSpace($env:VCPKG_ROOT)) {
            $env:VCPKG_ROOT
        }
        else {
            Join-Path $env:USERPROFILE "vcpkg"
        }
    )
)

$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$runtimeDirectory = Join-Path $repositoryRoot "Runtime\Bergamot\$Architecture"
$buildDirectory = Join-Path $SourceDirectory "build-$Architecture"
$inferenceDirectory = Join-Path $SourceDirectory "inference"
$vcpkgTriplet = if ($Architecture -eq "arm64") { "arm64-windows-static" } else { "x64-windows-static" }
$openBlasRoot = Join-Path $VcpkgRoot "installed\$vcpkgTriplet"
$openBlasLibraryDirectory = Join-Path $openBlasRoot "lib"
$openBlasIncludeDirectory = Join-Path $openBlasRoot "include\openblas"

function Get-CommandPath([string]$PathOrCommand) {
    if (Test-Path -LiteralPath $PathOrCommand -PathType Leaf) {
        return (Resolve-Path -LiteralPath $PathOrCommand).Path
    }

    $command = Get-Command $PathOrCommand -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        throw "未找到 $PathOrCommand。请安装 CMake 并确保其已加入 PATH，或通过 -CMakePath 指定 cmake.exe。"
    }

    return $command.Source
}

function Get-VsDevCmdPath {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path -LiteralPath $vswhere -PathType Leaf) {
        $installationPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($installationPath)) {
            $candidate = Join-Path $installationPath "Common7\Tools\VsDevCmd.bat"
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                return $candidate
            }
        }
    }

    throw '未找到 Visual C++ 桌面开发工具。请在 Visual Studio Installer 中安装“使用 C++ 的桌面开发”工作负载。'
}

function ConvertTo-MsysPath([string]$Path) {
    $fullPath = (Resolve-Path -LiteralPath $Path).Path
    $drive = $fullPath.Substring(0, 1).ToLowerInvariant()
    return "/$drive" + $fullPath.Substring(2).Replace("\", "/")
}

$cmake = Get-CommandPath $CMakePath
$git = Get-CommandPath "git"
$openBlasLibrary = Get-ChildItem -LiteralPath $openBlasLibraryDirectory -Filter "*openblas*.lib" -File -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($null -eq $openBlasLibrary -or -not (Test-Path -LiteralPath (Join-Path $openBlasIncludeDirectory "cblas.h") -PathType Leaf)) {
    throw "未找到静态 OpenBLAS（$vcpkgTriplet）。请先运行：$VcpkgRoot\vcpkg.exe install openblas:$vcpkgTriplet"
}
$gitRoot = Split-Path -Parent (Split-Path -Parent $git)
$gitBash = Join-Path $gitRoot "usr\bin\bash.exe"
if (-not (Test-Path -LiteralPath $gitBash -PathType Leaf)) {
    throw "未找到 Git for Windows 的 Bash 运行环境：$gitBash"
}

if (-not (Test-Path -LiteralPath (Join-Path $inferenceDirectory "CMakeLists.txt") -PathType Leaf)) {
    if (Test-Path -LiteralPath $SourceDirectory) {
        throw "源码目录不完整：$SourceDirectory"
    }

    Write-Host "正在获取 Mozilla Translations 源码..."
    & $git clone --recurse-submodules https://github.com/mozilla/translations.git $SourceDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "Mozilla Translations 源码下载失败。"
    }
}

Write-Host "正在同步 Bergamot 构建所需的子模块..."
$sourceDirectoryForBash = ConvertTo-MsysPath $SourceDirectory
$gitForBash = ConvertTo-MsysPath $git
$submoduleCommand = "cd '$sourceDirectoryForBash' && '$gitForBash' submodule update --init --recursive -- inference/3rd_party/ssplit-cpp inference/marian-fork/src/3rd_party/intgemm inference/marian-fork/src/3rd_party/sentencepiece"
& $gitBash --noprofile --norc -lc $submoduleCommand
if ($LASTEXITCODE -ne 0) {
    throw "Bergamot 构建子模块同步失败。请确认 Git 可以访问 GitHub 后重试。"
}

$intgemmCMakeLists = Join-Path $inferenceDirectory "marian-fork\src\3rd_party\intgemm\CMakeLists.txt"
$intgemmContents = [System.IO.File]::ReadAllText($intgemmCMakeLists)
$intgemmOriginalFlags = "add_compile_options(/W4 /WX)"
$intgemmPatchedFlags = "add_compile_options(/W4 /WX /wd4189)"
if ($intgemmContents.Contains($intgemmOriginalFlags)) {
    # Newer MSVC emits C4189 for this pinned upstream source; Marian enables /WX.
    $intgemmContents = $intgemmContents.Replace($intgemmOriginalFlags, $intgemmPatchedFlags)
    [System.IO.File]::WriteAllText($intgemmCMakeLists, $intgemmContents, [System.Text.UTF8Encoding]::new($false))
}
elseif (-not $intgemmContents.Contains($intgemmPatchedFlags)) {
    throw "无法应用 intgemm 的 MSVC 兼容性修补：$intgemmCMakeLists"
}

$pcreCMakeLists = Join-Path $inferenceDirectory "3rd_party\ssplit-cpp\cmake\FindPCRE2.cmake"
$pcreContents = [System.IO.File]::ReadAllText($pcreCMakeLists)
$pcreOriginalLibrary = 'set(PCRE2_LIBRARIES ${CMAKE_BINARY_DIR}/${CMAKE_INSTALL_LIBDIR}/${CMAKE_STATIC_LIBRARY_PREFIX}pcre2-8${CMAKE_STATIC_LIBRARY_SUFFIX})'
$pcrePatchedLibrary = 'set(PCRE2_LIBRARIES ${CMAKE_BINARY_DIR}/${CMAKE_INSTALL_LIBDIR}/${CMAKE_STATIC_LIBRARY_PREFIX}pcre2-8-static${CMAKE_STATIC_LIBRARY_SUFFIX})'
$pcreInternalGuard = 'if(SSPLIT_USE_INTERNAL_PCRE2)'
$pcrePatchedGuard = "if(SSPLIT_USE_INTERNAL_PCRE2)`n  add_compile_definitions(PCRE2_STATIC)"
if ($pcreContents.Contains($pcreOriginalLibrary)) {
    # PCRE2 names the static MSVC library pcre2-8-static.lib.
    $pcreContents = $pcreContents.Replace($pcreOriginalLibrary, $pcrePatchedLibrary)
}
elseif (-not $pcreContents.Contains($pcrePatchedLibrary)) {
    throw "无法应用 PCRE2 的 MSVC 库名兼容性修补：$pcreCMakeLists"
}
if ($pcreContents.Contains($pcreInternalGuard)) {
    $pcreContents = $pcreContents.Replace($pcreInternalGuard, $pcrePatchedGuard)
}
elseif (-not $pcreContents.Contains('add_compile_definitions(PCRE2_STATIC)')) {
    throw "无法应用 PCRE2 静态链接修补：$pcreCMakeLists"
}
[System.IO.File]::WriteAllText($pcreCMakeLists, $pcreContents, [System.Text.UTF8Encoding]::new($false))

$vsDevCmd = Get-VsDevCmdPath
$nativeArchitecture = if ($Architecture -eq "arm64") { "arm64" } else { "x64" }
$vsInstallationDirectory = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $vsDevCmd))
$msvcToolsDirectory = Get-ChildItem -LiteralPath (Join-Path $vsInstallationDirectory "VC\Tools\MSVC") -Directory |
    Sort-Object Name -Descending |
    Select-Object -First 1
if ($null -eq $msvcToolsDirectory) {
    throw "未找到 Visual C++ 工具目录。"
}

$nmake = Join-Path $msvcToolsDirectory.FullName "bin\Hostx64\x64\nmake.exe"
if (-not (Test-Path -LiteralPath $nmake -PathType Leaf)) {
    throw "未找到 NMake：$nmake"
}

# BLAS discovery is cached by CMake. Start this generated build directory over
# so a newly installed OpenBLAS library cannot be mistaken for the old failure.
if (Test-Path -LiteralPath $buildDirectory) {
    Remove-Item -LiteralPath $buildDirectory -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $buildDirectory | Out-Null
$toolchainFile = Join-Path $buildDirectory "windtranslator-toolchain.cmake"
$nmakeForCMake = $nmake.Replace("\", "/")
Set-Content -LiteralPath $toolchainFile -Encoding ASCII -Value @(
    "set(CMAKE_MAKE_PROGRAM `"$nmakeForCMake`" CACHE FILEPATH `"NMake path`" FORCE)",
    "set(CMAKE_POLICY_VERSION_MINIMUM `"3.5`" CACHE STRING `"CMake compatibility policy version`" FORCE)"
)

$configureArguments = @(
    "-S", $inferenceDirectory,
    "-B", $buildDirectory,
    "-G", "NMake Makefiles",
    "-DGIT_SUBMODULE=OFF",
    "-DCOMPILE_TESTS=OFF",
    "-DUSE_DOXYGEN=OFF",
    "-DCMAKE_POLICY_VERSION_MINIMUM=3.5",
    "-DSSPLIT_USE_INTERNAL_PCRE2=ON",
    "-DUSE_MKL=OFF",
    "-DUSE_NCCL=OFF",
    "-DBLA_VENDOR=OpenBLAS",
    "-DBLA_STATIC=ON",
    "-DCMAKE_LIBRARY_PATH=$($openBlasLibraryDirectory.Replace('\', '/'))",
    "-DCMAKE_INCLUDE_PATH=$($openBlasIncludeDirectory.Replace('\', '/'))",
    "-DCMAKE_BUILD_TYPE=Release",
    "-DCMAKE_MAKE_PROGRAM=$nmake",
    "-DCMAKE_TOOLCHAIN_FILE=$toolchainFile",
    "-DBUILD_ARCH=core-avx2"
)

$configureCommand = '"{0}" {1}' -f $cmake, (($configureArguments | ForEach-Object { '"{0}"' -f $_ }) -join ' ')
$buildCommand = '"{0}" --build "{1}" --target translator-cli' -f $cmake, $buildDirectory
$command = 'call "{0}" -arch={1} -host_arch=x64 && set "CMAKE_GENERATOR=NMake Makefiles" && {2} && {3}' -f $vsDevCmd, $nativeArchitecture, $configureCommand, $buildCommand

Write-Host "正在构建 Mozilla Translations $Architecture 运行库..."
& cmd.exe /d /s /c $command
if ($LASTEXITCODE -ne 0) {
    throw "Bergamot 运行库构建失败。请检查上方 CMake 输出；Doxygen 不是必需依赖。"
}

$translator = Get-ChildItem -Path $buildDirectory -Filter "translator-cli.exe" -File -Recurse | Select-Object -First 1
if ($null -eq $translator) {
    throw "构建已完成，但未找到 translator-cli.exe。"
}

New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null
Copy-Item -LiteralPath $translator.FullName -Destination $runtimeDirectory -Force
Get-ChildItem -LiteralPath $translator.DirectoryName -Filter "*.dll" -File | Copy-Item -Destination $runtimeDirectory -Force

Write-Host "已写入运行库：$runtimeDirectory"
