//using System.Linq;
//using System.Threading.Tasks;
//using Microsoft.CodeAnalysis;
//using Microsoft.CodeAnalysis.CSharp;
//using Microsoft.CodeAnalysis.CSharp.Syntax;

//namespace RoslynWorkspace
//{
//    public class SwitchIfRewriter : Rewriter
//    {

//        public override async Task<Document> Rewrite(Document document)
//        {
//            var visitor = new CodeRewriter();
//            var root = await document.GetSyntaxRootAsync();
//            var newRoot = visitor.Visit(root);
//            var newDocument = document.WithSyntaxRoot(newRoot);
//            return newDocument;
//        }

//        public class CodeRewriter : CSharpSyntaxRewriter
//        {
//            public override SyntaxNode VisitSwitchStatement(SwitchStatementSyntax node)
//            {
//                var leadingTrivia = SyntaxFactory.ParseLeadingTrivia("").Add(node.GetLeadingTrivia().Last());
//                var tabTrivia = SyntaxFactory.ParseLeadingTrivia("\t");
//                var crlfTrivia = SyntaxFactory.ParseLeadingTrivia("\r\n");

//                var sections = node.Sections;

//                //empty switch
//                if (sections.Count == 0)
//                    return null;

//                var section = sections.First();

//                //switch with multiple cases or labels
//                if (sections.Count != 1 || section.Labels.Count > 1)
//                    return base.VisitSwitchStatement(node);

//                //switch with 1 case with 1 label
//                var statements = section
//                    .Statements
//                    .Where(s => !(s is BreakStatementSyntax)).ToList();

//                var statementsAndTriva = statements
//                    .Select((s, i) =>
//                    {
//                        var s1 =
//                            s.WithoutTrivia().WithLeadingTrivia(crlfTrivia.AddRange(leadingTrivia.AddRange(tabTrivia)));

//                        //Last statement should have trailing trivia to place `}` correctly
//                        if (i == statements.Count - 1)
//                        {
//                            s1 = s1.WithTrailingTrivia(crlfTrivia.AddRange(leadingTrivia));
//                        }
//                        return s1;
//                    })
//                    .ToList();

//                var statement = SyntaxFactory.Block(statementsAndTriva)
//                    .WithLeadingTrivia(crlfTrivia.AddRange(leadingTrivia))
//                    .WithTrailingTrivia(crlfTrivia);

//                var label = (CaseSwitchLabelSyntax)section.Labels.First();

//                var expression = SyntaxFactory.BinaryExpression(SyntaxKind.EqualsExpression, node.Expression,
//                    label.Value);

//                var ifStatement = SyntaxFactory.IfStatement(expression, statement)
//                    .WithLeadingTrivia(node.GetLeadingTrivia());

//                var breakRemover = new BreakRemover();

//                var result = breakRemover.Visit(ifStatement);

//                return result;
//            }
//        }

//        public class BreakRemover : CSharpSyntaxRewriter
//        {
//            public override SyntaxNode VisitBreakStatement(BreakStatementSyntax node)
//            {
//                // should add further check to make sure break statement is in
//                // switch, the idea is find closest ancestor must be switch (not
//                // for, foreach, while, dowhile)
//                return SyntaxFactory.EmptyStatement();
//            }
//        }
//    }
//}
