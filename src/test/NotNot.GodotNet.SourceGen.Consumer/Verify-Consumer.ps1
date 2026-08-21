#Requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$fixtureRoot = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $fixtureRoot '..\..')).Path
$workspaceRoot = (Resolve-Path -LiteralPath (Join-Path $fixtureRoot '..\..\..\..\..\..')).Path
$wrapper = Join-Path $workspaceRoot '.vow-agent\tools\vow-build.ps1'
$consumerProject = Join-Path $fixtureRoot 'NotNot.GodotNet.SourceGen.Consumer.csproj'
$packageProject = Join-Path $repoRoot 'nuget\NotNot.GodotNet.SourceGen.Package\NotNot.GodotNet.SourceGen.Package.csproj'
$feed = Join-Path $fixtureRoot 'feed'

foreach ($path in @($wrapper, $consumerProject, $packageProject)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required verification path is missing: $path"
    }
}

New-Item -ItemType Directory -Force -Path $feed | Out-Null

function Invoke-VowBuild {
    param(
        [Parameter(Mandatory)]
        [string[]] $DotnetArguments,
        [switch] $AllowFailure
    )

    $output = (& pwsh -NoProfile -File $wrapper -- @DotnetArguments 2>&1 | Out-String)
    $exitCode = $LASTEXITCODE
    Write-Host $output -NoNewline

    if (-not $AllowFailure -and $exitCode -ne 0) {
        throw "vow-build failed with exit code ${exitCode}: $($DotnetArguments -join ' ')"
    }

    [pscustomobject]@{
        ExitCode = $exitCode
        Output = $output
    }
}

$version = "0.0.0-consumer.$([DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))"

$withoutGenerator = Invoke-VowBuild -AllowFailure -DotnetArguments @(
    'build', $consumerProject, '--nologo',
    '-p:EnableGodotSourceGen=false'
)
if ($withoutGenerator.ExitCode -eq 0 -or $withoutGenerator.Output -notmatch 'NotNotSceneRoot|_ResPath|InstantiateTscn') {
    throw 'Generator-absent red check did not fail on a generated API reference.'
}
Write-Host "[consumer-verify] generator-absent red check: PASS (exit=$($withoutGenerator.ExitCode))"

$builtPackage = Invoke-VowBuild -DotnetArguments @(
    'build', $packageProject, '--configuration', 'Debug', '--nologo',
    "-p:MinVerVersionOverride=$version"
)

$packed = Invoke-VowBuild -DotnetArguments @(
    'pack', $packageProject, '--configuration', 'Debug', '--nologo',
    "-p:MinVerVersionOverride=$version",
    "-p:PackageOutputPath=$feed"
)
$packagePath = Join-Path $feed "NotNot.GodotNet.SourceGen.$version.nupkg"
if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
    throw "Fresh package was not created at $packagePath"
}
Write-Host "[consumer-verify] fresh local package: PASS ($packagePath)"

$green = Invoke-VowBuild -DotnetArguments @(
    'build', $consumerProject, '--nologo',
    "-p:GodotSourceGenPackageVersion=$version"
)
if ($green.ExitCode -ne 0 -or $green.Output -notmatch 'Build succeeded\.') {
    throw 'Packed consumer green build did not succeed.'
}
Write-Host "[consumer-verify] generated consumer green check: PASS (exit=$($green.ExitCode))"

$analyzerProbe = Invoke-VowBuild -AllowFailure -DotnetArguments @(
    'build', $consumerProject, '--nologo',
    "-p:GodotSourceGenPackageVersion=$version",
    '-p:EnableAnalyzerProbe=true'
)
if ($analyzerProbe.ExitCode -eq 0 -or $analyzerProbe.Output -notmatch '\bGODOT002\b') {
    throw 'Packaged analyzer red check did not observe GODOT002.'
}
Write-Host "[consumer-verify] packaged analyzer red check: PASS (exit=$($analyzerProbe.ExitCode), GODOT002 observed)"

Write-Host "[consumer-verify] code-fix activation: NOT_RUN (CLI build only)"
