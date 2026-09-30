// -----------------------------------------------------------------------
//  <copyright file="CheckAotWarnings.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

// A .NET 10 file-based app - run it with `dotnet run scripts/CheckAotWarnings.cs -- <args>`.
// No project file, no build step to maintain, and it runs on the same SDK the pipeline already
// installs.
//
// Compares the trim/AOT analysis warnings in a `dotnet publish` log against a checked-in baseline.
// Used by the "AOT canary (Linux)" job in build-system/pr-validation.yaml to keep the reflection
// surface of Akka.NET's own code from growing unnoticed.
//
// Usage:
//   dotnet run scripts/CheckAotWarnings.cs -- \
//       --log <publish log> \
//       --baseline src/aot/Akka.AOT.App/aot-warnings.baseline.txt \
//       [--repo-root <dir>] [--allow-empty]
//
// Exit codes:
//   0  every emitted in-scope warning is in the baseline. Extra baseline entries only print a
//      notice: a warning that went away is a fix, and failing the build for it would punish the
//      person who fixed it.
//   1  at least one in-scope warning is NOT in the baseline, the log measured nothing while the
//      baseline expects warnings, or the arguments/files are bad.

#:property TreatWarningsAsErrors=true
#:property Nullable=enable
#:property ImplicitUsings=enable

using System.Text.RegularExpressions;

string? logPath = null;
string? baselinePath = null;
var repoRoot = Directory.GetCurrentDirectory();
var allowEmpty = false;

for (var i = 0; i < args.Length; i++)
{
    var arg = args[i];
    switch (arg)
    {
        case "--log":
        case "--baseline":
        case "--repo-root":
            // Read the value here so a flag with nothing after it reports the real problem instead
            // of falling through and claiming the flag itself is unrecognized.
            if (i + 1 >= args.Length)
                return AotWarningCheck.Fail($"'{arg}' needs a value.{Environment.NewLine}{AotWarningCheck.Usage}");
            var value = args[++i];
            if (arg == "--log") logPath = value;
            else if (arg == "--baseline") baselinePath = value;
            else repoRoot = value;
            break;
        case "--allow-empty":
            allowEmpty = true;
            break;
        case "--help":
        case "-h":
            Console.WriteLine(AotWarningCheck.Usage);
            return 0;
        default:
            return AotWarningCheck.Fail($"unrecognized argument '{arg}'.{Environment.NewLine}{AotWarningCheck.Usage}");
    }
}

if (logPath is null || baselinePath is null)
    return AotWarningCheck.Fail($"--log and --baseline are both required.{Environment.NewLine}{AotWarningCheck.Usage}");

if (!File.Exists(logPath))
    return AotWarningCheck.Fail($"publish log not found at '{logPath}'.");

if (!File.Exists(baselinePath))
    return AotWarningCheck.Fail($"baseline not found at '{baselinePath}'.");

if (!Directory.Exists(repoRoot))
    return AotWarningCheck.Fail($"repo root not found at '{repoRoot}'.");

repoRoot = Path.GetFullPath(repoRoot);

var scan = AotWarningCheck.Scan(File.ReadAllLines(logPath), repoRoot);
var baseline = AotWarningCheck.ReadBaseline(File.ReadAllLines(baselinePath));

Console.WriteLine("AOT warning check");
Console.WriteLine($"  log              : {logPath}");
Console.WriteLine($"  baseline         : {baselinePath}");
Console.WriteLine($"  repo root        : {repoRoot}");
Console.WriteLine($"  scope            : {AotWarningCheck.ScopePrefix}");
Console.WriteLine($"  in scope         : {scan.InScope.Count} distinct warning(s)");
Console.WriteLine($"  other files      : {scan.OtherFileCount} warning(s) in files outside the scope");
Console.WriteLine($"  no source location: {scan.NoLocationCount} warning(s) ILC/ILLink reported without a file - dependencies and assembly-level diagnostics land here, and none of them can be baselined");
Console.WriteLine($"  baseline entries : {baseline.Count}");
Console.WriteLine();

var emitted = scan.InScope.Keys.ToList();

// A log that produced nothing while the baseline expects warnings means the check measured
// nothing, not that the code got clean. Three real ways that happens: a warm obj/ lets MSBuild skip
// ILC entirely, a wrong --repo-root pushes every path out of scope, and a publish without PDBs
// reports every warning with no file at all. Any of them used to print a cheerful OK.
if (baseline.Count > 0 && emitted.Count == 0 && !allowEmpty)
{
    Console.WriteLine($"FAILED: the log contains no {AotWarningCheck.ScopePrefix} warnings at all, but the baseline lists");
    Console.WriteLine($"        {baseline.Count}. That normally means this check measured nothing rather than that the");
    Console.WriteLine("        warnings are gone. Check that:");
    Console.WriteLine("          * the publish actually ran ILC/ILLink (delete bin/ and obj/ first - a warm");
    Console.WriteLine("            intermediate directory makes MSBuild skip native compilation and emit no warnings);");
    Console.WriteLine($"          * --repo-root is the repository root (got '{repoRoot}');");
    Console.WriteLine($"          * warnings in the log carry a source file (see 'no source location' above:"
                      + $" {scan.NoLocationCount}).");
    Console.WriteLine("        If the surface really is clean, empty the baseline in the same commit, or pass");
    Console.WriteLine("        --allow-empty.");
    AotWarningCheck.Annotate("error", $"AOT warning check measured no {AotWarningCheck.ScopePrefix} warnings while the baseline lists {baseline.Count}.");
    return 1;
}

var added = emitted.Where(k => !baseline.Contains(k)).Order(StringComparer.Ordinal).ToList();
var gone = baseline.Where(k => !scan.InScope.ContainsKey(k)).Order(StringComparer.Ordinal).ToList();

if (gone.Count > 0)
{
    Console.WriteLine($"NOTICE: {gone.Count} baseline entry/entries are no longer emitted. Nice - please delete");
    Console.WriteLine($"        these lines from '{baselinePath}':");
    foreach (var key in gone)
        Console.WriteLine($"          {key}");
    Console.WriteLine();
    AotWarningCheck.Annotate("warning", $"AOT warning baseline is stale: {gone.Count} entry/entries no longer emitted. Update {baselinePath}.");
}

if (added.Count > 0)
{
    Console.WriteLine($"FAILED: {added.Count} trim/AOT warning(s) under {AotWarningCheck.ScopePrefix} are not in the baseline.");
    Console.WriteLine();
    foreach (var key in added)
    {
        Console.WriteLine($"  {scan.InScope[key]}");
        Console.WriteLine($"    baseline key: {key}");
        Console.WriteLine();
        AotWarningCheck.Annotate("error", $"New AOT warning: {key}");
    }

    Console.WriteLine("Either fix the reflection site, or - if the warning is understood and accepted - add its");
    Console.WriteLine($"baseline key to '{baselinePath}' in the same PR, with a comment saying why.");
    return 1;
}

Console.WriteLine($"OK: no new trim/AOT warnings under {AotWarningCheck.ScopePrefix}.");
return 0;

/// <summary>
/// Parsing and comparison helpers for the AOT warning baseline check.
/// </summary>
internal static class AotWarningCheck
{
    /// <summary>
    /// Repo-relative path prefix a warning's file must start with to be compared against the
    /// baseline. Akka.NET's own code, and deliberately nothing else: dependency and BCL warnings
    /// are not ours to fix and they move with every package bump.
    /// </summary>
    internal const string ScopePrefix = "src/core/Akka/";

    internal const string Usage = """
        usage: dotnet run scripts/CheckAotWarnings.cs -- --log <path> --baseline <path>
                   [--repo-root <dir>] [--allow-empty]

          --log         a `dotnet publish` log, captured with `2>&1 | tee`.
          --baseline    the checked-in baseline file.
          --repo-root   repository root, used to make the compiler's absolute paths relative.
                        Defaults to the current directory.
          --allow-empty do not fail when the log has no in-scope warnings but the baseline is
                        non-empty. Only correct when the surface really did go clean.
        """;

    /// <summary>
    /// An MSBuild/ILC/ILLink diagnostic line carrying a source location, e.g.
    /// <c>/abs/src/core/Akka/Actor/Settings.cs(146): Trim analysis warning IL2057: Member(): text [/abs/app.csproj]</c>
    /// and the Roslyn analyzer's <c>Settings.cs(146,17): warning IL2057: text</c>.
    /// </summary>
    /// <remarks>
    /// Both <c>warning</c> and <c>error</c> are matched on purpose. ILC's severity depends on
    /// <c>IlcTreatWarningsAsErrors</c>, and a checker that only looked for "warning IL" would go
    /// quietly blind the moment that property flipped.
    /// </remarks>
    private static readonly Regex Diagnostic = new(
        @"^(?<file>[^(]+)\((?<line>\d+)(?:,\d+)?\):\s*.*?\b(?<severity>warning|error)\s+(?<code>IL\d{4}):\s+(?<message>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// A diagnostic with no source location, e.g. <c>ILC : Trim analysis warning IL3050: ...</c> or
    /// <c>ILLink : Trim analysis warning IL2057: ...</c>. Everything from a dependency arrives this
    /// way, because the compiler has no PDB for it, and so does every assembly-level diagnostic
    /// such as IL2104. None of them can be keyed to a file, so none can be baselined; they are
    /// counted so a log that reports *only* these cannot masquerade as a clean run.
    /// </summary>
    private static readonly Regex NoLocation = new(
        @"\b(?:warning|error)\s+IL\d{4}:",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The member that owns the warning site, which ILC and ILLink put in front of most messages.
    /// Member names never contain whitespace (parameter lists are written comma-separated with no
    /// spaces), so requiring an unbroken run of non-space characters before the colon cannot
    /// swallow prose. The Roslyn analyzer form has no such prefix.
    /// </summary>
    private static readonly Regex MemberPrefix = new(
        @"^(?<member>\S+?):\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>MSBuild's trailing <c> [/path/to/project.csproj]</c> annotation.</summary>
    private static readonly Regex ProjectSuffix = new(
        @"\s*\[[^\[\]]*\.csproj\]\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Whitespace and digits, both removed from a message to form the key's last component.
    /// Digits go because a line number or an arity in the prose must not churn the key. Whitespace
    /// goes because ILC and ILLink disagree about it for the same warning - ILC writes
    /// <c>GetType(String,Boolean)</c> where ILLink writes <c>GetType(String, Boolean)</c>.
    /// </summary>
    private static readonly Regex WhitespaceAndDigits = new(
        @"[\s\d]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>One warning, reduced to the parts the baseline key is built from.</summary>
    private sealed record Warning(string Code, string File, string Member, string Message, string Display)
    {
        /// <summary>
        /// The baseline key. The line number is deliberately absent - shifting a warning site down a
        /// few lines is not a change in the AOT surface, and keying on it would rewrite the baseline
        /// on every unrelated commit. The member and the normalized message are both present because
        /// neither alone is enough: one file holds several warnings of the same code, one member
        /// holds several warnings of the same code, and two warnings can differ only by the callee
        /// they name.
        /// </summary>
        internal string Key => $"{Code}|{File}|{Member}|{Message}";

        /// <summary>The same warning ignoring which tool reported it, used to fold the forms together.</summary>
        internal string FormKey => $"{Code}|{File}|{Message}";
    }

    internal sealed record ScanResult(
        Dictionary<string, string> InScope,
        int OtherFileCount,
        int NoLocationCount);

    /// <summary>
    /// Writes an Azure Pipelines logging command, but only when running on an agent - locally it
    /// would just be noise next to the human-readable text that is always printed.
    /// </summary>
    internal static void Annotate(string severity, string message)
    {
        if (Environment.GetEnvironmentVariable("TF_BUILD") is not null)
            Console.WriteLine($"##vso[task.logissue type={severity}]{message}");
    }

    internal static int Fail(string message)
    {
        Console.Error.WriteLine($"AOT warning check: {message}");
        Annotate("error", $"AOT warning check: {message}");
        return 1;
    }

    /// <summary>
    /// Reduces a publish log to the distinct set of in-scope warnings, keyed so that unrelated edits
    /// do not churn the baseline.
    /// </summary>
    internal static ScanResult Scan(IEnumerable<string> logLines, string repoRoot)
    {
        var found = new List<Warning>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var otherFile = 0;
        var noLocation = 0;

        foreach (var line in logLines)
        {
            var match = Diagnostic.Match(line);
            if (!match.Success)
            {
                if (NoLocation.IsMatch(line))
                    noLocation++;
                continue;
            }

            var file = ToRepoRelative(match.Groups["file"].Value, repoRoot);
            if (!file.StartsWith(ScopePrefix, StringComparison.Ordinal))
            {
                otherFile++;
                continue;
            }

            var body = ProjectSuffix.Replace(match.Groups["message"].Value, string.Empty).Trim();

            // Split the member prefix off the prose, so the two get their own key components. The
            // Roslyn analyzer form has no prefix, leaving the member empty.
            var prefix = MemberPrefix.Match(body);
            var member = prefix.Success ? prefix.Groups["member"].Value : string.Empty;
            var prose = prefix.Success ? body[prefix.Length..] : body;

            var warning = new Warning(
                match.Groups["code"].Value,
                file,
                member,
                WhitespaceAndDigits.Replace(prose, string.Empty),
                // First sighting's text, so the failure report quotes a real line number even though
                // the key does not use one.
                $"{file}({match.Groups["line"].Value}): {match.Groups["severity"].Value} {match.Groups["code"].Value}: {body}");

            if (seen.Add(warning.Key))
                found.Add(warning);
        }

        // Fold the Roslyn-analyzer form (no member prefix) into the ILC/ILLink form of the same
        // warning, so a publish log that happens to contain both does not baseline it twice.
        var withMember = found.Where(w => w.Member.Length > 0).Select(w => w.FormKey)
                              .ToHashSet(StringComparer.Ordinal);
        var kept = found.Where(w => w.Member.Length > 0 || !withMember.Contains(w.FormKey));

        var inScope = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var warning in kept)
            inScope[warning.Key] = warning.Display;

        return new ScanResult(inScope, otherFile, noLocation);
    }

    /// <summary>
    /// Turns a compiler-reported path into a repo-relative one with forward slashes. An absolute
    /// path is made relative to <paramref name="repoRoot"/>; a path already relative is taken as
    /// given. Anchoring on the repo root rather than on the first "/src/" segment matters: a
    /// checkout living under, say, ~/src/akka.net would otherwise be cut at the wrong segment and
    /// every warning would look out of scope.
    /// </summary>
    internal static string ToRepoRelative(string path, string repoRoot)
    {
        var trimmed = path.Trim();
        var relative = Path.IsPathRooted(trimmed) ? Path.GetRelativePath(repoRoot, trimmed) : trimmed;
        return relative.Replace('\\', '/');
    }

    /// <summary>Reads a baseline file, ignoring blank lines and <c>#</c> comments.</summary>
    internal static HashSet<string> ReadBaseline(IEnumerable<string> lines)
    {
        var baseline = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;
            baseline.Add(trimmed);
        }

        return baseline;
    }
}
