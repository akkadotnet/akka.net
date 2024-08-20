// -----------------------------------------------------------------------
// <copyright file="IConnectionBehaviorSetter.cs" company="Petabridge, LLC">
//      Copyright (C) 2015 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Threading.Tasks;

namespace Akka.Persistence.TestKit;

public interface IJournalConnectionBehaviorSetter
{
    Task SetInterceptorAsync(IConnectionInterceptor interceptor);
}