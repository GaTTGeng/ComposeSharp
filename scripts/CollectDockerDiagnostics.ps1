[CmdletBinding()]
param(
    [string]$OutputDirectory = "artifacts/docker-diagnostics"
)

$ErrorActionPreference = "Continue"

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

function Save-CommandOutput {
    param(
        [string]$FileName,
        [string]$Command,
        [string[]]$Arguments = @()
    )

    $targetPath = Join-Path $OutputDirectory $FileName
    try {
        $output = & $Command @Arguments 2>&1
        $exitCode = $LASTEXITCODE
        @(
            "Command: $Command $($Arguments -join ' ')"
            "ExitCode: $exitCode"
            "TimestampUtc: $([DateTimeOffset]::UtcNow.ToString('O'))"
            ""
            $output
        ) | Out-File -FilePath $targetPath -Encoding utf8
    }
    catch {
        @(
            "Command: $Command $($Arguments -join ' ')"
            "Exception: $($_.Exception.Message)"
            "TimestampUtc: $([DateTimeOffset]::UtcNow.ToString('O'))"
        ) | Out-File -FilePath $targetPath -Encoding utf8
    }
}

# Host and endpoint context
$envLines = [System.Collections.Generic.List[string]]::new()
$envLines.Add("TimestampUtc: $([DateTimeOffset]::UtcNow.ToString('O'))")
$envLines.Add("OSDescription: $([System.Runtime.InteropServices.RuntimeInformation]::OSDescription)")
$envLines.Add("ProcessArchitecture: $([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture)")
$envLines.Add("PowerShellVersion: $($PSVersionTable.PSVersion)")
$envLines.Add("DOCKER_HOST: $($env:DOCKER_HOST)")
$envLines.Add("COMPOSESHARP_REQUIRE_DOCKER: $($env:COMPOSESHARP_REQUIRE_DOCKER)")
$envLines.Add("COMPOSESHARP_EXPECTED_DOCKER_ENDPOINT: $($env:COMPOSESHARP_EXPECTED_DOCKER_ENDPOINT)")
$envLines.Add("COMPOSESHARP_EXPECTED_DOCKER_OS: $($env:COMPOSESHARP_EXPECTED_DOCKER_OS)")

if ($IsWindows) {
    $envLines.Add("DefaultEndpoint: npipe://./pipe/docker_engine")
    try {
        $pipes = Get-ChildItem -Path "\\.\pipe\" -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like "*docker*" } |
            Select-Object -ExpandProperty Name
        $envLines.Add("DockerNamedPipes: $($pipes -join ', ')")
    }
    catch {
        $envLines.Add("DockerNamedPipes: <failed to enumerate: $($_.Exception.Message)>")
    }

    try {
        $services = Get-Service -Name "*docker*" -ErrorAction SilentlyContinue |
            ForEach-Object { "$($_.Name)=$($_.Status)" }
        $envLines.Add("DockerServices: $($services -join ', ')")
    }
    catch {
        $envLines.Add("DockerServices: <failed to query: $($_.Exception.Message)>")
    }
}
else {
    $envLines.Add("DefaultEndpoint: unix:///var/run/docker.sock")
    $envLines.Add("UnixSocketExists: $(Test-Path -LiteralPath '/var/run/docker.sock')")
}

$envLines | Out-File -FilePath (Join-Path $OutputDirectory "environment.txt") -Encoding utf8

# Core Docker daemon and resource diagnostics
Save-CommandOutput -FileName "docker-version.txt" -Command "docker" -Arguments @("version")
Save-CommandOutput -FileName "docker-info.txt" -Command "docker" -Arguments @("info")
Save-CommandOutput -FileName "docker-ps.txt" -Command "docker" -Arguments @("ps", "-a", "--no-trunc")
Save-CommandOutput -FileName "docker-images.txt" -Command "docker" -Arguments @("images", "-a", "--no-trunc")
Save-CommandOutput -FileName "docker-networks.txt" -Command "docker" -Arguments @("network", "ls", "--no-trunc")
Save-CommandOutput -FileName "docker-volumes.txt" -Command "docker" -Arguments @("volume", "ls")
Save-CommandOutput -FileName "docker-events.txt" -Command "docker" -Arguments @("events", "--since", "15m", "--until", "0s")

# Inspect and collect logs from any remaining Compose-labeled containers
try {
    $containerIds = & docker ps -aq --filter "label=com.docker.compose.project" 2>$null
    if ($LASTEXITCODE -eq 0 -and $containerIds) {
        $containerDir = Join-Path $OutputDirectory "containers"
        New-Item -ItemType Directory -Path $containerDir -Force | Out-Null

        foreach ($id in ($containerIds | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
            $shortId = $id.Trim()
            Save-CommandOutput -FileName (Join-Path "containers" "$shortId-inspect.json") -Command "docker" -Arguments @("inspect", $shortId)
            Save-CommandOutput -FileName (Join-Path "containers" "$shortId-logs.txt") -Command "docker" -Arguments @("logs", "--timestamps", $shortId)
        }
    }
}
catch {
    "Failed to inspect project containers: $($_.Exception.Message)" |
        Out-File -FilePath (Join-Path $OutputDirectory "container-diagnostics-error.txt") -Encoding utf8
}

# Inspect any remaining Compose-labeled networks
try {
    $networkIds = & docker network ls -q --filter "label=com.docker.compose.project" 2>$null
    if ($LASTEXITCODE -eq 0 -and $networkIds) {
        $networkList = @($networkIds | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Trim() })
        if ($networkList.Count -gt 0) {
            Save-CommandOutput -FileName "docker-project-networks-inspect.json" -Command "docker" -Arguments (@("network", "inspect") + $networkList)
        }
    }
}
catch {
    "Failed to inspect project networks: $($_.Exception.Message)" |
        Out-File -FilePath (Join-Path $OutputDirectory "network-diagnostics-error.txt") -Encoding utf8
}

Write-Host "Collected Docker diagnostics in '$OutputDirectory'."
