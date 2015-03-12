using Akka.Actor;

namespace Akka.TestKit
{
    /// <summary>
    /// Normally test classes has <see cref="TestKitBase.TestActor">TestActor</see> as implicit sender.
    /// So when no sender is specified when sending messages, <see cref="TestKitBase.TestActor">TestActor</see>
    /// is used.
    /// When a a test class implements <see cref="NoImplicitSender"/> this behavior is removed and the normal
    /// behavior is restored, i.e. <see cref="NoSender"/> is used as sender when no sender has been specified.
    /// <example>
    /// <code>
    /// public class WithImplicitSender : TestKit
    /// {
    ///    public void TheTestMethod()
    ///    {
    ///       ...
    ///       someActor.Tell("message");             //TestActor is used as Sender
    ///       someActor.Tell("message", TestActor);  //TestActor is used as Sender
    ///    }
    /// }
    /// 
    /// public class WithNoImplicitSender : TestKit, NoImplicitSender
    /// {
    ///    public void TheTestMethod()
    ///    {
    ///       ...
    ///       someActor.Tell("message");    //NoSender is used as Sender
    ///    }
    /// }
    /// </code>
    /// </example>
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public interface NoImplicitSender
    {
    }
}