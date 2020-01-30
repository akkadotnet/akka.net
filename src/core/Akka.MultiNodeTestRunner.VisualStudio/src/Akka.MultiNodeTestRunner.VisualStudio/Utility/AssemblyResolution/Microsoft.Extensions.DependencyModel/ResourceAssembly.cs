// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if NETFRAMEWORK || NETCOREAPP

using System;

namespace Internal.Microsoft.Extensions.DependencyModel
{
    internal class ResourceAssembly
    {
        public ResourceAssembly(string path, string locale)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException(nameof(path));
            }
            if (string.IsNullOrEmpty(locale))
            {
                throw new ArgumentException(nameof(locale));
            }
            Locale = locale;
            Path = path;
        }

        public string Locale { get; set; }

        public string Path { get; set; }

    }
}

#endif
