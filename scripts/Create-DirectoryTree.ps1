function Ensure-DirectoryPath {
    <#
    .SYNOPSIS
    Ensures that the specified directory path exists, creating parent directories as needed.
    .DESCRIPTION
    This function takes a directory path as input. It checks if the directory exists.
    If it does not exist, it creates the directory and any necessary parent directories.
    The function is idempotent, meaning running it multiple times with the same path
    will not cause errors or change the state after the first successful execution.
    .PARAMETER Path
    The full path of the directory to ensure existence of.
    .EXAMPLE
    Ensure-DirectoryPath -Path "C:\\MyData\\Output\\Reports"
    Ensures the "Reports" directory exists within "Output" within "MyData" on the C: drive.
    .EXAMPLE
    Ensure-DirectoryPath "bin/nuget"
    Ensures the "nuget" directory exists within the "bin" directory relative to the current location.
    #>
    param(
        [Parameter(Mandatory=$true, Position=0)]
        [string]$Path
    )

    # Normalize the path to handle both '/' and '\' separators
    $NormalizedPath = $Path -replace '/', '\\'

    # Check if the directory already exists
    if (-not (Test-Path -Path $NormalizedPath -PathType Container)) {
        try {
            # Create the directory. The -Force switch creates parent directories if they don't exist.
            $null = New-Item -ItemType Directory -Path $NormalizedPath -Force -ErrorAction Stop
            Write-Host "Created directory structure: $NormalizedPath"
        }
        catch {
            Write-Error "Failed to create directory structure '$NormalizedPath': $_"
            # Re-throw the exception if you want the script calling this function to handle it
            # throw $_
        }
    }
    else {
        Write-Host "Directory already exists: $NormalizedPath"
    }
}

# Example usage (can be commented out or removed if used purely as a library)
# Ensure-DirectoryPath -Path ".\example\nested\directory"
# Ensure-DirectoryPath -Path "another/example/structure"

# Export the function to make it available for dot-sourcing
# Export-ModuleMember -Function Ensure-DirectoryPath 