$currentDirectory = split-path $MyInvocation.MyCommand.Definition

# See if we have the ClientSecret available
if([string]::IsNullOrEmpty($env:SignClientSecret)){
	Write-Host "Client Secret not found, not signing packages"
	return;
}

dotnet tool install --tool-path . SignClient

# Setup Variables we need to pass into the sign client tool
$appSettings = "$currentDirectory\appsettings.json"
$filter = "$currentDirectory\filter.txt"

$nupgks = gci $Env:ArtifactDirectory\*.nupkg -Recurse | Select -ExpandProperty FullName
$vsixs = gci $Env:ArtifactDirectory\*.vsix -Recurse | Select -ExpandProperty FullName

foreach ($nupkg in $nupgks){
	Write-Host "Submitting $nupkg for signing"

	.\SignClient 'sign' -c $appSettings -i $nupkg -f $filter -r $env:SignClientUser -s $env:SignClientSecret -n 'xUnit.net' -d 'xUnit.net' -u 'https://github.com/xunit/visualstudio.xunit' 

	Write-Host "Finished signing $nupkg"
}

Write-Host "Sign-package complete"