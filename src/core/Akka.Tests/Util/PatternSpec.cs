//-----------------------------------------------------------------------
// <copyright file="PatternSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Xunit;

namespace Akka.Tests.Util
{
    
    public class PatternSpec
    {
        [Fact]
        public void PatternMatch_should_not_throw_NullReferenceException()
        {
            object nullObj = null;
            nullObj.Match()
                .With<string>(str => { })
                .Default(m => {});
        }
    }
}

