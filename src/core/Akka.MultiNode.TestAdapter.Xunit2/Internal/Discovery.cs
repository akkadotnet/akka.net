//-----------------------------------------------------------------------
// <copyright file="Discovery.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit.Abstractions;
using Xunit.Sdk;
using LongLivedMarshalByRefObject = Xunit.LongLivedMarshalByRefObject;

namespace Akka.MultiNode.TestAdapter.Internal
{
    internal class Discovery : LongLivedMarshalByRefObject, IMessageSink, IDisposable
    {
        // There can be multiple fact attributes in a single class, but our convention
        // limits them to 1 fact attribute per test class
        public List<MultiNodeTestCase> TestCases { get; }
        public List<ErrorMessage> Errors { get; } = new List<ErrorMessage>();
        public bool WasSuccessful => Errors.Count == 0;

        private readonly string _assemblyPath;
        
        /// <summary>
        /// Initializes a new instance of the <see cref="Discovery"/> class.
        /// </summary>
        public Discovery(string assemblyPath)
        {
            _assemblyPath = assemblyPath;
            TestCases = new List<MultiNodeTestCase>();
            Finished = new ManualResetEvent(false);
        }

        public ManualResetEvent Finished { get; }

        public virtual bool OnMessage(IMessageSinkMessage message)
        {
            switch (message)
            {
                case ITestCaseDiscoveryMessage discovery:
                    var testClass = discovery.TestClass.Class;
                    if (testClass.IsAbstract) 
                        break;
                    
                    foreach (var c in discovery.TestCases.Where(t => t is MultiNodeTestCase).Cast<MultiNodeTestCase>())
                    {
                        TestCases.Add(c);
                    }
                    break;
                case IDiscoveryCompleteMessage _:
                    Finished.Set();
                    break;
                case ErrorMessage err:
                    Errors.Add(err);
                    break;
            }

            return true;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Finished.Dispose();
        }
    }
}
