using System;
using System.Reflection;
using Akka.MultiNode.TestAdapter.Internal;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Akka.MultiNode.TestAdapter
{
    public class MultiNodeTestFramework : TestFramework
    {
        public MultiNodeTestFramework(IMessageSink diagnosticMessageSink) : base(diagnosticMessageSink)
        {
        }

        protected override ITestFrameworkDiscoverer CreateDiscoverer(IAssemblyInfo assemblyInfo)
        {
            return new XunitTestFrameworkDiscoverer(
                assemblyInfo: assemblyInfo,
                sourceProvider: SourceInformationProvider,
                diagnosticMessageSink: DiagnosticMessageSink,
                collectionFactory: new CollectionPerSessionTestCollectionFactory());
        }

        protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName)
        {
            return new MultiNodeTestFrameworkExecutor(assemblyName, SourceInformationProvider, DiagnosticMessageSink);
        }
    }
}