// -----------------------------------------------------------------------
//  <copyright file="HostingSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Annotations;
using Akka.TestKit.Xunit.Attributes;
using Xunit;

namespace Akka.Hosting.TestKit
{
    [AkkaCleanAmbientContext]
    public abstract partial class TestKit : IAsyncLifetime, IAsyncDisposable
    {
        [InternalApi]
        public ValueTask InitializeAsync() => new(InitializeAsyncCore());

        public ValueTask DisposeAsync() => new(DisposeAsyncCore());
    }
}
