# Builds the single-exe release (framework-dependent + ReadyToRun: requires the .NET 10 Desktop Runtime on the target PC) and zips it. Upload the zip to GitHub Releases under a tag like v0.1.0.
$ErrorActionPreference = 'Stop'
$out = Join-Path $PSScriptRoot 'artifacts'
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish "$PSScriptRoot/src/AbilityDraftOverlay/ADraftCompact.csproj" -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -p:PublishSingleFile=true -o "$out/publish"
$version = (Select-String -Path "$PSScriptRoot/src/AbilityDraftOverlay/ADraftCompact.csproj" -Pattern '<Version>(.+)</Version>').Matches[0].Groups[1].Value
Compress-Archive -Path "$out/publish/AbilityDraftOverlay.exe" -DestinationPath "$out/AbilityDraftOverlay-v$version-win-x64.zip" -Force
Write-Host "Done: $out/AbilityDraftOverlay-v$version-win-x64.zip"
