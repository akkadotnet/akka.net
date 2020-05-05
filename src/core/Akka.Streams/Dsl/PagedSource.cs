//-----------------------------------------------------------------------
// <copyright file="PagedSource.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Annotations;
using Akka.Streams.Util;
using Akka.Util;

namespace Akka.Streams.Dsl
{
    public static class PagedSource
    {
        /// <summary>
        /// Page for <see cref="PagedSource"/>.
        /// </summary>
        /// <typeparam name="T">type of page items</typeparam>
        /// <typeparam name="TKey">type of page keys</typeparam>
        public class Page<T, TKey>
        {
            public IEnumerable<T> Items { get; }
            public Option<TKey> NextKey { get; }

            public Page(IEnumerable<T> items, Option<TKey> nextKey)
            {
                Items = items;
                NextKey = nextKey;
            }
        }

        /// <summary>
        /// Defines a factory for "paged source".
        /// <para>
        /// "Paged source" is a Source streaming items from a paged API.
        /// The paged API is accessed with a page key and returns data.
        /// This data contain items and optional information about the key of the next page.
        /// </para>
        /// </summary>
        /// <typeparam name="T">type of page items</typeparam>
        /// <typeparam name="TKey">type of page keys</typeparam>
        /// <param name="firstKey">key of first page</param>
        /// <param name="pageFactory">maps page key to Task of page data</param>
        [ApiMayChange]
        public static Source<T, NotUsed> Create<T, TKey>(TKey firstKey, Func<TKey, Task<Page<T, TKey>>> pageFactory)
        {
            var pageSource =
                Source.UnfoldAsync
                (
                    new Option<TKey>(firstKey),
                    async key =>
                    {
                        var page = key.HasValue ? await pageFactory(key.Value) : new Page<T, TKey>(Enumerable.Empty<T>(), Option<TKey>.None);

                        if (page.Items != null && page.Items.Any())
                            return (page.NextKey, page);
                        else
                            return Option<(Option<TKey>, Page<T, TKey>)>.None;
                    }
                );

            return pageSource.ConcatMany(page => Source.From(page.Items));
        }
    }
}
