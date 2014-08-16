using Akka.Actor;

namespace Akka.TestKit
{

    /// <summary>
    /// A extension to be used together with the TestKit.
    /// <example>
    /// To get the settings:
    /// <code>var testKitSettings = TestKitExtension.For(system);</code>
    /// </example>
    /// </summary>
    public class TestKitExtension : ExtensionIdProvider<TestKitSettings>
    {      
        public override TestKitSettings CreateExtension(ExtendedActorSystem system)
        {
            return new TestKitSettings(system.Settings.Config);
        }

        public static TestKitSettings For(ActorSystem system)
        {
            return system.WithExtension<TestKitSettings, TestKitExtension>();
        }
    }
}