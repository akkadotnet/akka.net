// //-----------------------------------------------------------------------
// // <copyright file="DocsSample.cs" company="Akka.NET Project">
// //     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hocon;
using Xunit;

namespace DocsExamples.Configuration
{
    public class ConfigurationSample
    {
        const string _hoconContent = @"
    BLOCKED_ENVIRONMENT_VAR = null
    string_bar = bar
    my_object {
        from_environment = $(?ENVIRONMENT_VAR)
        should_be_null = $(?BLOCKED_ENVIRONMENT_VAR)
        foobar = foo$(bar)
    }
";

        [Fact]
        public void EnvironmentVariableSample()
        {
            var hoconString = @"
my_object {
    from_environment = $(?ENVIRONMENT_VAR) # This substitution will be subtituted by the environment variable.
}";

            Environment.SetEnvironmentVariable("ENVIRONMENT_VAR", "1000"); // Set environment variable named `ENVIRONMENT_VAR` with the string value 1000
            try
            {

                Config config = hoconString;  // This Config uses implicit conversion from string directly into a Config object
                var integerValue = config.GetInt("my_object.from_environment");
                Console.WriteLine($"Value obtained from environment variable should bee 1000, and it is {integerValue}");
            }
            finally
            {
                Environment.SetEnvironmentVariable("ENVIRONMENT_VAR", null); // Delete the environment variable.
            }
        }

        const string _blockedEnvironmentHocon = @"
ENVIRONMENT_VAR = null
my_object {
    from_environment = $(?ENVIRONMENT_VAR)
}
";
        public void BlockedEnvironmentVariableSample()
        {
            Environment.SetEnvironmentVariable("ENVIRONMENT_VAR", "1000");
            try
            {
                var config = ConfigurationFactory.ParseString(_blockedEnvironmentHocon); // This config uses ConfigurationFactory as a helper
                var value = config.GetString("my_object.from_environment");
                Console.WriteLine($"Environment variable is blocked by the previous declaration. The property should be null?{value == null}");
            }
            finally
            {
                Environment.SetEnvironmentVariable("ENVIRONMENT_VAR", null);
            }
        }
    }
}
