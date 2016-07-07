$lineBreak = "`r`n"
$noticeTemplate = "//-----------------------------------------------------------------------$lineBreak// <copyright file=`"[FileName]`" company=`"Akka.NET Project`">$lineBreak//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>$lineBreak//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>$lineBreak// </copyright>$lineBreak//-----------------------------------------------------------------------$lineBreak$lineBreak"
$tokenToReplace = [regex]::Escape("[FileName]")

Function CreateFileSpecificNotice($sourcePath){
    $fileName = Split-Path $sourcePath -Leaf
    $fileSpecificNotice = $noticeTemplate -replace $tokenToReplace, $fileName
    return $fileSpecificNotice
}

Function SourceFileContainsNotice($sourcePath){
    $copyrightSnippet = [regex]::Escape("<copyright")

    $fileSpecificNotice = CreateFileSpecificNotice($sourcePath)
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
    
    $containsNotice = SourceFileContainsNotice($sourcePath)
    # "Contains notice: $containsNotice"

    if ($containsNotice){
        #"Source file already contains notice -- not adding"
    }
    else {
        #"Source file does not contain notice -- adding"
        $noticeToInsert = CreateFileSpecificNotice($sourcePath)

        $fileLines = (Get-Content $sourcePath) -join $lineBreak
    
        $content = $noticeToInsert + $fileLines + $lineBreak

        $content | Out-File $sourcePath -Encoding utf8

    }
}

$scriptPath = split-path -parent $MyInvocation.MyCommand.Definition
$parent = (get-item $scriptPath).Parent.FullName
$startingPath = "$parent\src"
Get-ChildItem  $startingPath\*.cs -Recurse | Select FullName | Foreach-Object { AddHeaderToSourceFile($_.FullName)}
Get-ChildItem  $startingPath\*.fs -Recurse | Select FullName | Foreach-Object { AddHeaderToSourceFile($_.FullName)}
