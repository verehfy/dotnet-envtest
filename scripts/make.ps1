<#
.SYNOPSIS
    Developer-experience tasks for Kubernetes.EnvTest (Windows counterpart of the Makefile).

.PARAMETER Target
    One of: build, test, test-integration, test-all, bench, format, format-check, pack, publish-tool, clean.

.EXAMPLE
    ./scripts/make.ps1 build
    ./scripts/make.ps1 pack -Configuration Debug
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('build', 'test', 'test-integration', 'test-all', 'bench', 'format', 'format-check', 'pack', 'publish-tool', 'clean')]
    [string] $Target = 'build',

    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

$solution = 'Kubernetes.EnvTest.slnx'
$artifacts = 'artifacts'

# Local builds target only the current machine's RID; the release workflow
# owns the full six-RID matrix.
$ridOs = if ($IsMacOS) { 'osx' } elseif ($IsLinux) { 'linux' } else { 'win' }
$ridArch = if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'arm64' } else { 'x64' }
$rid = "$ridOs-$ridArch"

function Invoke-Step {
    param([string[]] $Arguments)
    Write-Host "> dotnet $($Arguments -join ' ')" -ForegroundColor Cyan
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet exited with code $LASTEXITCODE"
    }
}

switch ($Target) {
    'build' { Invoke-Step @('build', $solution, '-c', $Configuration) }
    'test' { Invoke-Step @('test', 'test/Kubernetes.EnvTest.Tests/Kubernetes.EnvTest.Tests.csproj', '-c', $Configuration) }
    'test-integration' { Invoke-Step @('test', 'test/Kubernetes.EnvTest.IntegrationTests/Kubernetes.EnvTest.IntegrationTests.csproj', '-c', $Configuration) }
    'test-all' {
        Invoke-Step @('test', 'test/Kubernetes.EnvTest.Tests/Kubernetes.EnvTest.Tests.csproj', '-c', $Configuration)
        Invoke-Step @('test', 'test/Kubernetes.EnvTest.IntegrationTests/Kubernetes.EnvTest.IntegrationTests.csproj', '-c', $Configuration)
    }
    'bench' { Invoke-Step @('run', '--project', 'test/Kubernetes.EnvTest.Benchmarks', '-c', 'Release', '--', '--filter', '*') }
    'format' { Invoke-Step @('format', $solution) }
    'format-check' { Invoke-Step @('format', $solution, '--verify-no-changes') }
    'pack' {
        Invoke-Step @('pack', 'src/Kubernetes.EnvTest.Provisioning', '-c', $Configuration, '-o', $artifacts)
        Invoke-Step @('pack', 'src/Kubernetes.EnvTest', '-c', $Configuration, '-o', $artifacts)
        Invoke-Step @('pack', 'src/Kubernetes.EnvTest.Tool', '-c', $Configuration, '-o', $artifacts, '-r', $rid)
    }
    'publish-tool' { Invoke-Step @('publish', 'src/Kubernetes.EnvTest.Tool', '-c', 'Release', '-r', $rid, '-o', "$artifacts/setup-envtest-$rid") }
    'clean' {
        Invoke-Step @('clean', $solution, '-c', $Configuration)
        if (Test-Path $artifacts) { Remove-Item -Recurse -Force $artifacts }
    }
}
