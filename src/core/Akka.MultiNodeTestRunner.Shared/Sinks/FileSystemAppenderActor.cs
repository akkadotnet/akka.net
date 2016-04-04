//-----------------------------------------------------------------------
// <copyright file="FileSystemAppenderActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using Akka.Actor;

namespace Akka.MultiNodeTestRunner.Shared.Sinks
{
    /// <summary>
    /// Actor that just writes dumb messages to the file system - used for capturing
    /// raw logs from individual nodes.
    /// </summary>
    public class FileSystemAppenderActor : ReceiveActor
    {
        private readonly string _fullFilePath;

        public FileSystemAppenderActor(string fullFilePath)
        {
            _fullFilePath = fullFilePath;

            ReceiveAny(o =>
            {
                File.AppendAllText(_fullFilePath, o.ToString() + Environment.NewLine);
            });
        }
    }
}