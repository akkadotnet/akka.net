// -----------------------------------------------------------------------
//  <copyright file="TypeHints.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------
#if AOT_ENABLED
using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Akka.Event;

namespace Akka.Actor.Internal;

internal sealed class TypeHints
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public static readonly Type DefaultActorRefProviderType = typeof(LocalActorRefProvider);

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public static readonly Type DefaultSchedulerType = typeof(HashedWheelTimerScheduler);
}
#endif