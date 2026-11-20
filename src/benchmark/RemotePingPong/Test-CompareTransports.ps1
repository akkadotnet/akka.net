#requires -Version 7.2
# Runs without Pester or other additional modules. Exit code is nonzero on any failed check.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Compare-Transports.ps1')
$script:PassedChecks = 0

function Assert-True([bool]$Condition, [string]$Reason = 'Assertion failed.') {
    if (!$Condition) { throw $Reason }
}

function Assert-Rejected([scriptblock]$Action) {
    $rejected = $false
    try { & $Action | Out-Null }
    catch [System.IO.InvalidDataException] { $rejected = $true }
    Assert-True $rejected 'Invalid result was accepted or rejected for an unrelated reason.'
}

function Invoke-Check([string]$Name, [scriptblock]$Action) {
    & $Action
    $script:PassedChecks++
    Write-Host "PASS: $Name"
}

function Copy-Fixture($Value) { return ConvertFrom-Json -InputObject (ConvertTo-Json -InputObject $Value -Depth 30 -Compress) -AsHashtable -NoEnumerate }

function New-Proof {
    return @{ version = 1; phaseId = [guid]::NewGuid().ToString(); pattern = 'oneway'; clients = 2; messagesPerClient = 8193;
        countedMessages = 16386; window = 10; ackEvery = 5; finalReceipts = 2; payloadBytes = 352; sequenceBase = 1000000000;
        preparedPayloads = 8193; datasetAllocatedBytes = 1000; orderedPayloadsAndContent = $true; retries = 0 }
}

function New-FixtureOutput {
    param([object[]]$Proofs, [string]$Row = '2,16386,5001,3277.2,20,20', [switch]$Legacy)
    if ($null -eq $Proofs) { $Proofs = @((New-Proof), (New-Proof)) }
    $size = if ($Legacy) { 348 } else { 352 }
    $lines = @('Round-trip check:                  PASSED', "Serialized payload size: $size bytes", $Row, $Row)
    foreach ($proof in $Proofs) { $lines += 'VALIDATION ' + (ConvertTo-Json -InputObject $proof -Depth 30 -Compress) }
    return $lines -join "`n"
}

function Read-Fixture([string]$Text) {
    return Get-ComparisonRows $Text 2 @{ transport = 'dotnetty' } $true 10 8193 'oneway'
}

function New-RttFixture {
    $report = @{ phaseId = [guid]::NewGuid().ToString(); clients = 2; messagesPerClient = 3; samples = 6; intervalMs = 2;
        nominalRttsPerSecond = 1000; actualReleaseRttsPerSecond = 1000; releaseSpanMilliseconds = 4 }
    foreach ($item in @(@('releaseLateness', @(1, 2, 3, 4)), @('dispatchDelay', @(2, 3, 4, 5)), @('sendToEcho', @(10, 20, 30, 40)), @('scheduledToEcho', @(12, 23, 34, 45)))) {
        $values = $item[1]
        $report[$item[0]] = @{ p50Us = $values[0]; p95Us = $values[1]; p99Us = $values[2]; maxUs = $values[3] }
    }
    return $report
}

Invoke-Check 'Complete, distinct phase proofs are retained' {
    $proofs = @((New-Proof), (New-Proof))
    $rows = Read-Fixture (New-FixtureOutput -Proofs $proofs)
    Assert-True ($rows.Count -eq 2 -and $rows[0].validation.phaseId -eq $proofs[0].phaseId -and $rows[1].validation.phaseId -eq $proofs[1].phaseId)
}

Invoke-Check 'Missing, repeated and malformed proofs fail closed' {
    Assert-Rejected { Read-Fixture (New-FixtureOutput -Proofs @()) }
    Assert-Rejected { Read-Fixture (New-FixtureOutput -Proofs @((New-Proof))) }
    $proof = New-Proof
    Assert-Rejected { Read-Fixture (New-FixtureOutput -Proofs @($proof, $proof)) }
    Assert-Rejected { Read-Fixture (New-FixtureOutput -Proofs @(@{}, @{})) }
    Assert-Rejected { Read-Fixture ((New-FixtureOutput) + "`nVALIDATION []") }
    Assert-Rejected { Read-Fixture ((New-FixtureOutput) + "`nVALIDATION {invalid") }
    Assert-Rejected { Read-Fixture ((New-FixtureOutput) + "`nRTT {}") }
}

Invoke-Check 'Changed parameters and false delivery confirmation fail' {
    $changes = @{ version = 2; phaseId = 'invalid'; pattern = 'ping-pong'; clients = 3; messagesPerClient = 8192; countedMessages = 16385;
        window = 11; ackEvery = 6; finalReceipts = 1; payloadBytes = 348; sequenceBase = 0; preparedPayloads = 8192;
        datasetAllocatedBytes = 0; orderedPayloadsAndContent = $false; retries = 1 }
    foreach ($key in $changes.Keys) {
        $proofs = @((New-Proof), (New-Proof)); $proofs[1][$key] = $changes[$key]
        Assert-Rejected { Read-Fixture (New-FixtureOutput -Proofs $proofs) }
    }
}

Invoke-Check 'Missing fields, wrong JSON types and field casing fail' {
    foreach ($key in @((New-Proof).Keys)) {
        $proofs = @((New-Proof), (New-Proof)); $proofs[0].Remove($key)
        Assert-Rejected { Read-Fixture (New-FixtureOutput -Proofs $proofs) }
    }
    foreach ($badValue in @($true, '1', 1.0)) {
        $proofs = @((New-Proof), (New-Proof)); $proofs[0].version = $badValue
        Assert-Rejected { Read-Fixture (New-FixtureOutput -Proofs $proofs) }
    }
    $proofs = @((New-Proof), (New-Proof)); $proofs[0].Remove('version'); $proofs[0]['Version'] = 1
    Assert-Rejected { Read-Fixture (New-FixtureOutput -Proofs $proofs) }
}

Invoke-Check 'Legacy count mode does not require phase proofs' {
    $rows = Get-ComparisonRows (New-FixtureOutput -Proofs @() -Legacy) 2 @{ transport = 'dotnetty' }
    Assert-True ($rows.Count -eq 2 -and !$rows[0].Contains('validation'))
}

Invoke-Check 'Normal commands preserve options, lane budgets and paths containing spaces' {
    $config = @{ Clients = 2; Window = 10; Messages = 8193; Pattern = 'oneway'; ValidateSequence = $true; RttIntervalMs = 0; QueueBudget = 8192 }
    $cell = @{ transport = 'artery-tls'; lanes = 4; binary = '/a directory/benchmark.dll' }
    $command = New-ComparisonCommand $config $cell
    Assert-True ($command[0] -ceq $cell.binary -and $command -contains '--validate-sequence' -and $command -contains '2048')
    $config.ValidateSequence = $false
    Assert-True ((New-ComparisonCommand $config $cell) -notcontains '--validate-sequence')
}

Invoke-Check 'Numeric corruption and integer overflow fail' {
    foreach ($badRow in @('2,16386.5,5001,3277.2,20,20', '2,16386,5001.5,3277.2,20,20', '2,16386,5001,NaN,20,20',
        '2,16386,5001,Infinity,20,20', '2,16386,5001,-1,20,20', '2,16386,9000,3277.2,20,20',
        '2,16386,5001,3277.2,20.5,20', '2,16386,5001,3277.2,20,0', '2,9223372036854775808,5001,3277.2,20,20',
        '2,16386.00000000000000000000000001,5001,3277.2,20,20')) {
        Assert-Rejected { Read-Fixture (New-FixtureOutput -Row $badRow) }
    }
}

Invoke-Check 'Complete consistent RTT reports pass' {
    $report = New-RttFixture
    Assert-RttReport $report @{ phaseId = $report.phaseId } 2 3 2
}

Invoke-Check 'Missing and mismatched RTT fields fail' {
    $original = New-RttFixture
    foreach ($key in @($original.Keys)) {
        $report = Copy-Fixture $original; $report.Remove($key)
        Assert-Rejected { Assert-RttReport $report @{ phaseId = $original.phaseId } 2 3 2 }
    }
    foreach ($key in @('phaseId', 'clients', 'messagesPerClient', 'samples', 'intervalMs', 'nominalRttsPerSecond', 'actualReleaseRttsPerSecond', 'releaseSpanMilliseconds')) {
        $report = Copy-Fixture $original
        $report[$key] = if ($key -eq 'phaseId') { [guid]::NewGuid().ToString() } else { $report[$key] + 1 }
        Assert-Rejected { Assert-RttReport $report @{ phaseId = $original.phaseId } 2 3 2 }
    }
}

Invoke-Check 'Nonfinite, negative, unordered and inconsistent quantiles fail' {
    $original = New-RttFixture
    foreach ($group in @('releaseLateness', 'dispatchDelay', 'sendToEcho', 'scheduledToEcho')) {
        foreach ($value in @([double]::NaN, [double]::PositiveInfinity, -1, $true)) {
            $report = Copy-Fixture $original; $report[$group].p50Us = $value
            Assert-Rejected { Assert-RttReport $report @{ phaseId = $original.phaseId } 2 3 2 }
        }
    }
    $report = Copy-Fixture $original; $report.scheduledToEcho.p99Us = 22
    Assert-Rejected { Assert-RttReport $report @{ phaseId = $original.phaseId } 2 3 2 }
    $report = Copy-Fixture $original; $report.dispatchDelay = @{ p50Us = 0; p95Us = 0; p99Us = 0; maxUs = 0 }
    Assert-Rejected { Assert-RttReport $report @{ phaseId = $original.phaseId } 2 3 2 }
}

Invoke-Check 'Nonfinite and false RTT rates and spans fail' {
    $original = New-RttFixture
    foreach ($key in @('nominalRttsPerSecond', 'actualReleaseRttsPerSecond', 'releaseSpanMilliseconds')) {
        foreach ($value in @([double]::NaN, [double]::PositiveInfinity, 0, $true)) {
            $report = Copy-Fixture $original; $report[$key] = $value
            Assert-Rejected { Assert-RttReport $report @{ phaseId = $original.phaseId } 2 3 2 }
        }
    }
}

Invoke-Check 'Matched offered-rate gate rejects drift and invalid input' {
    Assert-True (Test-MatchedOfferRates @(1000, 1020))
    Assert-True (!(Test-MatchedOfferRates @(1000, 1040)))
    Assert-True (!(Test-MatchedOfferRates @()))
    Assert-True (!(Test-MatchedOfferRates @(1000, [double]::NaN)))
}

Invoke-Check 'The public parser links two RTT reports to the correct phases and interval' {
    $proofs = @((New-Proof), (New-Proof))
    $reports = foreach ($proof in $proofs) {
        $proof.pattern = 'ping-pong'; $proof.rttIntervalMs = 2; $proof.countedMessages = 32772
        $report = New-RttFixture
        $report.phaseId = $proof.phaseId; $report.messagesPerClient = 8193; $report.samples = 16386; $report.releaseSpanMilliseconds = 16384
        $report
    }
    $text = New-FixtureOutput -Proofs $proofs -Row '2,32772,10001,3277.2,20,20'
    foreach ($report in $reports) { $text += "`nRTT " + (ConvertTo-Json -InputObject $report -Depth 30 -Compress) }
    $rows = Get-ComparisonRows $text 2 @{ transport = 'dotnetty' } $true 10 8193 'ping-pong' 2
    Assert-True ($rows.Count -eq 2 -and $rows[1].rtt.phaseId -eq $proofs[1].phaseId)
    Assert-Rejected { Get-ComparisonRows $text.Substring(0, $text.LastIndexOf('RTT ')) 2 @{ transport = 'dotnetty' } $true 10 8193 'ping-pong' 2 }
    $bad = $text.Replace('"rttIntervalMs":2', '"rttIntervalMs":3')
    Assert-Rejected { Get-ComparisonRows $bad 2 @{ transport = 'dotnetty' } $true 10 8193 'ping-pong' 2 }
}

Invoke-Check 'Lane occupancy and error markers reject invalid transport results' {
    $text = New-FixtureOutput
    $occupancy = "`nRecipient lane occupancy (send): 2`nRecipient lane occupancy (reply): 2"
    $rows = Get-ComparisonRows ($text + $occupancy + $occupancy) 2 @{ transport = 'artery-tcp'; lanes = 1 } $true 10 8193 'oneway'
    Assert-True ($rows.Count -eq 2)
    Assert-Rejected { Get-ComparisonRows $text 2 @{ transport = 'artery-tcp'; lanes = 1 } $true 10 8193 'oneway' }
    Assert-Rejected { Read-Fixture ($text + "`n[ERROR] peer failed") }
    Assert-Rejected { Read-Fixture ($text + "`nSUSPECT resend") }
}

Invoke-Check 'Result parsing is independent of the current culture' {
    $originalCulture = [System.Globalization.CultureInfo]::CurrentCulture
    try {
        [System.Globalization.CultureInfo]::CurrentCulture = [System.Globalization.CultureInfo]::GetCultureInfo('fr-FR')
        Assert-True ((Read-Fixture (New-FixtureOutput))[1].milliseconds -eq 3277.2)
    }
    finally { [System.Globalization.CultureInfo]::CurrentCulture = $originalCulture }
}

Invoke-Check 'Invalid workload settings and existing output are rejected before execution' {
    $directory = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString())
    $config = @{ Binary = Join-Path $PSScriptRoot 'Compare-Transports.ps1'; Output = $directory; Clients = 2; Window = 10; Messages = 8193;
        Pattern = 'oneway'; Lanes = @(1, 4); QueueBudget = 8192; Repetitions = 2; RoundOffset = 0; MinimumSeconds = 3; TimeoutSeconds = 180;
        ValidateSequence = $true; RttIntervalMs = 0 }
    Assert-ComparisonConfig $config
    foreach ($change in @(@{ Clients = 0 }, @{ Window = 0 }, @{ Messages = 0 }, @{ Repetitions = 0 }, @{ RoundOffset = -1 },
        @{ Lanes = @(1, 1) }, @{ Lanes = @(3) }, @{ QueueBudget = 1 }, @{ TimeoutSeconds = [double]::NaN },
        @{ MinimumSeconds = [double]::PositiveInfinity }, @{ Messages = 1000001 }, @{ RttIntervalMs = 2 })) {
        $invalid = $config.Clone()
        foreach ($key in $change.Keys) { $invalid[$key] = $change[$key] }
        $rejected = $false
        try { Assert-ComparisonConfig $invalid } catch [ArgumentException] { $rejected = $true }
        Assert-True $rejected 'Invalid settings were accepted.'
    }
    [void][IO.Directory]::CreateDirectory($directory)
    try {
        $sentinel = Join-Path $directory 'existing-evidence.txt'
        [IO.File]::WriteAllText($sentinel, 'retain this evidence')
        $rejected = $false
        try { Assert-ComparisonConfig $config } catch [ArgumentException] { $rejected = $true }
        Assert-True ($rejected -and [IO.File]::ReadAllText($sentinel) -ceq 'retain this evidence')
    }
    finally { [IO.Directory]::Delete($directory, $true) }
}

Invoke-Check 'Summaries use per-run values and exclude failed attempts' {
    $directory = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString())
    [void][IO.Directory]::CreateDirectory($directory)
    try {
        $records = @(@{ cell = 'control'; status = 'valid'; measured = @{ messages_per_second = 100 } },
            @{ cell = 'control'; status = 'invalid'; measured = @{ messages_per_second = 10000 } },
            @{ cell = 'control'; status = 'valid'; measured = @{ messages_per_second = 300 } })
        Write-ComparisonSummary $directory $records
        $row = @(Import-Csv -LiteralPath (Join-Path $directory 'summary.csv'))
        Assert-True ($row.Count -eq 1 -and $row[0].samples -eq '2' -and $row[0].median_messages_sec -eq '200')
        Assert-True ($row[0].min_messages_sec -eq '100' -and $row[0].max_messages_sec -eq '300' -and $row[0].stdev_percent -eq '70.71')
        foreach ($case in @(@{ values = @(100); deviation = '0' }, @{ values = @(100, 200, 300); deviation = '50' })) {
            $points = @($case.values | ForEach-Object { @{ cell = 'control'; status = 'valid'; measured = @{ messages_per_second = $_ } } })
            Write-ComparisonSummary $directory $points
            $row = @(Import-Csv -LiteralPath (Join-Path $directory 'summary.csv'))
            Assert-True ($row.Count -eq 1 -and $row[0].samples -eq [string]$case.values.Count -and $row[0].stdev_percent -eq $case.deviation)
        }
        Write-ComparisonSummary $directory @(@{ cell = 'failure'; status = 'timeout' })
    }
    finally { [IO.Directory]::Delete($directory, $true) }
}

Invoke-Check 'A timed-out child is killed and its partial output is retained' {
    $executable = [Environment]::ProcessPath
    $arguments = @('-NoProfile', '-Command', '[Console]::Out.WriteLine("partial-output"); [Console]::Out.Flush(); Start-Sleep -Seconds 30')
    if ([IO.Path]::GetFileNameWithoutExtension($executable) -eq 'dotnet') { $arguments = @([Reflection.Assembly]::GetEntryAssembly().Location) + $arguments }
    # Sleeping is the controlled failure injected into the child, not a delay used to synchronize this test.
    $result = Invoke-ComparisonProcess $executable $arguments 5
    Assert-True ($result.TimedOut -and $result.Text.Contains('partial-output'))
}

Write-Host "All $script:PassedChecks PowerShell controller checks passed."
