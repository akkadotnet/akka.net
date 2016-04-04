//-----------------------------------------------------------------------
// <copyright file="PatternSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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

