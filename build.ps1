. "$PSScriptRoot\scripts\getReleaseNotes.ps1"
. "$PSScriptRoot\scripts\bumpVersion.ps1"

######################################################################
# Step 1: Grab release notes and update solution metadata
######################################################################
$releaseNotes = Get-ReleaseNotes -MarkdownFile (Join-Path -Path $PSScriptRoot -ChildPath "RELEASE_NOTES.md")

# inject release notes into Directory.Buil
UpdateVersionAndReleaseNotes -ReleaseNotesResult $releaseNotes -XmlFilePath (Join-Path -Path $PSScriptRoot -ChildPath "Directory.Build.props")

Write-Output "Added release notes $releaseNotes"