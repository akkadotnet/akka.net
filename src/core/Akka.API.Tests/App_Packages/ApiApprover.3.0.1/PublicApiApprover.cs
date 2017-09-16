//-----------------------------------------------------------------------
// <copyright file="PublicApiApprover.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.IO;
using ApprovalTests;
using ApprovalTests.Namers;
using Mono.Cecil;

namespace ApiApprover
{
    public static class PublicApiApprover
    {
        public static void ApprovePublicApi(string assemblyPath)
        {
            var assemblyResolver = new DefaultAssemblyResolver();
            assemblyResolver.AddSearchDirectory(Path.GetDirectoryName(assemblyPath));

            var readSymbols = File.Exists(Path.ChangeExtension(assemblyPath, ".pdb"));
            var asm = AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters(ReadingMode.Deferred)
            {
                ReadSymbols = readSymbols,
                AssemblyResolver = assemblyResolver,
            });

            var publicApi = PublicApiGenerator.CreatePublicApiForAssembly(asm);
            var writer = new ApprovalTextWriter(publicApi, "cs");
            var approvalNamer = new AssemblyPathNamer(assemblyPath);
            ApprovalTests.Approvals.Verify(writer, approvalNamer, ApprovalTests.Approvals.GetReporter());
        }

        private class AssemblyPathNamer : UnitTestFrameworkNamer
        {
            private readonly string name;

            public AssemblyPathNamer(string assemblyPath)
            {
                name = Path.GetFileNameWithoutExtension(assemblyPath);
            }

            public override string Name
            {
                get { return name; }
            }
        }
    }
}