<#
.SYNOPSIS
    FlowMemo 회귀 테스트를 로컬에서 실행한다. GitHub Actions 같은 CI는 쓰지 않는다.

.DESCRIPTION
    - logic : FlowMemo.dll의 실제 편집 컨트롤과 도구 메뉴를 검사한다.
    - data  : 임시 DB로 생성·마이그레이션·저장소·백업을 검사한다.
    - ui    : 앱 복사본을 띄워 창·메뉴·관리 창·설정 창을 검사한다.
              화면에 FlowMemo 창이 잠깐씩 뜬다. 마우스·키보드 입력은 보내지 않는다.
    - sync  : 로컬 Worker(wrangler dev)에 가상 PC 여러 대를 연결해 동기화를 검사한다.
              Node.js가 필요하다. 실제 Cloudflare 계정은 쓰지 않는다.

    모두 Debug 빌드로 실행한다. Debug 빌드는 단일 인스턴스 이름이 달라서,
    평소 쓰는 FlowMemo(Release)가 켜져 있어도 서로 간섭하지 않는다.
    테스트 앱은 임시 폴더에서 포터블 모드로 실행되므로 실제 메모 DB는 건드리지 않는다.

.PARAMETER Suite
    all(기본) | logic | data | sync | ui

.EXAMPLE
    .\tests\run.ps1
    .\tests\run.ps1 -Suite data
#>
param(
    [ValidateSet('all', 'logic', 'data', 'sync', 'ui')]
    [string]$Suite = 'all'
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$repo = Split-Path $PSScriptRoot -Parent
$work = Join-Path ([IO.Path]::GetTempPath()) 'FlowMemo-tests'
$appDir = Join-Path $work 'app'
$outDir = Join-Path $work 'out'
$dataDir = Join-Path $work 'data'
New-Item -ItemType Directory -Force $work, $outDir | Out-Null

Write-Host "빌드 (Debug)..."
$buildLog = dotnet build (Join-Path $PSScriptRoot 'FlowMemo.Tests') -c Debug -nologo -v quiet 2>&1
if ($LASTEXITCODE -ne 0) { $buildLog | Write-Host; throw '빌드 실패' }

$testExe = Join-Path $PSScriptRoot 'FlowMemo.Tests\bin\Debug\net10.0-windows\FlowMemo.Tests.exe'

if ($Suite -in 'all', 'ui') {
    # 앱 복사본: 기존 테스트 DB는 지우고 포터블 모드로 둔다.
    $appBin = Join-Path $repo 'FlowMemo.WpfUi\bin\Debug\net10.0-windows'
    robocopy $appBin $appDir /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "앱 복사 실패 (robocopy $LASTEXITCODE)" }
    Set-Content -LiteralPath (Join-Path $appDir 'portable.txt') -Value 'FlowMemo 테스트용 포터블 모드'
}

$runs = [Collections.Generic.List[string[]]]::new()
if ($Suite -in 'all', 'logic') { $runs.Add(@('logic', $outDir)) }
if ($Suite -in 'all', 'data') { $runs.Add(@('data', $dataDir)) }
if ($Suite -in 'all', 'ui') { $runs.Add(@('ui', $appDir, $outDir)) }

$failed = 0
foreach ($run in $runs) {
    & $testExe $run
    if ($LASTEXITCODE -ne 0) { $failed += $LASTEXITCODE }
}

if ($Suite -in 'all', 'sync') {
    # 로컬 Worker를 띄운다. 끝나면 이 서버 폴더에서 실행한 프로세스만 정리한다.
    $serverDir = Join-Path $repo 'server'
    $setupCode = 'local-setup-code-for-tests-only'
    $syncUrl = 'http://127.0.0.1:8799'
    try {
        & (Join-Path $serverDir 'start-local.ps1') -SetupCode $setupCode | Out-Null
        $ready = $false
        foreach ($i in 1..80) {
            Start-Sleep -Milliseconds 500
            try {
                Invoke-WebRequest -UseBasicParsing "$syncUrl/" -TimeoutSec 2 | Out-Null
                $ready = $true
                break
            }
            catch { }
        }
        if (-not $ready) { throw '로컬 Worker가 시작되지 않았습니다.' }

        & $testExe sync (Join-Path $work 'sync') $syncUrl $setupCode
        if ($LASTEXITCODE -ne 0) { $failed += $LASTEXITCODE }
    }
    finally {
        Get-CimInstance Win32_Process |
            Where-Object { $_.CommandLine -like "*wrangler*dev*8799*" -or $_.Name -eq 'workerd.exe' -and $_.CommandLine -like "*wrangler*" } |
            ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    }
}

Write-Host ''
Write-Host "캡처·로그: $outDir"
if ($failed -gt 0) {
    Write-Host "실패: $failed" -ForegroundColor Red
    exit 1
}
Write-Host '모두 통과' -ForegroundColor Green
