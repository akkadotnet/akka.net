#!/usr/bin/env pwsh
# ThreadLocalPool Recycler Test Runner for Akka.Remote
# 
# This script makes it easy to test the impact of disabling the DotNetty ThreadLocalPool recycler.
# Based on the memory analysis from Akka.NET team, this addresses a race condition that specifically
# affects ARM64 + Server GC environments.

param(
    [string]$Mode = "compare",           # compare, enabled, disabled
    [int]$Times = 1,                     # Number of benchmark runs
    [string]$Framework = "net6.0",       # Target framework
    [switch]$Verbose,                    # Verbose output
    [switch]$MonitorExceptions,          # Monitor for ThreadLocalPool exceptions
    [switch]$Help                        # Show help
)

function Show-Help {
    Write-Host "ThreadLocalPool Recycler Test Runner" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Tests the impact of disabling DotNetty ThreadLocalPool recycler in Akka.Remote"
    Write-Host "to address NullReferenceException race conditions on ARM64 + Server GC."
    Write-Host ""
    Write-Host "USAGE:" -ForegroundColor Yellow
    Write-Host "  .\run-recycler-test.ps1 [options]"
    Write-Host ""
    Write-Host "OPTIONS:" -ForegroundColor Yellow
    Write-Host "  -Mode <mode>           Test mode: compare, enabled, disabled (default: compare)"
    Write-Host "  -Times <n>             Number of benchmark runs (default: 1)"  
    Write-Host "  -Framework <framework> Target framework (default: net6.0)"
    Write-Host "  -Verbose               Enable verbose output"
    Write-Host "  -MonitorExceptions     Monitor for ThreadLocalPool exceptions"
    Write-Host "  -Help                  Show this help"
    Write-Host ""
    Write-Host "EXAMPLES:" -ForegroundColor Green
    Write-Host "  .\run-recycler-test.ps1                                    # Compare both modes"
    Write-Host "  .\run-recycler-test.ps1 -Mode disabled -Times 3            # Test disabled mode 3 times"
    Write-Host "  .\run-recycler-test.ps1 -Mode compare -Verbose             # Detailed comparison"
    Write-Host "  .\run-recycler-test.ps1 -Mode enabled -MonitorExceptions   # Monitor for crashes"
    Write-Host ""
    Write-Host "INTERPRETATION:" -ForegroundColor Yellow
    Write-Host "✓ SUCCESS: Zero ThreadLocalPool exceptions with recycler disabled"
    Write-Host "✓ ACCEPTABLE: Performance impact < 10% (higher GC pressure is expected)"
    Write-Host "⚠ WARNING: Significant performance impact > 10%"
    Write-Host "✗ FAILURE: ThreadLocalPool exceptions still occurring when disabled"
}

if ($Help) {
    Show-Help
    exit 0
}

# Validate parameters
if ($Mode -notin @("compare", "enabled", "disabled")) {
    Write-Error "Invalid mode '$Mode'. Must be: compare, enabled, disabled"
    exit 1
}

if ($Times -lt 1 -or $Times -gt 10) {
    Write-Error "Times must be between 1 and 10"
    exit 1
}

Write-Host "=== ThreadLocalPool Recycler Test Runner ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Configuration:" -ForegroundColor Yellow
Write-Host "  Mode:       $Mode"
Write-Host "  Times:      $Times"
Write-Host "  Framework:  $Framework"
Write-Host "  Verbose:    $Verbose"
Write-Host "  Monitor:    $MonitorExceptions"
Write-Host ""

# Check if we're in the right directory
if (-not (Test-Path "RemotePingPong.csproj")) {
    Write-Error "Please run this script from the RemotePingPong directory"
    Write-Host "Expected path: src/benchmark/RemotePingPong/" -ForegroundColor Yellow
    exit 1
}

# Build the project first
Write-Host "Building RemotePingPong..." -ForegroundColor Green
$buildArgs = @("build", "-c", "Release", "-f", $Framework)
$buildResult = & dotnet $buildArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed!"
    exit 1
}

# Prepare arguments for the recycler test
$testArgs = @()
$testArgs += "-p:RecyclerTest=true"  # Use the recycler test main program

# Add mode-specific arguments
switch ($Mode) {
    "compare" {
        $testArgs += "--", "--compare"
    }
    "enabled" {
        # Default behavior (recycler enabled)
    }
    "disabled" {
        $testArgs += "--", "--disable-recycler"
    }
}

# Add common arguments
$testArgs += "--times", $Times

if ($Verbose) {
    $testArgs += "--verbose"
}

if ($MonitorExceptions) {
    $testArgs += "--monitor-exceptions"
}

# Show system information
Write-Host "System Information:" -ForegroundColor Yellow
Write-Host "  OS:           $([System.Environment]::OSVersion)"
Write-Host "  Processors:   $([System.Environment]::ProcessorCount)"
Write-Host "  Server GC:    $([System.GC]::IsServerGC)"
Write-Host "  .NET Version: $(dotnet --version)"
Write-Host ""

# Show current recycler setting
$currentSetting = [System.Environment]::GetEnvironmentVariable("io.netty.recycler.maxCapacityPerThread")
if ($currentSetting) {
    Write-Host "Current recycler setting: $currentSetting" -ForegroundColor Yellow
} else {
    Write-Host "Current recycler setting: NOT SET" -ForegroundColor Yellow
}
Write-Host ""

# Run the test
Write-Host "Running RemotePingPong Recycler Test..." -ForegroundColor Green
Write-Host "Command: dotnet run -c Release -f $Framework $($testArgs -join ' ')" -ForegroundColor Gray
Write-Host ""

$runArgs = @("run", "-c", "Release", "-f", $Framework) + $testArgs
$testResult = & dotnet $runArgs

# Check results
if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "✓ Test completed successfully!" -ForegroundColor Green
    
    if ($Mode -eq "compare") {
        Write-Host ""
        Write-Host "NEXT STEPS:" -ForegroundColor Yellow
        Write-Host "1. If recycler disabled eliminated exceptions → Safe to deploy with io.netty.recycler.maxCapacityPerThread=0"
        Write-Host "2. If performance impact acceptable (< 10%) → Proceed with mitigation"
        Write-Host "3. If performance impact significant → Consider code-level fixes in DotNetty ThreadLocalPool"
        Write-Host ""
        Write-Host "PRODUCTION DEPLOYMENT:" -ForegroundColor Yellow
        Write-Host "  Environment Variable: io.netty.recycler.maxCapacityPerThread=0"
        Write-Host "  Docker: -e io.netty.recycler.maxCapacityPerThread=0"
        Write-Host "  K8s: env: [{name: io.netty.recycler.maxCapacityPerThread, value: '0'}]"
    }
} else {
    Write-Host ""
    Write-Host "✗ Test failed with exit code $LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
} 