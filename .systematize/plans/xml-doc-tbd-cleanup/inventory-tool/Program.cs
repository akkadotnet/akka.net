using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

#nullable enable

var repo = args[0];
var output = args[1];
var git = Process.Start(new ProcessStartInfo("git", "ls-files -z") { WorkingDirectory = repo, RedirectStandardOutput = true })!;
var paths = git.StandardOutput.ReadToEnd().Split('\0', StringSplitOptions.RemoveEmptyEntries);
git.WaitForExit();
var records = new List<object>();
var outside = new List<object>();
var pattern = new Regex(@"\bTBD\b", RegexOptions.IgnoreCase);
foreach (var path in paths.Where(p => p.StartsWith("src/") && (p.EndsWith(".cs") || p.EndsWith(".tt"))))
{
    var source = File.ReadAllText(Path.Combine(repo, path));
    if (!pattern.IsMatch(source)) continue;
    var tree = path.EndsWith(".cs") ? CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.Parse)) : null;
    var root = tree?.GetRoot();
    var docs = root?.DescendantTrivia(descendIntoTrivia: false)
        .Where(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
        .ToArray() ?? [];
    var lines = source.Split('\n');
    var covered = new HashSet<int>();
    for (var i = 0; i < lines.Length; i++)
    {
        if (!Regex.IsMatch(lines[i], @"^\s*///")) continue;
        var start = i;
        while (i + 1 < lines.Length && Regex.IsMatch(lines[i + 1], @"^\s*///")) i++;
        var block = string.Join("\n", lines[start..(i + 1)]);
        var count = pattern.Matches(block).Count;
        if (count == 0) continue;
        for (var k = start; k <= i; k++) covered.Add(k);
        var trivia = docs.FirstOrDefault(t => tree!.GetLineSpan(t.Span).StartLinePosition.Line == start);
        var declaration = trivia.Token.Parent?.AncestorsAndSelf().OfType<MemberDeclarationSyntax>().FirstOrDefault();
        var signature = declaration is null ? "" : declaration.ToString().Split('\n')[0].Trim();
        var ancestorTypes = declaration?.Ancestors().OfType<BaseTypeDeclarationSyntax>().Select(t => t.Identifier.ValueText).Reverse().ToArray() ?? [];
        var modifiers = declaration?.ChildTokens().Where(t => t.IsKeyword()).Select(t => t.ValueText).ToArray() ?? [];
        bool Visible(MemberDeclarationSyntax node)
        {
            var mods = node.ChildTokens().Select(t => t.Kind()).ToArray();
            if (mods.Contains(SyntaxKind.PrivateKeyword) || mods.Contains(SyntaxKind.InternalKeyword)) return mods.Contains(SyntaxKind.ProtectedKeyword) && !mods.Contains(SyntaxKind.PrivateKeyword);
            if (mods.Contains(SyntaxKind.PublicKeyword) || mods.Contains(SyntaxKind.ProtectedKeyword)) return true;
            if (node is EnumMemberDeclarationSyntax || node.Parent is InterfaceDeclarationSyntax) return true;
            return false;
        }
        var visibility = declaration is null ? "unresolved" : Visible(declaration) && declaration.Ancestors().OfType<BaseTypeDeclarationSyntax>().All(Visible) ? "externally-visible-syntax" : "nonpublic-syntax";
        var tags = new List<string>();
        foreach (Match m in Regex.Matches(block, @"<(summary|remarks|param|typeparam|returns|value|exception|example)\b[^>]*>([\s\S]*?)</\1>"))
            if (pattern.IsMatch(m.Groups[2].Value)) tags.Add(m.Groups[1].Value);
        var plain = Regex.Replace(Regex.Replace(block, @"^\s*/// ?", "", RegexOptions.Multiline), @"<[^>]*>", "");
        plain = pattern.Replace(plain, "");
        var usefulText = Regex.IsMatch(plain, @"[a-zA-Z]{3}");
        var inheritanceCandidate = modifiers.Contains("override") || declaration is MethodDeclarationSyntax method && method.ExplicitInterfaceSpecifier is not null || block.Contains("<inheritdoc");
        var category = path.EndsWith(".tt") || path.Contains("/CodeGen/") ? "generated-or-template" : inheritanceCandidate ? "inheritance-candidate-unverified" : usefulText ? "partial-documentation-needs-review" : "placeholder-only-needs-contract-review";
        var parts = path.Split('/');
        var module = parts.Length > 2 && parts[1] == "core" ? string.Join('/', parts.Take(3)) : parts.Length > 3 && parts[1] == "contrib" ? string.Join('/', parts.Take(4)) : string.Join('/', parts.Take(Math.Min(3, parts.Length)));
        records.Add(new { path, line = start + 1, endLine = i + 1, occurrences = count, module, containingTypes = ancestorTypes, declaration = signature, visibility, modifiers, tags = tags.Distinct().ToArray(), category, reviewStatus = "unreviewed", documentation = block });
    }
    for (var i = 0; i < lines.Length; i++)
        if (!covered.Contains(i) && pattern.IsMatch(lines[i]))
            outside.Add(new { path, line = i + 1, occurrences = pattern.Matches(lines[i]).Count, text = lines[i].Trim(), reviewStatus = "non-triple-slash-needs-review" });
}
Directory.CreateDirectory(output);
File.WriteAllText(Path.Combine(output, "inventory.json"), JsonSerializer.Serialize(new { records, outside }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Recorded {records.Count} XML comment blocks; {outside.Count} other matching lines. Output: {output}");
