# Test-only worker; no Azure entry point.
& (Join-Path $PSScriptRoot 'provider_test.ps1') -Worker
exit $LASTEXITCODE
