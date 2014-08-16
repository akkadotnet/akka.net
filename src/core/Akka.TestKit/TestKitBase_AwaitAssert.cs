using System;
using System.Threading;
using Akka.Util;

namespace Akka.TestKit
{
    public abstract partial class TestKitBase
    {

        //TODO: Implement awaitAssert. Make it public
        //  /**
        //   * Await until the given assert does not throw an exception or the timeout
        //   * expires, whichever comes first. If the timeout expires the last exception
        //   * is thrown.
        //   *
        //   * If no timeout is given, take it from the innermost enclosing `within`
        //   * block.
        //   *
        //   * Note that the timeout is scaled using Duration.dilated,
        //   * which uses the configuration entry "akka.test.timefactor".
        //   */
        //  def awaitAssert(a: ⇒ Any, max: Duration = Duration.Undefined, interval: Duration = 800.millis) {
        //    val _max = remainingOrDilated(max)
        //    val stop = now + _max
        //
        //    @tailrec
        //    def poll(t: Duration) {
        //      val failed =
        //        try { a; false } catch {
        //          case NonFatal(e) ⇒
        //            if ((now + t) >= stop) throw e
        //            true
        //        }
        //      if (failed) {
        //        Thread.sleep(t.toMillis)
        //        poll((stop - now) min interval)
        //      }
        //    }
        //
        //    poll(_max min interval)
        //  }
    }
}