#requires -Version 7.2
<#
.SYNOPSIS
Runs sequential, rotated RemotePingPong TCP/TLS comparisons with equal warmup and measurement workloads.
.DESCRIPTION
Use a frozen Release build. Build, profiling and other heavy work must stay outside measured runs.
Raw logs are private research artifacts and can contain paths and certificate thumbprints.
Dot source this script to load its result-validation functions without starting a comparison.
#>
[CmdletBinding()]
param(
    [string]$Binary,
    [string]$Output,
    [ValidateSet('ping-pong', 'oneway')][string]$Pattern = 'ping-pong',
    [long]$Messages = 0,
    [int]$Clients = 16,
    [int]$Window = 50,
    [string]$Lanes = '1,2,4,8',
    [int]$QueueBudget = 8192,
    [int]$Repetitions = 5,
    [int]$RoundOffset = 0,
    [string]$Dotnet = 'dotnet',
    [double]$MinimumSeconds = 3,
    [double]$TimeoutSeconds = 180,
    [switch]$ValidateSequence,
    [int]$RttIntervalMs = 0,
    [switch]$KeepGoing
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Stop-InvalidResult([string]$Reason) {
    throw [System.IO.InvalidDataException]::new($Reason)
}

function Test-IntegerValue($Value) {
    return $Value -is [int] -or $Value -is [long]
}

function Test-FiniteNumber($Value) {
    return ($Value -is [int] -or $Value -is [long] -or $Value -is [double] -or $Value -is [decimal]) -and [double]::IsFinite([double]$Value)
}

function Assert-ExpectedFields($Actual, [hashtable]$Expected) {
    if ($Actual -isnot [System.Collections.IDictionary]) { Stop-InvalidResult 'Expected a JSON object.' }
    foreach ($key in $Expected.Keys) {
        if ($Actual.Keys -cnotcontains $key) { Stop-InvalidResult "Missing field: $key" }
        $value = $Actual[$key]
        $expectedValue = $Expected[$key]
        if (Test-IntegerValue $expectedValue) {
            if (!(Test-IntegerValue $value) -or $value -ne $expectedValue) { Stop-InvalidResult "Invalid integer field: $key" }
        }
        elseif ($expectedValue -is [bool]) {
            if ($value -isnot [bool] -or $value -ne $expectedValue) { Stop-InvalidResult "Invalid boolean field: $key" }
        }
        elseif ($value -isnot [string] -or $value -cne $expectedValue) { Stop-InvalidResult "Invalid string field: $key" }
    }
}

function Assert-RttReport($Report, $Proof, [int]$Clients, [long]$Messages, [int]$Interval) {
    Assert-ExpectedFields $Report @{ phaseId = $Proof.phaseId; clients = $Clients; messagesPerClient = $Messages; samples = $Clients * $Messages; intervalMs = $Interval }
    $nominal = $Clients * 1000.0 / $Interval
    foreach ($key in @('nominalRttsPerSecond', 'actualReleaseRttsPerSecond', 'releaseSpanMilliseconds')) {
        if ($Report.Keys -cnotcontains $key -or !(Test-FiniteNumber $Report[$key]) -or $Report[$key] -le 0) { Stop-InvalidResult "Invalid paced rate/span: $key" }
    }
    if ([math]::Abs($Report.nominalRttsPerSecond / $nominal - 1) -gt 1e-9 -or [math]::Abs($Report.actualReleaseRttsPerSecond / $nominal - 1) -gt 0.05) {
        Stop-InvalidResult 'Actual offered rate must be within 5% of the fixed nominal schedule.'
    }
    $expectedRate = $Clients * ($Messages - 1) * 1000.0 / $Report.releaseSpanMilliseconds
    if ([math]::Abs($Report.actualReleaseRttsPerSecond / $expectedRate - 1) -gt 1e-9) { Stop-InvalidResult 'Actual offered rate disagrees with release span and count.' }
    $keys = @('p50Us', 'p95Us', 'p99Us', 'maxUs')
    foreach ($group in @('releaseLateness', 'dispatchDelay', 'sendToEcho', 'scheduledToEcho')) {
        if ($Report.Keys -cnotcontains $group -or $Report[$group] -isnot [System.Collections.IDictionary]) { Stop-InvalidResult 'Paced latency distribution missing.' }
        $previous = 0.0
        foreach ($key in $keys) {
            if ($Report[$group].Keys -cnotcontains $key -or !(Test-FiniteNumber $Report[$group][$key]) -or $Report[$group][$key] -lt $previous) {
                Stop-InvalidResult 'Paced quantiles must be finite, nonnegative and ordered.'
            }
            $previous = [double]$Report[$group][$key]
        }
    }
    foreach ($key in $keys) {
        if ($Report.scheduledToEcho[$key] + 1e-6 -lt [math]::Max($Report.dispatchDelay[$key], $Report.sendToEcho[$key]) -or
            $Report.dispatchDelay[$key] + 1e-6 -lt $Report.releaseLateness[$key]) { Stop-InvalidResult 'Paced latency components contradict end-to-end latency.' }
    }
}

function ConvertFrom-ResultJson([string]$Text) {
    try { return ConvertFrom-Json -InputObject $Text -AsHashtable -NoEnumerate -ErrorAction Stop }
    catch { Stop-InvalidResult 'Malformed result JSON.' }
}

function Get-ComparisonRows {
    param([string]$Text, [int]$Clients, [hashtable]$Cell, [bool]$ValidateSequence = $false,
        [int]$Window = 0, [long]$Messages = 0, [string]$Pattern = '', [int]$RttIntervalMs = 0)
    $rows = [System.Collections.Generic.List[object]]::new()
    $proofs = [System.Collections.Generic.List[object]]::new()
    $reports = [System.Collections.Generic.List[object]]::new()
    $culture = [System.Globalization.CultureInfo]::InvariantCulture
    foreach ($line in [regex]::Split($Text, '\r?\n')) {
        if ($line -match "^\s*$Clients,") {
            $parts = $line.Split(',')
            if ($parts.Length -ne 6) { Stop-InvalidResult 'Unexpected benchmark row.' }
            $values = [long[]]::new(6)
            foreach ($index in @(0, 1, 2, 4, 5)) {
                $value = 0L
                if (![long]::TryParse($parts[$index].Trim(), [System.Globalization.NumberStyles]::Integer, $culture, [ref]$value) -or $value -le 0) {
                    Stop-InvalidResult 'Fractional, nonpositive or out-of-range integer benchmark result.'
                }
                $values[$index] = $value
            }
            $elapsed = [decimal]0
            if ($parts[3].Trim() -notmatch '^\d+(\.\d{1,2})?$' -or ![decimal]::TryParse($parts[3].Trim(), [System.Globalization.NumberStyles]::AllowDecimalPoint, $culture, [ref]$elapsed) -or $elapsed -le 0 -or $elapsed -gt [long]::MaxValue) {
                Stop-InvalidResult 'Nonfinite, nonpositive or invalid elapsed time.'
            }
            # The harness computes ceil(rate) from integer milliseconds and prints elapsed time rounded to two decimals.
            $tolerance = [decimal]::Parse('0.005', $culture)
            $minimumMs = [decimal]::Floor($elapsed - $tolerance)
            $maximumMs = [decimal]::Floor($elapsed + $tolerance)
            if ($minimumMs -lt 1) { Stop-InvalidResult 'Benchmark duration too short for rate arithmetic.' }
            $numerator = [decimal]$values[1] * 1000
            if ($values[2] -lt [decimal]::Ceiling($numerator / $maximumMs) -or $values[2] -gt [decimal]::Ceiling($numerator / $minimumMs)) { Stop-InvalidResult 'Throughput disagrees with message count and elapsed time.' }
            $rows.Add(@{ clients = $values[0]; messages = $values[1]; messages_per_second = $values[2]; milliseconds = [double]$elapsed; start_threads = $values[4]; end_threads = $values[5] })
        }
        if ($line.StartsWith('VALIDATION ', [StringComparison]::Ordinal)) { $proofs.Add((ConvertFrom-ResultJson $line.Substring(11))) }
        if ($line.StartsWith('RTT ', [StringComparison]::Ordinal)) { $reports.Add((ConvertFrom-ResultJson $line.Substring(4))) }
    }
    if ($rows.Count -ne 2) { Stop-InvalidResult 'Expected exactly one warmup and one measurement row.' }
    if (!$Text.Contains('Round-trip check:                  PASSED')) { Stop-InvalidResult 'Serializer round-trip proof missing.' }
    if ($Text.Contains('[ERROR]') -or $Text.Contains('SUSPECT') -or $Text.Contains('WARNING:')) { Stop-InvalidResult 'Error, drop or application resend invalidates row.' }
    $size = [regex]::Match($Text, 'Serialized payload size:\s+(\d+) bytes')
    $expectedSize = if ($ValidateSequence) { 352 } else { 348 }
    if (!$size.Success -or $size.Groups[1].Value -ne [string]$expectedSize) { Stop-InvalidResult 'Unexpected real V2 payload size.' }
    if ($ValidateSequence) {
        if ($proofs.Count -ne 2) { Stop-InvalidResult 'Two validation proofs are required.' }
        $phaseIds = [System.Collections.Generic.HashSet[guid]]::new()
        $expectedCount = $Clients * $Messages * $(if ($Pattern -eq 'oneway') { 1 } else { 2 })
        $expected = @{ version = 1; pattern = $Pattern; clients = $Clients; messagesPerClient = $Messages; countedMessages = $expectedCount;
            window = $Window; ackEvery = [math]::Max(1, [int][math]::Floor($Window / 2.0)); finalReceipts = $Clients; payloadBytes = 352;
            sequenceBase = 1000000000; preparedPayloads = $Messages; orderedPayloadsAndContent = $true; retries = 0 }
        if ($RttIntervalMs -ne 0) { $expected.rttIntervalMs = $RttIntervalMs }
        for ($index = 0; $index -lt 2; $index++) {
            $proof = $proofs[$index]
            if ($proof -isnot [System.Collections.IDictionary] -or $proof.Keys -cnotcontains 'phaseId' -or $proof.phaseId -isnot [string]) { Stop-InvalidResult 'Validation phase identifier missing.' }
            $id = [guid]::Empty
            if (![guid]::TryParse($proof.phaseId, [ref]$id) -or !$phaseIds.Add($id)) { Stop-InvalidResult 'Invalid or repeated validation phase.' }
            Assert-ExpectedFields $proof $expected
            if ($proof.Keys -cnotcontains 'datasetAllocatedBytes' -or !(Test-IntegerValue $proof.datasetAllocatedBytes) -or $proof.datasetAllocatedBytes -le 0) { Stop-InvalidResult 'Prepared dataset allocation evidence missing.' }
            if ($rows[$index].messages -ne $expectedCount) { Stop-InvalidResult 'Completion count differs from the validated workload.' }
            $rows[$index].validation = $proof
        }
    }
    if ($RttIntervalMs -eq 0 -and $reports.Count -gt 0) { Stop-InvalidResult 'Unexpected paced results in an unpaced run.' }
    if ($RttIntervalMs -ne 0) {
        if (!$ValidateSequence -or $reports.Count -ne 2) { Stop-InvalidResult 'Paced results require two ordered validation proofs.' }
        for ($index = 0; $index -lt 2; $index++) {
            Assert-RttReport $reports[$index] $rows[$index].validation $Clients $Messages $RttIntervalMs
            $rows[$index].rtt = $reports[$index]
        }
    }
    if ($Cell.transport -ne 'dotnetty') {
        foreach ($direction in @('send', 'reply')) {
            $occupancy = [regex]::Matches($Text, "Recipient lane occupancy \($direction\):\s+([0-9,]+)")
            if ($occupancy.Count -ne 2) { Stop-InvalidResult "Missing $direction recipient lane occupancy." }
            for ($index = 0; $index -lt 2; $index++) {
                $counts = @($occupancy[$index].Groups[1].Value.Split(',') | ForEach-Object { [int]$_ })
                if ($counts.Count -ne $Cell.lanes -or @($counts | Where-Object { $_ -le 0 }).Count -gt 0 -or ($counts | Measure-Object -Sum).Sum -ne $Clients) { Stop-InvalidResult "$direction workload did not occupy every configured lane." }
                $rows[$index][$direction + '_lane_recipients'] = $counts
            }
        }
    }
    return ,$rows.ToArray()
}

function Test-MatchedOfferRates([double[]]$Rates) {
    if ($Rates.Count -eq 0 -or @($Rates | Where-Object { ![double]::IsFinite($_) -or $_ -le 0 }).Count -gt 0) { return $false }
    $range = $Rates | Measure-Object -Minimum -Maximum
    return $range.Maximum / $range.Minimum -le 1.03
}

function New-ComparisonCommand([hashtable]$Config, [hashtable]$Cell) {
    $arguments = @($Cell.binary, 'run', '--transport', $Cell.transport, '--payload', 'real', '--serializer', 'v2',
        '--clients', [string]$Config.Clients, '--window', [string]$Config.Window, '--msgs', [string]$Config.Messages, '--times', '2')
    if ($Config.Pattern -eq 'oneway') { $arguments += '--oneway' }
    if ($Config.ValidateSequence) { $arguments += '--validate-sequence' }
    if ($Config.RttIntervalMs -ne 0) { $arguments += @('--rtt-interval-ms', [string]$Config.RttIntervalMs) }
    if ($Cell.transport -ne 'dotnetty') { $arguments += @('--inbound-lanes', [string]$Cell.lanes, '--outbound-lanes', [string]$Cell.lanes, '--qsize', [string]($Config.QueueBudget / $Cell.lanes)) }
    return ,$arguments
}

function Invoke-ComparisonProcess([string]$FileName, [string[]]$Arguments, [double]$TimeoutSeconds) {
    $start = [System.Diagnostics.ProcessStartInfo]::new($FileName)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $start
    $started = $false
    try {
        $started = $process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $timedOut = !$process.WaitForExit([int][math]::Ceiling($TimeoutSeconds * 1000))
        if ($timedOut) {
            $process.Kill($true)
            if (!$process.WaitForExit(5000)) { throw 'Timed-out benchmark process did not terminate.' }
        }
        return @{ ExitCode = $process.ExitCode; TimedOut = $timedOut; Text = $stdout.GetAwaiter().GetResult() + "`n" + $stderr.GetAwaiter().GetResult() }
    }
    finally {
        # Disposal alone does not terminate an owned child if the controller is interrupted.
        if ($started -and !$process.HasExited) {
            $process.Kill($true)
            if (!$process.WaitForExit(5000)) { throw 'Benchmark child did not terminate during controller cleanup.' }
        }
        $process.Dispose()
    }
}

function Get-Median([double[]]$Values) {
    $sorted = @($Values | Sort-Object)
    $middle = [int][math]::Floor($sorted.Count / 2.0)
    if ($sorted.Count % 2) { return $sorted[$middle] }
    return ($sorted[$middle - 1] + $sorted[$middle]) / 2.0
}

function Write-ComparisonSummary([string]$Directory, [object[]]$Records) {
    $valid = @($Records | Where-Object status -EQ 'valid')
    $throughput = @(foreach ($group in ($valid | Group-Object cell | Sort-Object Name)) {
        $values = @($group.Group | ForEach-Object { [double]$_.measured.messages_per_second })
        $stats = $values | Measure-Object -AllStats
        $deviation = if ($values.Count -gt 1) { $stats.StandardDeviation / $stats.Average * 100 } else { 0.0 }
        [pscustomobject][ordered]@{ cell = $group.Name; samples = $values.Count; median_messages_sec = [math]::Round((Get-Median $values));
            min_messages_sec = $stats.Minimum; max_messages_sec = $stats.Maximum; stdev_percent = [math]::Round($deviation, 2) }
    })
    if ($throughput.Count -gt 0) { $throughput | Export-Csv -LiteralPath (Join-Path $Directory 'summary.csv') -NoTypeInformation -Encoding utf8NoBOM }
    $paced = @($valid | Where-Object { $_.measured.Contains('rtt') })
    $latency = @(foreach ($group in ($paced | Group-Object cell | Sort-Object Name)) {
        $reports = @($group.Group | ForEach-Object { $_.measured.rtt })
        $p99 = @($reports | ForEach-Object { $_.scheduledToEcho.p99Us })
        [pscustomobject][ordered]@{ cell = $group.Name; runs = $reports.Count; median_actual_offered_rtts_sec = Get-Median @($reports.actualReleaseRttsPerSecond);
            median_run_p50_us = Get-Median @($reports | ForEach-Object { $_.scheduledToEcho.p50Us }); median_run_p99_us = Get-Median $p99;
            min_run_p99_us = ($p99 | Measure-Object -Minimum).Minimum; max_run_p99_us = ($p99 | Measure-Object -Maximum).Maximum;
            median_run_release_p99_us = Get-Median @($reports | ForEach-Object { $_.releaseLateness.p99Us }) }
    })
    if ($latency.Count -gt 0) { $latency | Export-Csv -LiteralPath (Join-Path $Directory 'rtt-summary.csv') -NoTypeInformation -Encoding utf8NoBOM }
}

function Invoke-CheckedCommand([string]$FileName, [string[]]$Arguments) {
    $text = & $FileName @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "$FileName exited $LASTEXITCODE" }
    return $text -join "`n"
}

function Assert-ComparisonConfig([hashtable]$Config) {
    if ($Config.Messages -le 0 -or $Config.Clients -le 0 -or $Config.Window -le 0 -or $Config.Repetitions -le 0 -or $Config.RoundOffset -lt 0 -or $Config.Lanes.Count -eq 0) { throw [ArgumentException]::new('Workload counts must be positive and round offset nonnegative.') }
    if (@($Config.Lanes | Where-Object { $_ -le 0 }).Count -gt 0 -or @($Config.Lanes | Select-Object -Unique).Count -ne $Config.Lanes.Count -or
        @($Config.Lanes | Where-Object { $Config.QueueBudget -lt $_ -or $Config.QueueBudget % $_ -ne 0 }).Count -gt 0) { throw [ArgumentException]::new('Lane counts must be unique, positive and divide the queue budget.') }
    if (!(Test-FiniteNumber $Config.MinimumSeconds) -or $Config.MinimumSeconds -le 0 -or !(Test-FiniteNumber $Config.TimeoutSeconds) -or $Config.TimeoutSeconds -le 0 -or $Config.TimeoutSeconds * 1000 -gt [int]::MaxValue) { throw [ArgumentException]::new('Invalid measurement or process timeout bounds.') }
    if ([decimal]$Config.Clients * $Config.Messages * 2 -gt [long]::MaxValue) { throw [ArgumentException]::new('Workload count exceeds the supported result range.') }
    if ($Config.ValidateSequence -and $Config.Messages -gt 1000000) { throw [ArgumentException]::new('Validation supports at most 1000000 messages per client.') }
    if ($Config.RttIntervalMs -ne 0 -and (!$Config.ValidateSequence -or $Config.Pattern -ne 'ping-pong' -or $Config.RttIntervalMs -lt 1 -or $Config.RttIntervalMs -gt 1000 -or $Config.Messages -lt 2 -or $Config.Messages * $Config.Clients -gt 1000000)) { throw [ArgumentException]::new('Paced latency requires validated ping-pong, interval 1–1000 ms, at least two bursts and at most 1000000 samples.') }
    if (!(Test-Path -LiteralPath $Config.Binary -PathType Leaf)) { throw [ArgumentException]::new('Benchmark binary does not exist.') }
    if (Test-Path -LiteralPath $Config.Output) { throw [ArgumentException]::new('Output directory already exists; preserve earlier evidence and choose another path.') }
}

function Invoke-TransportComparison([hashtable]$Config) {
    Assert-ComparisonConfig $Config
    $repository = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
    $revision = Invoke-CheckedCommand 'git' @('-C', $repository, 'rev-parse', 'HEAD')
    $groups = @()
    $groups += ,@(@{ name = 'dotnetty'; revision = $revision; binary = $Config.Binary; transport = 'dotnetty'; lanes = 1 })
    foreach ($lane in $Config.Lanes) { $groups += ,@('artery-tcp', 'artery-tls' | ForEach-Object { @{ name = "$_-l$lane"; revision = $revision; binary = $Config.Binary; transport = $_; lanes = $lane } }) }
    foreach ($group in $groups) {
        foreach ($cell in $group) {
            $cell.binary_sha256 = (Get-FileHash -LiteralPath $cell.binary -Algorithm SHA256).Hash.ToLowerInvariant()
            $cell.akka_dependencies_sha256 = [ordered]@{}
            foreach ($dependency in (Get-ChildItem -LiteralPath (Split-Path $cell.binary) -Filter 'Akka*.dll' | Sort-Object Name)) { $cell.akka_dependencies_sha256[$dependency.Name] = (Get-FileHash -LiteralPath $dependency.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
        }
    }
    $sourceDiff = Invoke-CheckedCommand 'git' @('-C', $repository, 'diff', 'HEAD', '--', 'src/core', 'src/benchmark/RemotePingPong')
    $manifest = @{ arguments = $Config; driver_revision = $revision; driver_sha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash.ToLowerInvariant();
        source_diff_sha256 = $(if ($sourceDiff) { [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($sourceDiff))).ToLowerInvariant() } else { $null });
        system = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription; machine = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString();
        logical_cpus = [Environment]::ProcessorCount; affinity = $null; started_utc = [DateTime]::UtcNow.ToString('O');
        dotnet_info = Invoke-CheckedCommand $Config.Dotnet @('--info'); cells = $groups;
        interpretation = $(if ($Config.RttIntervalMs) { 'Scheduled offered-load latency; message rate is capped, not maximum throughput.' } else { 'Loopback throughput only.' }) + ' One same-sized warmup before every measurement. Native versions and effective endpoint runtime need separate verification.' }
    # Linux exposes the complete allowed CPU list without narrowing it to Process.ProcessorAffinity's pointer-sized mask.
    if ($IsLinux -and (Test-Path -LiteralPath '/proc/self/status')) { $allowed = Select-String -LiteralPath '/proc/self/status' -Pattern '^Cpus_allowed_list:\s+(.+)$'; if ($allowed) { $manifest.affinity = $allowed.Matches[0].Groups[1].Value } }
    # Do not silently reuse a directory created after parameter validation.
    [void](New-Item -ItemType Directory -Path $Config.Output -ErrorAction Stop)
    $manifest | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $Config.Output 'manifest.json') -Encoding utf8NoBOM
    $records = [System.Collections.Generic.List[object]]::new()
    $invalid = $false
    for ($repetition = $Config.RoundOffset; $repetition -lt $Config.RoundOffset + $Config.Repetitions; $repetition++) {
        for ($groupIndex = 0; $groupIndex -lt $groups.Count; $groupIndex++) {
            $group = $groups[$groupIndex]
            $offset = ($repetition + $groupIndex) % $group.Count
            for ($index = 0; $index -lt $group.Count; $index++) {
                $cell = $group[($offset + $index) % $group.Count]
                $arguments = New-ComparisonCommand $Config $cell
                $logName = "r$($repetition + 1)-$($cell.name).log"
                $record = @{ cell = $cell.name; revision = $revision; repetition = $repetition + 1; transport = $cell.transport; lanes = $cell.lanes; inbound_lanes = $cell.lanes;
                    command = @($Config.Dotnet) + $arguments; log = $logName; status = 'invalid' }
                try {
                    $result = Invoke-ComparisonProcess $Config.Dotnet $arguments $Config.TimeoutSeconds
                    [IO.File]::WriteAllText((Join-Path $Config.Output $logName), $result.Text)
                    $record.exit = $result.ExitCode
                    if ($result.TimedOut) { $record.status = 'timeout'; throw 'Process timeout.' }
                    if ($result.ExitCode -ne 0) { throw "Benchmark process exited $($result.ExitCode)." }
                    $rows = Get-ComparisonRows $result.Text $Config.Clients $cell $Config.ValidateSequence $Config.Window $Config.Messages $Config.Pattern $Config.RttIntervalMs
                    $expected = $Config.Clients * $Config.Messages * $(if ($Config.Pattern -eq 'oneway') { 1 } else { 2 })
                    if ($rows[1].messages -ne $expected) { Stop-InvalidResult 'Completion count differs from workload.' }
                    $record.warmup = $rows[0]; $record.measured = $rows[1]
                    if ($rows[1].milliseconds -lt $Config.MinimumSeconds * 1000) { Stop-InvalidResult 'Measurement too short; preserve evidence and rerun the matched matrix with more messages.' }
                    $record.status = 'valid'
                }
                catch { $record.error = $_.Exception.Message }
                $records.Add($record)
                $record | ConvertTo-Json -Depth 30 -Compress | Add-Content -LiteralPath (Join-Path $Config.Output 'runs.jsonl') -Encoding utf8NoBOM
                Write-ComparisonSummary $Config.Output $records.ToArray()
                Write-Host ($record | ConvertTo-Json -Depth 30 -Compress)
                if ($record.status -ne 'valid') { $invalid = $true; if (!$Config.KeepGoing) { return 1 } }
            }
        }
    }
    if ($Config.RttIntervalMs -ne 0) {
        $comparisons = foreach ($repetition in (($Config.RoundOffset + 1)..($Config.RoundOffset + $Config.Repetitions))) {
            $rates = @($records | Where-Object { $_.repetition -eq $repetition -and $_.status -eq 'valid' } | ForEach-Object { $_.measured.rtt.actualReleaseRttsPerSecond })
            $complete = $rates.Count -eq ($groups | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum
            $matched = $complete -and (Test-MatchedOfferRates $rates)
            if (!$matched) { $invalid = $true }
            @{ repetition = $repetition; complete = $complete; matched = $matched; actual_rates = $rates }
        }
        ConvertTo-Json -InputObject @($comparisons) -Depth 20 | Set-Content -LiteralPath (Join-Path $Config.Output 'rtt-rate-comparisons.json') -Encoding utf8NoBOM
    }
    if ($invalid) { return 1 }
    return 0
}

if ($MyInvocation.InvocationName -ne '.') {
    try {
        $laneCounts = @($Lanes.Split(',') | ForEach-Object { [int]::Parse($_, [System.Globalization.CultureInfo]::InvariantCulture) })
        $config = @{ Binary = [IO.Path]::GetFullPath($Binary); Output = [IO.Path]::GetFullPath($Output); Pattern = $Pattern.ToLowerInvariant(); Messages = $Messages;
            Clients = $Clients; Window = $Window; Lanes = $laneCounts; QueueBudget = $QueueBudget; Repetitions = $Repetitions; RoundOffset = $RoundOffset;
            Dotnet = $Dotnet; MinimumSeconds = $MinimumSeconds; TimeoutSeconds = $TimeoutSeconds; ValidateSequence = [bool]$ValidateSequence; RttIntervalMs = $RttIntervalMs; KeepGoing = [bool]$KeepGoing }
        $code = Invoke-TransportComparison $config
        exit $code
    }
    catch { Write-Error $_ -ErrorAction Continue; exit 1 }
}
