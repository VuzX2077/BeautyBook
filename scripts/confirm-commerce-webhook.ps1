# Run from a trusted shell with the same commerce environment variables as the backend.
$ErrorActionPreference = 'Stop'
$taskClientId = [Environment]::GetEnvironmentVariable('CommercePayOS__ClientId')
$taskApiKey = [Environment]::GetEnvironmentVariable('CommercePayOS__ApiKey')
$taskOrigin = [Environment]::GetEnvironmentVariable('MuaWeb__ApiOrigin')
if ([string]::IsNullOrWhiteSpace($taskOrigin)) { $taskOrigin = 'https://api.bbookmakeup.com' }
$taskUri = [Uri]$taskOrigin
if ($taskUri.Scheme -ne 'https' -or -not $taskUri.IsDefaultPort -or $taskUri.UserInfo -or $taskUri.AbsolutePath -ne '/' -or $taskUri.Query -or $taskUri.Fragment) { throw 'Use the public HTTPS API origin without a path.' }
if ([string]::IsNullOrWhiteSpace($taskClientId) -or [string]::IsNullOrWhiteSpace($taskApiKey)) { throw 'Set CommercePayOS__ClientId and CommercePayOS__ApiKey in this trusted environment; do not paste them into chat.' }
$taskWebhook = $taskUri.GetLeftPart([UriPartial]::Authority) + '/api/mua/commerce/payos/webhook'
$taskBody = @{ webhookUrl = $taskWebhook } | ConvertTo-Json -Compress
try {
    $taskResult = Invoke-RestMethod -Method Post -Uri 'https://api-merchant.payos.vn/confirm-webhook' -Headers @{ 'x-client-id' = $taskClientId; 'x-api-key' = $taskApiKey } -ContentType 'application/json' -Body $taskBody -TimeoutSec 30
} catch { throw 'payOS webhook confirmation failed. Check the trusted environment, backend checksum key, HTTPS domain and server logs; credentials were not printed.' }
if ($taskResult.code -ne '00') { throw 'payOS did not confirm the webhook. Check backend readiness and signature validation.' }
Write-Output ('Confirmed commerce webhook: ' + $taskWebhook)
