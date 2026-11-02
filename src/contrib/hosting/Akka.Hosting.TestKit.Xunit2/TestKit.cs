// -----------------------------------------------------------------------
//  <copyright file="HostingSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Annotations;
using Xunit;

namespace Akka.Hosting.TestKit
{
    public abstract partial class TestKit : IAsyncLifetime
    {
        [InternalApi]
        public Task InitializeAsync() => InitializeAsyncCore();

        public Task DisposeAsync() => DisposeAsyncCore();
    }
}
