////-----------------------------------------------------------------------
//// <copyright file="ConfigurationSample.cs" company="Akka.NET Project">
////     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
////     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
//// </copyright>
////-----------------------------------------------------------------------

//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
//using Akka.Configuration;
//using Xunit;
//using Xunit.Abstractions;
//using FluentAssertions;

//namespace DocsExamples.Configuration
//{
//    public class ConfigurationSample
//    {
       
//        [Fact]
//        public void StringSubstitutionSample()
//        {
//            // <StringSubstitutionSample>
//            // ${string_bar} will be substituted by 'bar' and concatenated with `foo` into `foobar`
//            var hoconString = @"
//string_bar = bar
//string_foobar = foo${string_bar}
//";
//            var config = ConfigurationFactory.ParseString(hoconString); // This config uses ConfigurationFactory as a helper
//            config.GetString("string_foobar").Should().Be("foobar");
//            // </StringSubstitutionSample>
//        }


        
//        [Fact]
//        public void ArraySubstitutionSample()
//        {
//            // <ArraySubstitutionSample>
//            // ${a} will be substituted by the array [1, 2] and concatenated with [3, 4] to create [1, 2, 3, 4]
//            var hoconString = @"
//a = [1,2]
//b = ${a} [3, 4]";
//            Config config = hoconString; // This Config uses implicit conversion from string directly into a Config object
//            (new[] { 1, 2, 3, 4 }).ShouldAllBeEquivalentTo(config.GetIntList("b"));
//            // </ArraySubstitutionSample>
//        }
       
//        [Fact]
//        public void ObjectMergeSubstitutionSample()
//        {
//            // <ObjectMergeSubstitutionSample>
//            // ${a} will be substituted by hocon object 'a' and merged with object 'b'
//            var hoconString = @"
//a.a : 1
//b.b : 2
//b : ${a}
//";
//            var expectedHoconString = @"{
//  a : {
//    a : 1
//  },
//  b : {
//    b : 2,
//    a : 1
//  }
//}";
//            Config config = hoconString;
//            //expectedHoconString.ShouldBeEquivalentTo(config.Value.ToString(1, 2));
//            // </ObjectMergeSubstitutionSample>
//        }


        
//        [Fact]
//        public void SelfReferencingSubstitutionWithString()
//        {
//            // <SelfReferencingSubstitutionWithString>
//            // This is not an invalid substitution, it is a self referencing substitution, you can think of it as `a = a + 'bar'`
//            // ${a} will be substituted with its previous value, which is 'foo', concatenated with 'bar' to make 'foobar', 
//            // and then stored back into `a`
//            var hoconString = @"
//a = foo
//a = ${a}bar
//";
//            Config config = hoconString;
//            config.GetString("a").Should().Be("foobar");
//            // </SelfReferencingSubstitutionWithString>
//        }
        
//        [Fact]
//        public void SelfReferencingSubstitutionWithArray()
//        {
//            // <SelfReferencingSubstitutionWithArray>
//            // This is not an invalid substitution, it is a self referencing substitution, you can think of it as `a = a + [3, 4]`
//            // ${a} will be substituted with its previous value, which is [1, 2], concatenated with [3, 4] to make [1, 2, 3, 4], 
//            // and then stored back into a
//            var hoconString = @"
//a = [1, 2]
//a = ${a} [3, 4]
//";
//            Config config = hoconString;
//            (new int[] { 1, 2, 3, 4 }).ShouldAllBeEquivalentTo(config.GetIntList("a"));
//            // </SelfReferencingSubstitutionWithArray>
//        }

//        [Fact]
//        public void PlusEqualOperatorSample()
//        {
//            // <PlusEqualOperatorSample>
//            // These += operations will create an array field `a` with value [1, 2, 3, [4, 5] ]
//            // the first operation appends the value 3 to the array [1, 2]
//            // the second operation _inserts_ the array [4, 5] to the array [1, 2, 3]
//            var hoconString = @"
//a = [ 1, 2 ]
//a += 3
//a += ${b}
//b = [ 4, 5 ]
//";

//            Config config = hoconString;
//            var array = config.GetValue("a").GetArray();
//            array[0].GetInt().Should().Be(1);
//            array[1].GetInt().Should().Be(2);
//            array[2].GetInt().Should().Be(3);
//            array[3].GetIntList().ShouldAllBeEquivalentTo(new int[] { 4, 5 });
//            // </PlusEqualOperatorSample>
//        }

//        // <CircularReferenceSubstitutionError>
//        // All these are circular reference and will throw an exception during parsing
//        [Theory]
//        [InlineData(@"
//bar : ${foo}
//foo : ${bar}")]
//        [InlineData(@"
//a : ${b}
//b : ${c}
//c : ${a}")]
//        [InlineData(@"
//a : 1
//b : 2
//a : ${b}
//b : ${a}")]
//        public void CircularReferenceSubstitutionError(string hoconString)
//        {
//            var ex = Assert.Throws<Exception>(() =>
//            {
//                Config config = hoconString;
//            });
//            ex.Should().NotBeNull();
//            ex.Message.Should().Contain("cyclic");
//        }
//        // </CircularReferenceSubstitutionError>

//        [Fact]
//        public void EnvironmentVariableSample()
//        {
//            // <EnvironmentVariableSample>
//            // This substitution will be subtituted by the environment variable.
//            var hoconString = "from_environment = ${MY_ENV_VAR}";
//            var value = 1000;
//            // Set environment variable named `ENVIRONMENT_VAR` with the string value 1000
//            Environment.SetEnvironmentVariable("MY_ENV_VAR", value.ToString()); 
//            try
//            {
//                Config config = hoconString;
//                // Value obtained from environment variable should be 1000
//                config.GetInt("from_environment").Should().Be(value); 
//            }
//            finally
//            {
//                // Delete the environment variable.
//                Environment.SetEnvironmentVariable("MY_ENV_VAR", null); 
//            }
//            // </EnvironmentVariableSample>
//        }

//        [Fact]
//        public void BlockedEnvironmentVariableSample()
//        {
//            // <BlockedEnvironmentVariableSample>
//            var hoconString = @"
//# This property blocks `MY_ENV_VAR` from being resolved from the environment variable
//MY_ENV_VAR = null

//# This substitution will not be populated with the environment variable because it is blocked
//from_environment = ${MY_ENV_VAR} 
//";
//            Environment.SetEnvironmentVariable("MY_ENV_VAR", "1000");
//            try
//            {
//                Config config = hoconString;
//                // Environment variable is blocked by the previous declaration, it will contain null
//                config.GetString("from_environment").Should().BeNull(); 
//            }
//            finally
//            {
//                Environment.SetEnvironmentVariable("MY_ENV_VAR", null);
//            }
//            // </BlockedEnvironmentVariableSample>
//        }

//        [Fact]
//        public void UnsolvableSubstitutionWillThrowSample()
//        {
//            // <UnsolvableSubstitutionWillThrowSample>
//            // This substitution will throw an exception because it is a required substitution,
//            // and we can not resolve it, even when checking for environment variables.
//            var hoconString = "from_environment = ${MY_ENV_VAR}";

//            Assert.Throws<Exception>(() =>
//            {
//                Config config = hoconString;
//            }).Message.Should().StartWith("Unresolved substitution");
//            // </UnsolvableSubstitutionWillThrowSample>
//        }
//    }
//}
