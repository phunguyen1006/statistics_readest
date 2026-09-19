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
$assemblyVersion = [string]$projectXml.Project.PropertyGroup.AssemblyVersion
$fileVersion = [string]$projectXml.Project.PropertyGroup.FileVersion
if ($assemblyVersion -ne "$version.0" -or $fileVersion -ne "$version.0") {
    throw "Version mismatch: Version=$version AssemblyVersion=$assemblyVersion FileVersion=$fileVersion"
}
$versionOutput = Join-Path $projectRoot ("artifacts\v$version")
& $dotnet test (Join-Path $projectRoot 'ReadestStats.sln') -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnet build-server shutdown | Out-Null
if (Test-Path -LiteralPath $versionOutput) {
    $artifactsRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
    $resolvedOutput = [IO.Path]::GetFullPath($versionOutput)
    if (-not $resolvedOutput.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean an output path outside the artifacts directory: $resolvedOutput"
    }
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $versionOutput | Out-Null
& $dotnet publish $projectFile -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $versionOutput
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$exe = Join-Path $versionOutput 'ReadestStats.exe'
$namedExe = Join-Path $versionOutput "ReadestStats-v$version.exe"
Copy-Item -LiteralPath $exe -Destination $namedExe -Force
$zip = Join-Path $versionOutput "ReadestStats-v$version-win-x64.zip"
Compress-Archive -LiteralPath $namedExe -DestinationPath $zip -CompressionLevel Optimal -Force
$checksum = (Get-FileHash -Algorithm SHA256 -LiteralPath $zip).Hash.ToLowerInvariant()
Set-Content -LiteralPath ($zip + '.sha256') -Value "$checksum  $(Split-Path -Leaf $zip)" -Encoding ascii
$manifest = [ordered]@{ version = $version; runtime = 'win-x64'; selfContained = $true; executable = (Split-Path -Leaf $namedExe); archive = (Split-Path -Leaf $zip); sha256 = $checksum; builtAtUtc = [DateTimeOffset]::UtcNow.ToString('O') }
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $versionOutput 'release-manifest.json') -Encoding utf8
Remove-Item -LiteralPath $exe -Force
Write-Host "Published and packaged v$version to $versionOutput"
exit 0
