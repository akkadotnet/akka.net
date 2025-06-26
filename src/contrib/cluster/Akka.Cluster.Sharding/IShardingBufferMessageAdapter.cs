//-----------------------------------------------------------------------
// <copyright file="IShardingBufferMessageAdapter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Annotations;

namespace Akka.Cluster.Sharding;

[InternalApi]
public interface IShardingBufferMessageAdapter
{
    public object Apply(object message, IActorContext context);
    public object UnApply(object message, IActorContext context);
}

[InternalApi]
internal class EmptyBufferMessageAdapter: IShardingBufferMessageAdapter
{
    public static EmptyBufferMessageAdapter Instance { get; } = new ();

    private EmptyBufferMessageAdapter()
    {
    }
    
    public object Apply(object message, IActorContext context) => message;
    
    public object UnApply(object message, IActorContext context) => message;
}
