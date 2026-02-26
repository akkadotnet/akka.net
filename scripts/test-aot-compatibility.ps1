param([string]$targetNetFramework = "net8.0")

$rootDirectory = Split-Path $PSScriptRoot -Parent
$publishOutput = dotnet publish $rootDirectory/src/aot/Akka.AOT.App/Akka.AOT.App.csproj -nodeReuse:false /p:UseSharedCompilation=false /p:ExposeExperimentalFeatures=true

$actualWarningCount = 0

foreach ($line in $($publishOutput -split "`r`n"))
{
    if ($line -like "*analysis warning IL*")
    {
        Write-Host $line

        $actualWarningCount += 1
    }
}

# Determine the OS-specific folder
$osPlatform = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
if ($osPlatform -match "Windows") {
    $osFolder = "win-x64"
} else {
    $osFolder = "linux-x64"
    # Default to linux
}

$testAppPath = Join-Path -Path $rootDirectory/src/aot/Akka.AOT.App/bin/Release/$targetNetFramework -ChildPath $osFolder/publish

if (-Not (Test-Path $testAppPath)) {
    Write-Error "Test App path does not exist: $testAppPath"
    Exit 1
}

Write-Host $testAppPath
pushd $testAppPath

Write-Host "Executing test App..."
if ($osPlatform -match "Windows") {
    ./Akka.AOT.App.exe
} else {
    # Default to linux
    ./Akka.AOT.App    
}

Write-Host "Finished executing test App"

if ($LastExitCode -ne 0)
{
  Write-Host "There was an error while executing AotCompatibility Test App. LastExitCode is:", $LastExitCode
}

popd

Write-Host "Actual warning count is:", $actualWarningCount
$expectedWarningCount = 0

$testPassed = 0
if ($actualWarningCount -ne $expectedWarningCount)
{
    $testPassed = 1
    Write-Host "Actual warning count:", $actualWarningCount, "is not as expected. Expected warning count is:", $expectedWarningCount
}

Exit $testPassed
