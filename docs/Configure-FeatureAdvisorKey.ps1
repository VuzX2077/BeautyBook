param([string]$Model='gemini-3.5-flash-lite')
$ErrorActionPreference='Stop'
# Owner-run only. Writes this exact FeatureAdvisor section to the backend's
# .NET User Secrets store. Never searches other credentials or lists secrets.
$project=Join-Path $PSScriptRoot '..\BeautyBookBackend\BeautyBookBackend.csproj'
$secure=Read-Host 'Gemini API key (hidden; do not paste into chat)' -AsSecureString
$pointer=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try {
  $key=[Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
  if([string]::IsNullOrWhiteSpace($key)){throw 'Key is empty'}
  $settings=@{'FeatureAdvisor:ApiKey'=$key;'FeatureAdvisor:Model'=$Model;'FeatureAdvisor:SyntheticDiagnosticsEnabled'='true';'FeatureAdvisor:Enabled'='false';'FeatureAdvisor:CloudEnabled'='false';'FeatureAdvisor:DataProcessingApproval'='pending';'FeatureAdvisor:ProviderDataPolicy'='unpaid';'FeatureAdvisor:OnlineEnvironmentApproved'='false';'FeatureAdvisor:RealUserDataApproved'='false';'FeatureAdvisor:ModelAvailabilityVerified'='false';'FeatureAdvisor:TimeoutSeconds'='18'}
  $settings | ConvertTo-Json -Compress | dotnet user-secrets set --project $project | Out-Null
  if($LASTEXITCODE -ne 0){throw 'User Secrets configuration failed'}
  Write-Output 'Configured synthetic diagnostics. Real-user sharing remains OFF. Run the diagnostic commands in GEMINI-SETUP.md.'
} finally {
  [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
  $key=$null;$settings=$null;$secure.Dispose()
}
