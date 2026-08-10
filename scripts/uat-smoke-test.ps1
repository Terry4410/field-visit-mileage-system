param(
  [Parameter(Mandatory=$true)][string]$ApiBaseUrl,
  [string]$DemoPassword="123456"
)
$ErrorActionPreference="Stop";$ApiBaseUrl=$ApiBaseUrl.TrimEnd('/')
function Login($account){$b=@{account=$account;password=$DemoPassword}|ConvertTo-Json;Invoke-RestMethod -Method Post -Uri "$ApiBaseUrl/auth/demo-login" -ContentType "application/json" -Body $b}
function H($token){@{Authorization="Bearer $token"}}
Write-Host "1/9 API + DB health"
$root=$ApiBaseUrl -replace '/api/v1$','';$h=Invoke-RestMethod "$root/health";$db=Invoke-RestMethod "$root/health/db";if($h.status-ne'ok' -or $db.status-ne'ok'){throw "Health failed"}
Write-Host "2/9 Visitor login";$v=Login 'visitor01';$vh=H $v.accessToken
Write-Host "3/9 Master data";$loc=Invoke-RestMethod -Uri "$ApiBaseUrl/locations" -Headers $vh;if($loc.Count-lt2){throw "Need >=2 locations"}
$date=(Get-Date).AddDays(30).ToString('yyyy-MM-dd')
$body=@{visitDate=$date;startTime='18:10';endTime='19:10';claimedDistanceKm=12.3;purpose='Smoke Test';notes='Automated';timeOverlapConfirmed=$false;stops=@(@{locationId=$loc[0].locationId;projectId=$null;visitTypeId=$null;sourceType='Master';locationName=$loc[0].locationName;address=$loc[0].address},@{locationId=$loc[1].locationId;projectId=$null;visitTypeId=$null;sourceType='Master';locationName=$loc[1].locationName;address=$loc[1].address})}|ConvertTo-Json -Depth 8
Write-Host "4/9 Create draft";$t=Invoke-RestMethod -Method Post -Uri "$ApiBaseUrl/trips" -Headers $vh -ContentType 'application/json' -Body $body
Write-Host "5/9 Submit";$sh=@{Authorization="Bearer $($v.accessToken)";'If-Match'=$t.rowVersion};$sb=@{confirmTimeOverlap=$false}|ConvertTo-Json;$sent=Invoke-RestMethod -Method Post -Uri "$ApiBaseUrl/trips/$($t.visitTripId)/submit" -Headers $sh -ContentType 'application/json' -Body $sb
Write-Host "6/9 Leader login + batch mileage";$l=Login 'leader01';$lh=H $l.accessToken;$jb=@{mode='AllPending';startDate=$null;endDate=$null;selectedTripIds=$null}|ConvertTo-Json;$job=Invoke-RestMethod -Method Post -Uri "$ApiBaseUrl/mileage-jobs" -Headers $lh -ContentType 'application/json' -Body $jb
Write-Host "7/9 Review queue";$q=Invoke-RestMethod -Uri "$ApiBaseUrl/leader/review-queue" -Headers $lh;$target=$q|Where-Object{$_.visitTripId-eq$t.visitTripId}|Select-Object -First 1;if(-not$target){throw "Trip not in leader queue"};if($target.status-ne'PendingApproval'){throw "Unexpected status $($target.status)"}
Write-Host "8/9 Approve";$ab=@{approvedDistanceKm=$target.systemDistanceKm;rowVersion=$target.rowVersion;comments='Smoke test'}|ConvertTo-Json;$approved=Invoke-RestMethod -Method Post -Uri "$ApiBaseUrl/trips/$($target.visitTripId)/approve" -Headers $lh -ContentType 'application/json' -Body $ab;if($approved.status-ne'Approved' -or $null-eq$approved.approvedAmount){throw "Approve/subsidy failed"}
Write-Host "9/9 Visitor history";$hist=Invoke-RestMethod -Uri "$ApiBaseUrl/trips/history?startDate=$date&endDate=$date" -Headers $vh;$final=$hist|Where-Object{$_.visitTripId-eq$t.visitTripId}|Select-Object -First 1;if(-not$final){throw "History not found"}
Write-Host "PASS - Existing Schema 1.5.0 multi-role UAT flow completed." -ForegroundColor Green
Write-Host "Trip=$($final.tripNo), status=$($final.statusName), systemKm=$($final.systemDistanceKm), approvedAmount=$($final.approvedAmount)"
