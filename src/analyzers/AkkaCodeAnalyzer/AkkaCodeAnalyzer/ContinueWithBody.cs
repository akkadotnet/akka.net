using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.FindSymbols;

namespace RoslynWorkspace
{
    class ContinueWithBody : Rewriter
    {
        public override async Task<Document> Rewrite(Document document)
        {
            var root = await document.GetSyntaxRootAsync();

            var editor = await DocumentEditor.CreateAsync(document);
            var semanticModel = await document.GetSemanticModelAsync();

            var classes = root
                .DescendantNodes(node => true)
                .OfType<ClassDeclarationSyntax>()
                .ToList();


            foreach (var @class in classes)
            {
                var semanticClass = semanticModel.GetDeclaredSymbol(@class) as ITypeSymbol;

                var current = semanticClass;
                while (current.BaseType != null && current.BaseType.SpecialType == SpecialType.None)
                {
                    current = current.BaseType;
                }

                if (current.BaseType == null)
                    continue;
               
                if (current.BaseType.SpecialType != SpecialType.System_Object)
                    continue;

                //ActorBase
                if (current.Name != "ActorBase")
                    continue;

                var invocations = @class
                    .DescendantNodes(node => true)
                    .OfType<InvocationExpressionSyntax>()
                    .Where(i =>
                    {
                        var memberAccess = i.Expression as MemberAccessExpressionSyntax;
                        if (memberAccess != null && memberAccess.Name.Identifier.Text == "ContinueWith")
                            return true;
                        return false;
                    })
                    .ToList();

                if (invocations.Any())
                {
                    foreach (var invocation in invocations)
                    {
                        var args = invocation.ArgumentList.Arguments.ToList();
                        var body = args.First();
                        var options = args.Last();
                        var expression = invocation.Expression as MemberAccessExpressionSyntax;
                        if (body.Expression is SimpleLambdaExpressionSyntax)
                        {
                            var lambdaBody = body.Expression as SimpleLambdaExpressionSyntax;
                            //we are in a ContinueWith lambda body
                            //lets see if we find any violations

                            var violations = body
                                .DescendantNodes(node => true)
                                .OfType<IdentifierNameSyntax>()
                                .Where(i => i.Identifier.Text == "Context" || i.Identifier.Text == "Self" || i.Identifier.Text == "Sender")
                                .Select(i => i.Parent)
                                .ToList();

                            const string comment = "/*TODO: this needs to be closed over*/";
                            foreach (var violation in violations)
                            {
                                if (violation.GetLeadingTrivia().ToString().Contains(comment))
                                {
                                    //already patched
                                    continue;
                                }
                                var newTrivia = SyntaxFactory.ParseLeadingTrivia(comment);
                                var newViolation = violation.WithLeadingTrivia(violation.GetLeadingTrivia().Union(newTrivia));
                                editor.ReplaceNode(violation, newViolation);
                            }
                        }
                        else
                        {

                        }

                    }
                }
            }

            return editor.GetChangedDocument();
        }
    }
}
