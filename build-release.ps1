$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$dotnet = Join-Path $projectRoot '.dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    throw 'The local .NET 8 SDK was not found. Install .NET 8 SDK or restore the .dotnet folder.'
}
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.nuget\packages'
$projectFile = Join-Path $projectRoot 'src\ReadestStats\ReadestStats.csproj'
[xml]$projectXml = Get-Content -LiteralPath $projectFile
$version = [string]$projectXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw 'The application version is missing from ReadestStats.csproj.'
}
$versionOutput = Join-Path $projectRoot ("artifacts\v$version")
$latestOutput = Join-Path $projectRoot 'artifacts\win-x64'
& $dotnet test (Join-Path $projectRoot 'ReadestStats.sln') -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnet build-server shutdown | Out-Null
New-Item -ItemType Directory -Force -Path $versionOutput | Out-Null
& $dotnet publish $projectFile -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $versionOutput
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
New-Item -ItemType Directory -Force -Path $latestOutput | Out-Null
Copy-Item -LiteralPath (Join-Path $versionOutput 'ReadestStats.exe') -Destination (Join-Path $latestOutput 'ReadestStats.exe') -Force
Write-Host "Published v$version to $versionOutput"
exit 0
