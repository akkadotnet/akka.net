using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

#if CORECLR
namespace Akka.MultiNodeTestRunner.Shared.Extensions
{
    internal static class DateTimeExtension
    {
        public static string ToShortTimeString(this DateTime dateTime)
        {
            return dateTime.ToString("d");
        }
    }
}
#endif
