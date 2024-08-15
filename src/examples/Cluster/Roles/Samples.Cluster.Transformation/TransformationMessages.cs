﻿// -----------------------------------------------------------------------
//  <copyright file="TransformationMessages.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

namespace Samples.Cluster.Transformation;

public sealed class TransformationMessages
{
    public const string BACKEND_REGISTRATION = "BackendRegistration";

    public class TransformationJob
    {
        public TransformationJob(string text)
        {
            Text = text;
        }

        public string Text { get; }

        public override string ToString()
        {
            return Text;
        }
    }

    public class TransformationResult
    {
        public TransformationResult(string text)
        {
            Text = text;
        }

        public string Text { get; }

        public override string ToString()
        {
            return string.Format("TransformationResult({0})", Text);
        }
    }

    public class JobFailed
    {
        public JobFailed(string reason, TransformationJob job)
        {
            Job = job;
            Reason = reason;
        }

        public string Reason { get; }

        public TransformationJob Job { get; private set; }

        public override string ToString()
        {
            return string.Format("JobFailed({0})", Reason);
        }
    }
}