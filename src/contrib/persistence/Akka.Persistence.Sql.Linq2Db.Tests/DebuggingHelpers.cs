using System;
using System.Diagnostics;
using System.Text;
using LinqToDB.Data;
using Xunit.Abstractions;

namespace Akka.Persistence.Sql.Linq2Db.Tests
{
    public static class DebuggingHelpers
    {
        public static void SetupTraceDump(ITestOutputHelper outputHelper)
        {
            LinqToDB.Data.DataConnection.TurnTraceSwitchOn();
            LinqToDB.Data.DataConnection.OnTrace = info =>
            {
                try
                {
                    if (info.TraceInfoStep == TraceInfoStep.BeforeExecute)
                    {
                        outputHelper.WriteLine(info.SqlText);
                    }
                    else if (info.TraceLevel == TraceLevel.Error)
                    {
                        var sb = new StringBuilder();

                        for (var ex = info.Exception;
                            ex != null;
                            ex = ex.InnerException)
                        {
                            sb
                                .AppendLine()
                                .AppendLine("/*")
                                .AppendLine($"Exception: {ex.GetType()}")
                                .AppendLine($"Message  : {ex.Message}")
                                .AppendLine(ex.StackTrace)
                                .AppendLine("*/")
                                ;
                        }

                        outputHelper.WriteLine(sb.ToString());
                    }
                    else if (info.RecordsAffected != null)
                    {
                        outputHelper.WriteLine(
                            $"-- Execution time: {info.ExecutionTime}. Records affected: {info.RecordsAffected}.\r\n");
                    }
                    else
                    {
                        outputHelper.WriteLine(
                            $"-- Execution time: {info.ExecutionTime}\r\n");
                    }
                }
                catch (InvalidOperationException)
                {
                    //This will sometimes get thrown because of async and ITestOutputHelper interactions.
                }
            };
        }
    }
}