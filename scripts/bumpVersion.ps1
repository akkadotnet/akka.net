function UpdateVersionAndReleaseNotes {
    param (
        [Parameter(Mandatory=$true)]
        [PSCustomObject]$ReleaseNotesResult,

        [Parameter(Mandatory=$true)]
        [string]$XmlFilePath
    )

    # Load XML
    $xmlContent = New-Object XML
    $xmlContent.Load($XmlFilePath)

    # Update VersionPrefix and PackageReleaseNotes
    $versionPrefixElement = $xmlContent.SelectSingleNode("//VersionPrefix")
    $versionPrefixElement.InnerText = $ReleaseNotesResult.Version

    $packageReleaseNotesElement = $xmlContent.SelectSingleNode("//PackageReleaseNotes")
    $packageReleaseNotesElement.InnerText = $ReleaseNotesResult.ReleaseNotes

    # Save the updated XML
    $xmlContent.Save($XmlFilePath)
}

# Usage example:
# $notes = Get-ReleaseNotes -MarkdownFile "$PSScriptRoot\RELEASE_NOTES.md"
# $propsPath = Join-Path -Path (Get-Item $PSScriptRoot).Parent.FullName -ChildPath "Directory.Build.props"
# UpdateVersionAndReleaseNotes -ReleaseNotesResult $notes -XmlFilePath $propsPath
