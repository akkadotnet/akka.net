// -----------------------------------------------------------------------
//  <copyright file="HostingSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor.Internal;
using Akka.Annotations;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Hosting.TestKit
{
    public abstract partial class TestKit : IAsyncLifetime
    {
        private ImplicitSenderScope? _senderScope;

        [InternalApi]
        public Task InitializeAsync()
        {
            _senderScope = ImplicitSenderScope.Enter(() => Volatile.Read(ref _ambientTestActor));
            return InitializeWithScopeAsync(_senderScope);
        }

        private async Task InitializeWithScopeAsync(ImplicitSenderScope scope)
        {
            try
            {
                await InitializeAsyncCore();
            }
            catch (Exception initializationException)
            {
                scope.Dispose();
                try
                {
                    if (_host is { } host)
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        var stopping = host.StopAsync(cts.Token);
                        await stopping.WithCancellation(cts.Token);
                        await stopping;
                    }
                }
                catch (Exception stopException)
                {
                    initializationException.Data["HostingTestKitStopException"] = stopException;
                }
                try
                {
                    _host?.Dispose();
                }
                catch (Exception cleanupException)
                {
                    initializationException.Data["HostingTestKitCleanupException"] = cleanupException;
                }
                throw;
            }
        }

        public Task DisposeAsync()
        {
            var scope = _senderScope;
            var disposal = DisposeWithScopeAsync(scope);
            scope?.Detach();
            return disposal;
        }

        private async Task DisposeWithScopeAsync(ImplicitSenderScope? scope)
        {
            try
            {
                await DisposeAsyncCore();
            }
            finally
            {
                scope?.Dispose();
            }
        }
    }
}
