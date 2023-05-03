//-----------------------------------------------------------------------
// <copyright file="IHashService.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Util;

namespace Samples.Akka.AspNetCore.Services
{
    /// <summary>
    /// A simple service type we're going to use to test DI
    /// </summary>
    public interface IHashService : IDisposable
    {
        bool IsDisposed { get; }

        int Hash(string input);
    }
    
    /// <summary>
    /// Service implementation that will throw when disposed
    /// </summary>
    public sealed class HashServiceImpl : IHashService
    {
        public void Dispose()
        {
            IsDisposed = true;
        }

        public bool IsDisposed { get; private set; }

        public int Hash(string input)
        {
           if(IsDisposed)
               throw new ObjectDisposedException("HashServiceImpl disposed");

           return MurmurHash.StringHash(input);
        }
    }
}
