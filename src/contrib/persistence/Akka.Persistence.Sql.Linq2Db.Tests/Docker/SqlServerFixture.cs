using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Akka.Util;
using Docker.DotNet;
using Docker.DotNet.Models;
using Xunit;

namespace Akka.Persistence.Sql.Linq2Db.Tests.Docker
{
    /// <summary>
    ///     Fixture used to run SQL Server
    /// </summary>
    public class SqlServerFixture : IAsyncLifetime
    {
        protected readonly string SqlContainerName = $"sqlserver-{Guid.NewGuid():N}";
        protected DockerClient Client;

        public SqlServerFixture()
        {
            DockerClientConfiguration config;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                config = new DockerClientConfiguration(new Uri("unix://var/run/docker.sock"));
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                config = new DockerClientConfiguration(new Uri("npipe://./pipe/docker_engine"));
            else
                throw new NotSupportedException($"Unsupported OS [{RuntimeInformation.OSDescription}]");

            Client = config.CreateClient();
        }

        protected string SqlServerImageName
        {
            get
            {
                //if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                //    return "microsoft/mssql-server-windows-express";
                return "mcr.microsoft.com/mssql/server";
            }
        }

        protected string SqlServerImageTag
        {
            get
            {
                //if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                //    return "2017-latest";
                return "2017-latest-ubuntu";
            }
        }

        public string ConnectionString { get; private set; }

        public async Task InitializeAsync()
        {
            var images = await Client.Images.ListImagesAsync(new ImagesListParameters {MatchName = SqlServerImageName});
            if (images.Count == 0)
                await Client.Images.CreateImageAsync(
                    new ImagesCreateParameters {FromImage = SqlServerImageName, Tag = "latest"}, null,
                    new Progress<JSONMessage>(message =>
                    {
                        Console.WriteLine(!string.IsNullOrEmpty(message.ErrorMessage)
                            ? message.ErrorMessage
                            : $"{message.ID} {message.Status} {message.ProgressMessage}");
                    }));

            var sqlServerHostPort = ThreadLocalRandom.Current.Next(9000, 10000);

            // create the container
            await Client.Containers.CreateContainerAsync(new CreateContainerParameters
            {
                Image = SqlServerImageName,
                Name = SqlContainerName,
                Tty = true,
                ExposedPorts = new Dictionary<string, EmptyStruct>
                {
                    {"1433/tcp", new EmptyStruct()}
                },
                HostConfig = new HostConfig
                {
                    PortBindings = new Dictionary<string, IList<PortBinding>>
                    {
                        {
                            "1433/tcp",
                            new List<PortBinding>
                            {
                                new PortBinding
                                {
                                    HostPort = $"{sqlServerHostPort}"
                                }
                            }
                        }
                    }
                },
                Env = new[] {"ACCEPT_EULA=Y", "SA_PASSWORD=l0lTh1sIsOpenSource"}
            });

            // start the container
            await Client.Containers.StartContainerAsync(SqlContainerName, new ContainerStartParameters());

            // Provide a 30 second startup delay
            await Task.Delay(TimeSpan.FromSeconds(30));


            var connectionString = new DbConnectionStringBuilder
            {
                ConnectionString =
                    "data source=.;database=akka_persistence_tests;user id=sa;password=l0lTh1sIsOpenSource;"
            };
            connectionString["Data Source"] = $"localhost,{sqlServerHostPort}";

            ConnectionString = connectionString.ToString();
        }

        public async Task DisposeAsync()
        {
            if (Client != null)
            {
                await Client.Containers.StopContainerAsync(SqlContainerName, new ContainerStopParameters());
                await Client.Containers.RemoveContainerAsync(SqlContainerName,
                    new ContainerRemoveParameters {Force = true});
                Client.Dispose();
            }
        }
    }
}