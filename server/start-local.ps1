<#
.SYNOPSIS
    로컬 Worker(wrangler dev)를 띄워 FlowMemo 동기화 테스트를 준비한다.
.DESCRIPTION
    임시 비밀값으로 .dev.vars를 만들고 로컬 D1을 초기화한 뒤 8799 포트에서 서버를 실행한다.
    실행한 프로세스는 호출한 쪽에서 끝낸다. 실제 Cloudflare 계정은 쓰지 않는다.
.OUTPUTS
    wrangler dev 프로세스
#>
param(
    [string]$SetupCode = 'local-setup-code-for-tests-only'
)

$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if (-not (Test-Path 'node_modules\wrangler')) {
        & npm install --no-audit --no-fund 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'npm install 실패' }
    }

    if (Test-Path '.wrangler') { Remove-Item -Recurse -Force '.wrangler' }

    [IO.File]::WriteAllText((Join-Path $PSScriptRoot '.dev.vars'), "SERVER_SECRET=local-only-secret-not-used-in-production`nSETUP_CODE=$SetupCode`n")

    & npx wrangler d1 migrations apply flowmemo-sync --local 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw '로컬 D1 초기화 실패' }

    Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', 'npx wrangler dev --local --port 8799 > .wrangler\dev.log 2>&1' -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -PassThru
}
finally {
    Pop-Location
}
