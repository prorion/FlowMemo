# FlowMemo 설치파일을 만든다. 결과: installer\output\FlowMemo-Setup-<버전>.exe
# 필요한 것: .NET 10 SDK, Inno Setup 6 (winget install JRSoftware.InnoSetup)
# .NET 런타임을 함께 넣으므로 설치하는 PC에는 .NET이 없어도 된다.

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
$publishDir = Join-Path $PSScriptRoot 'publish'
$outputDir = Join-Path $PSScriptRoot 'output'

$iscc = @(
    (Get-Command iscc -ErrorAction SilentlyContinue).Source
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
if (-not $iscc) {
    throw 'Inno Setup 6을 찾을 수 없습니다. winget install JRSoftware.InnoSetup 으로 설치하세요.'
}

[xml]$props = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw
$version = @($props.Project.PropertyGroup.Version | Where-Object { $_ })[0]
if (-not $version) {
    throw 'Directory.Build.props에서 버전을 읽지 못했습니다.'
}

# 이전 결과물은 남기지 않고 최신만 둔다 (옛 버전 설치파일이 쌓이지 않게).
# 저장소 루트의 publish는 설치파일을 쓰기 전의 게시 폴더다.
foreach ($previous in $publishDir, $outputDir, (Join-Path $repoRoot 'publish')) {
    if (Test-Path -LiteralPath $previous) {
        Remove-Item -LiteralPath $previous -Recurse -Force
    }
}

Write-Host "FlowMemo $version 게시 중..."
dotnet publish (Join-Path $repoRoot 'FlowMemo.WpfUi') `
    -c Release -r win-x64 --self-contained true -o $publishDir `
    -p:PublishReadyToRun=true -p:SatelliteResourceLanguages=ko -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 실패 (종료 코드 $LASTEXITCODE)"
}

Write-Host '설치파일 만드는 중...'
& $iscc /Qp "/DAppVersion=$version" "/DPublishDir=$publishDir" "/O$outputDir" (Join-Path $PSScriptRoot 'FlowMemo.iss')
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup 실패 (종료 코드 $LASTEXITCODE)"
}

$setup = Get-Item -LiteralPath (Join-Path $outputDir "FlowMemo-Setup-$version.exe")
Write-Host ("완료: {0} ({1:N1} MB)" -f $setup.FullName, ($setup.Length / 1MB))
