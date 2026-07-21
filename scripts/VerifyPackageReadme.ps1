[CmdletBinding()]
param(
    [string]$PackageDirectory = "artifacts/packages"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $PackageDirectory)) {
    throw "Package directory was not found: $PackageDirectory"
}

$packages = Get-ChildItem -LiteralPath $PackageDirectory -Filter "*.nupkg" -File
if ($packages.Count -eq 0) {
    throw "No NuGet packages were found in: $PackageDirectory"
}

Add-Type -AssemblyName System.IO.Compression.FileSystem

foreach ($package in $packages) {
    $archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        $readme = $archive.Entries | Where-Object { $_.FullName -eq "README.md" } | Select-Object -First 1
        if ($null -eq $readme -or $readme.Length -eq 0) {
            throw "Package '$($package.Name)' does not contain a non-empty README.md."
        }
    }
    finally {
        $archive.Dispose()
    }
}

Write-Host "Validated README.md in $($packages.Count) NuGet package(s)."
