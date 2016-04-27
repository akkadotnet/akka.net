//-----------------------------------------------------------------------
// <copyright file="CoreAPISpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using ApiApprover;
using ApprovalTests;
using Mono.Cecil;
using Xunit;

namespace Akka.API.Tests
{
    public class CoreAPISpec
    {
        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ApproveCore()
        {
            var assemblyPath = Path.GetFullPath(typeof(PatternMatch).Assembly.Location);
            var asm = AssemblyDefinition.ReadAssembly(assemblyPath);
            var publicApi = Filter(PublicApiGenerator.CreatePublicApiForAssembly(asm));
            Approvals.Verify(publicApi);
        }

        static string Filter(string text)
        {
            return string.Join(Environment.NewLine, text.Split(new[]
            {
                Environment.NewLine
            }, StringSplitOptions.RemoveEmptyEntries)
                .Where(l => !l.StartsWith("[assembly: ReleaseDateAttribute("))
                .Where(l => !string.IsNullOrWhiteSpace(l))
                );
        }

    }
}
