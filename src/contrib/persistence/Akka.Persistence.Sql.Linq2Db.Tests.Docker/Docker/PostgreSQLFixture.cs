using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Akka.Util;
using Docker.DotNet;
using Docker.DotNet.Models;
using Npgsql;
using Xunit;

namespace Akka.Persistence.Sql.Linq2Db.Tests.Docker
{
    /// <summary>
    ///     Fixture used to run SQL Server
    /// </summary>
    public class PostgreSQLFixture : IAsyncLifetime
    {
        protected readonly string PostgreSqlImageName = $"PostgreSQL-{Guid.NewGuid():N}";
        protected DockerClient Client;

        public PostgreSQLFixture()
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

        protected string PostgreSQLImageName
        {
            get
            {
                //if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                //    return "microsoft/mssql-server-windows-express";
                return "postgres";
            }
        }

        protected string SqlServerImageTag
        {
            get
            {
                //if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                //    return "2017-latest";
                return "latest";
            }
        }

        public string ConnectionString { get; private set; }

        public async Task InitializeAsync()
        {
            var images = await Client.Images.ListImagesAsync(new ImagesListParameters {MatchName = PostgreSQLImageName});
            if (images.Count == 0)
                await Client.Images.CreateImageAsync(
                    new ImagesCreateParameters {FromImage = PostgreSQLImageName, Tag = "latest"}, null,
                    new Progress<JSONMessage>(message =>
                    {
                        Console.WriteLine(!string.IsNullOrEmpty(message.ErrorMessage)
                            ? message.ErrorMessage
                            : $"{message.ID} {message.Status} {message.ProgressMessage}");
                    }));

            var postgresPort = ThreadLocalRandom.Current.Next(9000, 10000);

            // create the container
            await Client.Containers.CreateContainerAsync(new CreateContainerParameters
            {
                Image = PostgreSQLImageName,
                Name = PostgreSqlImageName,
                Tty = true,
                ExposedPorts = new Dictionary<string, EmptyStruct>
                {
                    {"5432/tcp", new EmptyStruct()}
                },
                HostConfig = new HostConfig
                {
                    PortBindings = new Dictionary<string, IList<PortBinding>>
                    {
                        {
                            "5432/tcp",
                            new List<PortBinding>
                            {
                                new PortBinding
                                {
                                    HostPort = $"{postgresPort}"
                                }
                            }
                        }
                    }
                },
                Env = new[] {"POSTGRES_PASSWORD=l0lTh1sIsOpenSource"}
            });

            // start the container
            await Client.Containers.StartContainerAsync(PostgreSqlImageName, new ContainerStartParameters());

            // Provide a 30 second startup delay
            await Task.Delay(TimeSpan.FromSeconds(30));

            var connectionString = new NpgsqlConnectionStringBuilder()
            {
                Host = "localhost", Password = "l0lTh1sIsOpenSource",
                Username = "postgres", Database = "postgres",
                Port = postgresPort
            };

            ConnectionString = connectionString.ToString();
        }

        public async Task DisposeAsync()
        {
            if (Client != null)
            {
                await Client.Containers.StopContainerAsync(PostgreSqlImageName, new ContainerStopParameters());
                await Client.Containers.RemoveContainerAsync(PostgreSqlImageName,
                    new ContainerRemoveParameters {Force = true});
                Client.Dispose();
            }
        }
    }
}