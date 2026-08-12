param(
  [Parameter(Mandatory=$true)]
  [string]$ApiBaseUrl,

  [string]$DemoPassword = $env:FIELDVISIT_DEMO_PASSWORD,

  [int]$JobTimeoutSeconds = 90
)

$ErrorActionPreference = "Stop"
$ApiBaseUrl = $ApiBaseUrl.TrimEnd('/')

if ([string]::IsNullOrWhiteSpace($DemoPassword)) {
    throw "DemoPassword 未提供。請使用 -DemoPassword 或設定 FIELDVISIT_DEMO_PASSWORD 環境變數。"
}

function Login([string]$account) {
    $body = @{
        account  = $account
        password = $DemoPassword
    } | ConvertTo-Json

    Invoke-RestMethod `
        -Method Post `
        -Uri "$ApiBaseUrl/auth/demo-login" `
        -ContentType "application/json" `
        -Body $body
}

function H([string]$token) {
    @{
        Authorization = "Bearer $token"
    }
}

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "Field Visit Mileage System v1.6.0 UAT Smoke Test" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

# ------------------------------------------------------------
# 1. Health
# ------------------------------------------------------------
Write-Host "1/10 API + DB health"

$root = $ApiBaseUrl -replace '/api/v1$',''

$health = Invoke-RestMethod "$root/health"
$dbHealth = Invoke-RestMethod "$root/health/ready"

if ($health.status -ne 'ok' -or $dbHealth.status -ne 'ready') {
    throw "Health check failed."
}

# ------------------------------------------------------------
# 2. Visitor login
# ------------------------------------------------------------
Write-Host "2/10 Visitor login"

$visitor = Login 'visitor01'
$visitorHeaders = H $visitor.accessToken

# ------------------------------------------------------------
# 3. Master data
# ------------------------------------------------------------
Write-Host "3/10 Load master data"

$locations = Invoke-RestMethod `
    -Uri "$ApiBaseUrl/locations" `
    -Headers $visitorHeaders

if ($locations.Count -lt 2) {
    throw "Smoke Test 至少需要 2 個可用地點。"
}

# ------------------------------------------------------------
# 4. Create trip
# ------------------------------------------------------------
Write-Host "4/10 Create trip draft"

$date = (Get-Date).AddDays(30).ToString('yyyy-MM-dd')

$tripBody = @{
    visitDate             = $date
    startTime             = '18:10'
    endTime               = '19:10'
    claimedDistanceKm     = 12.3
    purpose               = 'v1.6.0 Smoke Test'
    notes                 = 'Automated UAT Smoke Test'
    timeOverlapConfirmed  = $false
    stops = @(
        @{
            locationId   = $locations[0].locationId
            projectId    = $null
            visitTypeId  = $null
            sourceType   = 'Master'
            locationName = $locations[0].locationName
            address      = $locations[0].address
        },
        @{
            locationId   = $locations[1].locationId
            projectId    = $null
            visitTypeId  = $null
            sourceType   = 'Master'
            locationName = $locations[1].locationName
            address      = $locations[1].address
        }
    )
} | ConvertTo-Json -Depth 8

$trip = Invoke-RestMethod `
    -Method Post `
    -Uri "$ApiBaseUrl/trips" `
    -Headers $visitorHeaders `
    -ContentType 'application/json' `
    -Body $tripBody

# ------------------------------------------------------------
# 5. Submit
# ------------------------------------------------------------
Write-Host "5/10 Submit trip"

$submitHeaders = @{
    Authorization = "Bearer $($visitor.accessToken)"
    'If-Match'    = $trip.rowVersion
}

$submitBody = @{
    confirmTimeOverlap = $false
} | ConvertTo-Json

$submitted = Invoke-RestMethod `
    -Method Post `
    -Uri "$ApiBaseUrl/trips/$($trip.visitTripId)/submit" `
    -Headers $submitHeaders `
    -ContentType 'application/json' `
    -Body $submitBody

# ------------------------------------------------------------
# 6. Leader login + enqueue mileage background job
# ------------------------------------------------------------
Write-Host "6/10 Leader login + enqueue mileage job"

$leader = Login 'leader01'
$leaderHeaders = H $leader.accessToken

$jobBody = @{
    mode            = 'Selected'
    startDate       = $null
    endDate         = $null
    selectedTripIds = @($trip.visitTripId)
} | ConvertTo-Json

$job = Invoke-RestMethod `
    -Method Post `
    -Uri "$ApiBaseUrl/jobs/mileage" `
    -Headers $leaderHeaders `
    -ContentType 'application/json' `
    -Body $jobBody

if (-not $job.backgroundJobId) {
    throw "建立 Mileage Background Job 失敗：沒有 BackgroundJobId。"
}

Write-Host "  JobId=$($job.backgroundJobId), initial=$($job.status)"

# ------------------------------------------------------------
# 7. Poll background job
# ------------------------------------------------------------
Write-Host "7/10 Wait for mileage job"

$deadline = (Get-Date).AddSeconds($JobTimeoutSeconds)
$jobState = $job

do {
    if ($jobState.status -in @('Succeeded','PartiallySucceeded','Failed')) {
        break
    }

    Start-Sleep -Seconds 2

    $jobState = Invoke-RestMethod `
        -Method Get `
        -Uri "$ApiBaseUrl/jobs/$($job.backgroundJobId)" `
        -Headers $leaderHeaders

    Write-Host "  Job status=$($jobState.status), success=$($jobState.successCount), failed=$($jobState.failedCount)"

} while ((Get-Date) -lt $deadline)

if ($jobState.status -notin @('Succeeded','PartiallySucceeded')) {
    throw "Mileage Job 未成功完成。Status=$($jobState.status), Error=$($jobState.errorMessage)"
}

if ($jobState.successCount -lt 1) {
    throw "Mileage Job 沒有成功處理任何行程。"
}

# ------------------------------------------------------------
# 8. Leader review queue + approve
# ------------------------------------------------------------
Write-Host "8/10 Leader review + approve"

$queue = Invoke-RestMethod `
    -Uri "$ApiBaseUrl/leader/review-queue" `
    -Headers $leaderHeaders

$target = $queue |
    Where-Object { $_.visitTripId -eq $trip.visitTripId } |
    Select-Object -First 1

if (-not $target) {
    throw "Mileage Job 完成後，行程未出現在 Leader Review Queue。"
}

if ($target.status -ne 'PendingApproval') {
    throw "Mileage Job 後狀態錯誤：$($target.status)"
}

if ($null -eq $target.systemDistanceKm) {
    throw "Mileage Job 完成後沒有 systemDistanceKm。"
}

$approveBody = @{
    approvedDistanceKm = $target.systemDistanceKm
    rowVersion         = $target.rowVersion
    comments           = 'v1.6.0 automated smoke test'
} | ConvertTo-Json

$approved = Invoke-RestMethod `
    -Method Post `
    -Uri "$ApiBaseUrl/trips/$($target.visitTripId)/approve" `
    -Headers $leaderHeaders `
    -ContentType 'application/json' `
    -Body $approveBody

if ($approved.status -ne 'Approved') {
    throw "核准失敗。Status=$($approved.status)"
}

# ------------------------------------------------------------
# 9. Unified Query
# ------------------------------------------------------------
Write-Host "9/10 Unified Query validation"

$query = Invoke-RestMethod `
    -Uri "$ApiBaseUrl/query/trips?startDate=$date&endDate=$date&page=1&pageSize=100" `
    -Headers $visitorHeaders

$final = $query.items |
    Where-Object { $_.visitTripId -eq $trip.visitTripId } |
    Select-Object -First 1

if (-not $final) {
    throw "Unified Query 找不到剛完成的行程。"
}

if ($final.status -ne 'Approved') {
    throw "Unified Query 行程狀態不是 Approved：$($final.status)"
}

if (-not $final.isSnapshot -or $final.snapshotVersion -lt 1) {
    throw "核准後沒有建立有效 Snapshot。"
}

# ------------------------------------------------------------
# 10. Final validation
# ------------------------------------------------------------
Write-Host "10/10 Final validation"

if ($null -eq $final.systemDistanceKm) {
    throw "Unified Query 缺少 systemDistanceKm。"
}

if ($null -eq $final.approvedDistanceKm) {
    throw "Unified Query 缺少 approvedDistanceKm。"
}

Write-Host ""
Write-Host "PASS - v1.6.0 multi-role UAT smoke flow completed." -ForegroundColor Green
Write-Host "Trip=$($final.tripNo)"
Write-Host "Status=$($final.statusName)"
Write-Host "SnapshotVersion=$($final.snapshotVersion)"
Write-Host "SystemKm=$($final.systemDistanceKm)"
Write-Host "ApprovedKm=$($final.approvedDistanceKm)"
Write-Host "Subsidy=$($final.subsidyAmount)"
