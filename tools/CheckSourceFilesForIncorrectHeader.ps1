$copyright_pattern = "copyright"

Write-Host "The following C# files do not have a copyright notice:`n"
gci -Path ..\src -Recurse|? {$_.FullName -notlike "*bin*" -and $_.FullName -notlike "*obj*" -and $_.Name -match "\.cs$"}|% {if (-not (gc $_.FullName | select-string -SimpleMatch $_.Name)){$_}}|select -ExpandProperty FullName

Write-Host "`n`n"

Write-Host "The following F# files do not have a copyright notice:`n"
gci -Path ..\src -Recurse|? {$_.FullName -notlike "*bin*" -and $_.FullName -notlike "*obj*" -and $_.Name -match "\.fs$"}|% {if (-not (gc $_.FullName | select-string -SimpleMatch $_.Name)){$_}}|select -ExpandProperty FullName