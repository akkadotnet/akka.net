using System;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.TestKit
{
    /// <summary>
    /// Contains settings to be used when writing tests with TestKit.
    /// </summary>
    public class TestKitSettings : IExtension
    {
        private readonly Config _config;

        public TestKitSettings(Config config)
        {
            _config = config;
        }


        /// <summary>
        /// Gets the default timeout as specified in the setting akka.test.default-timeout.
        /// If not specified, a default value of 1 second will be returned.
        /// </summary>
        public TimeSpan DefaultTimeout
        {
            get
            {
                return _config.GetMillisDuration("akka.test.default-timeout", TimeSpan.FromSeconds(5));
            }
        }

        //TODO: Implement:
        //  val TestTimeFactor = config.getDouble("akka.test.timefactor").
        //       requiring(tf ⇒ !tf.isInfinite && tf > 0, "akka.test.timefactor must be positive finite double")
        // val SingleExpectDefaultTimeout: FiniteDuration = config.getMillisDuration("akka.test.single-expect-default")
        // val TestEventFilterLeeway: FiniteDuration = config.getMillisDuration("akka.test.filter-leeway")

    }
}