using System;
using System.IO;
using Xunit.v3;

namespace Akka.MultiNode.TestAdapter.Internal
{
    internal class CollectionPerSessionTestCollectionFactory : IXunitTestCollectionFactory
    {
        private readonly IXunitTestAssembly _testAssembly;
        private IXunitTestCollection? _collection;

        public CollectionPerSessionTestCollectionFactory(IXunitTestAssembly testAssembly)
        {
            _testAssembly = testAssembly;
        }

        public IXunitTestCollection Get(Type testClass)
        {
            return _collection ??= new XunitTestCollection(
                _testAssembly,
                collectionDefinition: null,
                disableParallelization: true,
                displayName: $"MultiNode test collection for {Path.GetFileName(_testAssembly.Assembly.Location)}");
        }

        public string DisplayName => "collection-per-session";
    }
}
