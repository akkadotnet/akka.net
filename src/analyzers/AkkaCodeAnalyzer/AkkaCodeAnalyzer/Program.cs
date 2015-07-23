using System;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using System.Linq;

namespace RoslynWorkspace
{
    internal class Program
    {

        private static readonly Rewriter[] rewriters =
        {
            new ContinueWithBody(),
            new ClassReferenceCounter()
        };

        private static void Main(string[] args)
        {
            const string solutionPath = @"../../../../../akka.sln";
            ProcessSolution(solutionPath)
                .ContinueWith(t =>
                {
                    Console.WriteLine(t.Status);
                });
            Console.ReadLine();
        }

        private static async Task ProcessSolution(string solutionPath)
        {

            var msWorkspace = MSBuildWorkspace.Create();
            //You must install the MSBuild Tools or this line will throw an exception:
            var solution = await msWorkspace.OpenSolutionAsync(solutionPath);
            var projectCount = solution.Projects.Count();
            for (var i = 0;i< projectCount;i++)
            {
                var project = msWorkspace.CurrentSolution.Projects.Skip(i).First();
                var documentCount = project.Documents.Count();
                for (int j=0;j < documentCount;j++)
                {
                    var document = msWorkspace.CurrentSolution.Projects.Skip(i).First().Documents.Skip(j).First();

                    if (document.SourceCodeKind != SourceCodeKind.Regular)
                        continue;

                    if (document.FilePath.Contains(".designer."))
                        continue;

                    Console.WriteLine("Processing {0}", document.FilePath);

                    var doc = document;
                    foreach (var rewriter in rewriters)
                    {
                        doc = await rewriter.Rewrite(doc);
                    }

                    if (doc != document)
                    {
                        Console.WriteLine("changed {0}",doc.Name);
                        //save result
                        msWorkspace.TryApplyChanges(doc.Project.Solution);
                    }                    
                }
            }                        
        }
    }

    public abstract class Rewriter
    {
        public abstract Task<Document> Rewrite(Document document);
    }
}
