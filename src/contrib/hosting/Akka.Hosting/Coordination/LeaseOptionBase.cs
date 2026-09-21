// -----------------------------------------------------------------------
//  <copyright file="LeaseOptionBase.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Actor.Setup;

namespace Akka.Hosting.Coordination
{
    public abstract class LeaseOptionBase : IHoconOption
    {
        public abstract string ConfigPath { get; }
        public abstract Type Class { get; }
        public abstract void Apply(AkkaConfigurationBuilder builder, Setup? setup = null);
    }
}