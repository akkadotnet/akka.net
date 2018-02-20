$lineBreak = "`r`n"
$currentYear = get-date -Format yyyy
$copyRightBoundary = "//-----------------------------------------------------------------------"
$noticeTemplate = "$copyRightBoundary$lineBreak// <copyright file=`"[FileName]`" company=`"Akka.NET Project`">$lineBreak//     Copyright (C) 2009-$currentYear Lightbend Inc. <http://www.lightbend.com>$lineBreak//     Copyright (C) 2013-$currentYear .NET Foundation <https://github.com/akkadotnet/akka.net>$lineBreak// </copyright>$lineBreak$copyRightBoundary$lineBreak$lineBreak"
$tokenToReplace = [regex]::Escape("[FileName]")


$escapedBoundary = [regex]::Escape($copyRightBoundary)
$currentHeaderRegex = [regex]"($escapedBoundary)(.*)($escapedBoundary)"

Function CreateFileSpecificNotice($sourcePath){
    $fileName = Split-Path $sourcePath -Leaf
    $fileSpecificNotice = $noticeTemplate -replace $tokenToReplace, $fileName
    return $fileSpecificNotice
}

Function SourceFileContainsNotice($sourcePath){
    $fileSpecificNotice = CreateFileSpecificNotice($sourcePath)
    $arrMatchResults = Get-Content $sourcePath | Select-String $fileSpecificNotice

    if ($arrMatchResults -ne $null -and $arrMatchResults.count -gt 0){
        return $true 
    }
    else{ 
        return $false 
    }
}

Function AddHeaderToSourceFile($sourcePath) {
    # "Source path is: $sourcePath"
    
    $containsNotice = SourceFileContainsNotice($sourcePath)
    # "Contains notice: $containsNotice"

    if ($containsNotice){
        #"Source file already contains correct notice"
    }
    else {
        #"Source file does not contain notice -- adding or replacing"
        $noticeToInsert = CreateFileSpecificNotice($sourcePath)
        $copyrightSnippet = [regex]::Escape("<copyright")

        $fileLines = (Get-Content $sourcePath) -join $lineBreak

        $arrMatchResults = $fileLines | Select-String $copyrightSnippet

        if ($arrMatchResults -ne $null -and $arrMatchResults.count -gt 0){
            $content = $noticeToInsert + $fileLines + $lineBreak
        }
        else{ 
            # don't have any copyright header
            $content = ([regex]::replace($fileLines, $currentHeaderRegex, $noticeToInsert)) + $lineBreak
        }
    
        

        $content | Out-File $sourcePath -Encoding utf8

    }
}

$scriptPath = split-path -parent $MyInvocation.MyCommand.Definition
$parent = (get-item $scriptPath).Parent.FullName
$startingPath = "$parent\src"
Get-ChildItem  $startingPath\*.cs -Recurse | Select FullName | Foreach-Object { AddHeaderToSourceFile($_.FullName)}
Get-ChildItem  $startingPath\*.fs -Recurse | Select FullName | Foreach-Object { AddHeaderToSourceFile($_.FullName)}
