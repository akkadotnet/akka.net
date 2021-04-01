//-----------------------------------------------------------------------
// <copyright file="IJournalBehaviorSetter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.TestKit
{
    using System.Threading.Tasks;

    public interface IJournalBehaviorSetter
    {
        Task SetInterceptorAsync(IJournalInterceptor interceptor);
    }
}
