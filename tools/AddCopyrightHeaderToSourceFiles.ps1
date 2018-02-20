$lineBreak = "`r`n"
$currentYear = get-date -Format yyyy
$copyRightBoundary = "//-----------------------------------------------------------------------"
$noticeTemplate = "$copyRightBoundary$lineBreak// <copyright file=`"[FileName]`" company=`"Akka.NET Project`">$lineBreak//     Copyright (C) 2009-$currentYear Lightbend Inc. <http://www.lightbend.com>$lineBreak//     Copyright (C) 2013-$currentYear .NET Foundation <https://github.com/akkadotnet/akka.net>$lineBreak// </copyright>$lineBreak$copyRightBoundary$lineBreak$lineBreak"
$tokenToReplace = [regex]::Escape("[FileName]")
$copyrightSnippet = [regex]::Escape("<copyright")

Function CreateFileSpecificNotice($sourcePath){
    $fileName = Split-Path $sourcePath -Leaf
    $fileSpecificNotice = $noticeTemplate -replace $tokenToReplace, $fileName
    return $fileSpecificNotice
}

Function SourceFileContainsNotice($sourcePath){
    $arrMatchResults = Get-Content $sourcePath | Select-String $copyrightSnippet

    if ($arrMatchResults -ne $null -and $arrMatchResults.count -gt 0){
        return $true 
    }
    else{ 
        return $false 
    }
}

Function AddHeaderToSourceFile($sourcePath) {
    # "Source path is: $sourcePath"
    
    $noticeToInsert = CreateFileSpecificNotice($sourcePath)
    
    $containsNotice = SourceFileContainsNotice $sourcePath

    if ($containsNotice){
        Write-Host "$sourcePath has pre-existing headers. Replacing them."
        $fileLines = (Get-Content $sourcePath | select -Skip 7) -join $lineBreak

            
        $content = $noticeToInsert + $fileLines
        $content | Out-File $sourcePath -Encoding utf8
    }
    else {
        #"Source file does not contain notice -- adding or replacing
         Write-Host "$sourcePath has no headers. Adding them"

        $fileLines = (Get-Content $sourcePath) -join $lineBreak     

        $content = $noticeToInsert + $fileLines
        $content | Out-File $sourcePath -Encoding utf8  
    }
}

$scriptPath = split-path -parent $MyInvocation.MyCommand.Definition
$parent = (get-item $scriptPath).Parent.FullName
$startingPath = "$parent\src"
Get-ChildItem  $startingPath\*.cs -Recurse | Select FullName | Foreach-Object { AddHeaderToSourceFile($_.FullName)}
Get-ChildItem  $startingPath\*.fs -Recurse | Select FullName | Foreach-Object { AddHeaderToSourceFile($_.FullName)}
