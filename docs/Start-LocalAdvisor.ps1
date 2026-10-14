param([switch]$EnableRealData)
$ErrorActionPreference='Stop'
if($EnableRealData -and ($env:FeatureAdvisor__DataProcessingApproval -ne 'approved' -or $env:FeatureAdvisor__ProviderDataPolicy -ne 'paid_terms')){throw 'Real facial data is pending provider/data review. Use synthetic --verify; this switch cannot approve unpaid personal data.'}
$savedAdvisorEnv=@{}
foreach($name in @('FeatureAdvisor__Enabled','FeatureAdvisor__CloudEnabled','FeatureAdvisor__RealUserDataApproved','FeatureAdvisor__ModelAvailabilityVerified','FeatureAdvisor__Model','DOTNET_CLI_TELEMETRY_OPTOUT')){$savedAdvisorEnv[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
# Process environment only. Never writes appsettings, secrets, Render or billing.
# Review is separate from owner/tester consent. Defaults remain closed.
$env:FeatureAdvisor__Enabled=if($EnableRealData){'true'}else{'false'}
$env:FeatureAdvisor__RealUserDataApproved=if($EnableRealData){'true'}else{'false'}
$env:FeatureAdvisor__CloudEnabled=if($EnableRealData){'true'}else{'false'}
$env:FeatureAdvisor__ModelAvailabilityVerified='true'
$env:FeatureAdvisor__Model='gemini-3.5-flash-lite'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$advisorRoot=Split-Path $PSScriptRoot -Parent
Push-Location $advisorRoot
try { dotnet run --project .\BeautyBookAdvisor.Diagnostics -- --serve-advisor }
finally { Pop-Location;foreach($name in $savedAdvisorEnv.Keys){[Environment]::SetEnvironmentVariable($name,$savedAdvisorEnv[$name],'Process')} }
