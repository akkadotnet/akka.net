//-----------------------------------------------------------------------
// <copyright file="XUnitAsyncTestsSanityCheck.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Dispatch
{
	public class XUnitAsyncTestsSanityCheck : AkkaSpec
	{
		[Fact]
		public async Task Async_tests_should_not_lose_ambient_context()
		{
			var ambientContext = InternalCurrentActorCellKeeper.Current;
			var backgroundOps = new List<Task>();
			for (var c = 0; c < 50; c++)
			{
				backgroundOps.Add(Task.Factory.StartNew(async () =>
				{
					for (var t = 0; t < 1000; t++)
						await Task.Delay(1);
				}));
			}
			for (var t = 0; t < 1000; t++)
			{
				Assert.Equal(ambientContext, InternalCurrentActorCellKeeper.Current);
				await Task.Delay(1);
			}
			await Task.WhenAll(backgroundOps);
		}
	}
}
