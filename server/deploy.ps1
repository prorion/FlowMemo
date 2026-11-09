<#
.SYNOPSIS
    FlowMemo 동기화 서버를 내 Cloudflare 계정에 배포한다. (GitHub Actions는 쓰지 않는다)

.DESCRIPTION
    먼저 `npx wrangler login`으로 로그인해 둔다. 여러 번 실행해도 안전하다.
    - D1 데이터베이스가 없으면 만들고 스키마를 맞춘다.
    - Worker를 배포한다.
    - 비밀값은 처음 한 번만 만들어 Cloudflare에 저장한다. 다시 배포해도 바뀌지 않는다.
    - 첫 PC용 등록 코드는 화면에 출력하지 않고 .setup-code.txt에 저장한다 (git에 올라가지 않는다).
#>
$ErrorActionPreference = 'Stop'

$name = 'flowmemo-sync'
$placeholderId = '00000000-0000-0000-0000-000000000000'
$deployConfig = 'wrangler.deploy.toml'
$setupCodeFile = '.setup-code.txt'

function Invoke-Wrangler {
    param([Parameter(ValueFromRemainingArguments)][string[]]$Arguments)
    $output = & npx wrangler @Arguments 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) {
        $output | Write-Host
        throw "wrangler $($Arguments -join ' ') 실패"
    }
    return $output
}

function New-RandomSecret {
    $bytes = [byte[]]::new(32)
    [Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    return [Convert]::ToBase64String($bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=')
}

function Set-WorkerSecret {
    param([string]$Key, [string]$Value)
    $Value | & npx wrangler secret put $Key --config $deployConfig 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "비밀값 $Key 저장 실패" }
}

Push-Location $PSScriptRoot
try {
    if (-not (Test-Path 'node_modules\wrangler')) {
        & npm install --no-audit --no-fund 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'npm install 실패' }
    }

    Invoke-Wrangler whoami | Out-Null

    # D1 데이터베이스
    $database = (Invoke-Wrangler d1 list --json | Out-String | ConvertFrom-Json) | Where-Object { $_.name -eq $name }
    if (-not $database) {
        Write-Host "D1 데이터베이스 $name 만드는 중..."
        Invoke-Wrangler d1 create $name | Out-Null
        $database = (Invoke-Wrangler d1 list --json | Out-String | ConvertFrom-Json) | Where-Object { $_.name -eq $name }
    }
    if (-not $database) { throw 'D1 데이터베이스를 찾지 못했습니다.' }

    $config = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'wrangler.toml')).Replace($placeholderId, $database.uuid)
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot $deployConfig), $config)

    Write-Host '스키마 적용 중...'
    Invoke-Wrangler d1 migrations apply $name --remote --config $deployConfig | Out-Null

    Write-Host 'Worker 배포 중...'
    $deployOutput = Invoke-Wrangler deploy --config $deployConfig | Out-String
    $match = [regex]::Match($deployOutput, 'https://[a-z0-9-]+\.[a-z0-9-]+\.workers\.dev')
    if (-not $match.Success) {
        $deployOutput | Write-Host
        throw '배포 주소를 찾지 못했습니다.'
    }
    $url = $match.Value

    # 비밀값: 처음 한 번만
    $secrets = (Invoke-Wrangler secret list --config $deployConfig | Out-String | ConvertFrom-Json).name
    if ($secrets -notcontains 'SERVER_SECRET') {
        Set-WorkerSecret 'SERVER_SECRET' (New-RandomSecret)
    }
    if ($secrets -notcontains 'SETUP_CODE') {
        $setupCode = New-RandomSecret
        Set-WorkerSecret 'SETUP_CODE' $setupCode

        $json = @{ K = 'Setup'; U = $url; S = $setupCode; V = $null } | ConvertTo-Json -Compress
        $encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json)).Replace('+', '-').Replace('/', '_').TrimEnd('=')
        [IO.File]::WriteAllText((Join-Path $PSScriptRoot $setupCodeFile), "FMSYNC1.$encoded")
        Write-Host "첫 PC용 등록 코드를 저장했습니다: server\$setupCodeFile"
    }

    Write-Host "배포 완료: $url"
}
finally {
    Pop-Location
}
