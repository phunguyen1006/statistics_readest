$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$dotnet = Join-Path $projectRoot '.dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    throw 'The local .NET 8 SDK was not found. Install .NET 8 SDK or restore the .dotnet folder.'
}
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.nuget\packages'
& $dotnet test (Join-Path $projectRoot 'ReadestStats.sln') -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnet build-server shutdown | Out-Null
& $dotnet publish (Join-Path $projectRoot 'src\ReadestStats\ReadestStats.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o (Join-Path $projectRoot 'artifacts\win-x64')
exit $LASTEXITCODE
