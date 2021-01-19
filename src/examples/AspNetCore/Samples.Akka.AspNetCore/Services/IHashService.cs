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
        private bool _isDisposed;

        public void Dispose()
        {
            _isDisposed = true;
        }

        public bool IsDisposed => _isDisposed;

        public int Hash(string input)
        {
           if(_isDisposed)
               throw new ObjectDisposedException("HashServiceImpl disposed");

           return MurmurHash.StringHash(input);
        }
    }
}
