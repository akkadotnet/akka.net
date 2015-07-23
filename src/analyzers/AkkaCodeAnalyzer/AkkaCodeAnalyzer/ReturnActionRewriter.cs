//using System.Collections.Generic;
//using System.Linq;
//using System.Threading.Tasks;
//using Microsoft.CodeAnalysis;
//using Microsoft.CodeAnalysis.CSharp.Syntax;
//using Microsoft.CodeAnalysis.Editing;

//namespace RoslynWorkspace
//{
//    class ReturnActionRewriter : Rewriter
//    {
//        public override async Task<Document> Rewrite(Document document)
//        {
//            var root = await document.GetSyntaxRootAsync();

//            var editor = await DocumentEditor.CreateAsync(document);

//            var methods = root
//                .DescendantNodes(node => true)
//                .OfType<MethodDeclarationSyntax>()
//                .ToList();

//            foreach (var method in methods)
//            {
//                var returnActions = method
//                    .DescendantNodes(node => true)
//                    .OfType<BinaryExpressionSyntax>()
//                    .Where(node => node.OperatorToken.ValueText == "==")
//                    .Where(node => node.Right.ToString() == "\"#exit#\"" || node.Right.ToString() == "\"#break#\"")
//                    .Select(node => node.Parent as IfStatementSyntax)
//                    .ToList();

//                if (returnActions.Count > 0)
//                {
//                    foreach (var ifStatement in returnActions)
//                    {
//                        var mainCall = ifStatement.GetPrevious() as ExpressionStatementSyntax;
//                        var newIfStatement = ifStatement.WithCondition(mainCall.Expression.WithoutTrivia());

//                        editor.RemoveNode(mainCall);
//                        editor.ReplaceNode(ifStatement,newIfStatement);
//                    }
//                }
//            }
            
//            var newDocument = editor.GetChangedDocument();
//            return newDocument;
//        }
//    }

//    public static class SyntaxExt
//    {
//        public static StatementSyntax GetPrevious(this StatementSyntax self)
//        {
//            var parent = self.Parent as BlockSyntax;
//            var index = parent.Statements.IndexOf(self);
//            return parent.Statements[index - 1];
//        }
//    }
//}
