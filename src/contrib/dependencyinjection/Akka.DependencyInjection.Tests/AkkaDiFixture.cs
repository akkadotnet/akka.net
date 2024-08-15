﻿// -----------------------------------------------------------------------
//  <copyright file="AkkaDiFixture.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using Microsoft.Extensions.DependencyInjection;

namespace Akka.DependencyInjection.Tests;

public class AkkaDiFixture : IDisposable
{
    public AkkaDiFixture()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISingletonDependency, Singleton>()
            .AddScoped<IScopedDependency, Scoped>()
            .AddTransient<ITransientDependency, Transient>();

        Provider = services.BuildServiceProvider();
    }

    public IServiceProvider Provider { get; private set; }

    public void Dispose()
    {
        Provider = null;
    }

    public interface IDependency : IDisposable
    {
        string Name { get; }

        bool Disposed { get; }
    }

    public interface ITransientDependency : IDependency
    {
    }

    public class Transient : ITransientDependency
    {
        public Transient() : this("t" + Guid.NewGuid())
        {
        }

        public Transient(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public bool Disposed { get; private set; }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    public interface IScopedDependency : IDependency
    {
    }

    public class Scoped : IScopedDependency
    {
        public Scoped() : this("s" + Guid.NewGuid())
        {
        }

        public Scoped(string name)
        {
            Name = name;
        }

        public void Dispose()
        {
            Disposed = true;
        }

        public bool Disposed { get; private set; }
        public string Name { get; }
    }

    public interface ISingletonDependency : IDependency
    {
    }

    public class Singleton : ISingletonDependency
    {
        public Singleton() : this("singleton")
        {
        }

        public Singleton(string name)
        {
            Name = name;
        }

        public void Dispose()
        {
            Disposed = true;
        }

        public bool Disposed { get; private set; }
        public string Name { get; }
    }
}