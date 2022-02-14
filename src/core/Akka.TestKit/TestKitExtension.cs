//-----------------------------------------------------------------------
// <copyright file="TestKitExtension.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Akka.Actor;

namespace Akka.TestKit
{
    /// <summary>
    /// A extension to be used together with the TestKit.
    /// <example>
    /// To get the settings:
    /// <code>var testKitSettings = TestKitExtension.For(system);</code>
    /// </example>
    /// </summary>
    public class TestKitExtension : ExtensionIdProvider<TestKitSettings>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public override TestKitSettings CreateExtension(ExtendedActorSystem system)
        {
            return new TestKitSettings(system.Settings.Config);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public static TestKitSettings For(ActorSystem system)
        {
            return system.GetExtension<TestKitSettings>();
        }
        
    }

    public static class EnvelopeExtention
    {
        public static async Task<T> TryTakeAsync<T>(this BufferBlock<T> bufferBlock)
        {
            try
            {
                return await bufferBlock.ReceiveAsync();
            }
            catch { return default; }
        }
        
        public static async Task<T> TryTakeAsync<T>(this BufferBlock<T> bufferBlock, TimeSpan timeout, CancellationToken cst)
        {
            try
            {
                return await bufferBlock.ReceiveAsync(timeout, cst);
            }
            catch { return default; }
        }
    }
}
