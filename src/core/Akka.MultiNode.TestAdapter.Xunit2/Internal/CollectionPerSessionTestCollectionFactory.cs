using System;
using System.Collections.Generic;
using System.IO;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Akka.MultiNode.TestAdapter.Internal
{
    internal class CollectionPerSessionTestCollectionFactory : IXunitTestCollectionFactory
    {
        private readonly Dictionary<IAssemblyInfo, TestCollection> _collectionCache =
            new Dictionary<IAssemblyInfo, TestCollection>();

        public ITestCollection Get(ITypeInfo testClass)
        {
            if (_collectionCache.TryGetValue(testClass.Assembly, out var collection))
                return collection;
            
            collection = new TestCollection(
                new TestAssembly(testClass.Assembly),
                null,
                $"MultiNode test collection for {Path.GetFileName(testClass.Assembly.AssemblyPath)}");
            _collectionCache[testClass.Assembly] = collection;
            return collection;
        }

        public string DisplayName => "collection-per-session";
    }
}