//-----------------------------------------------------------------------
// <copyright file="AkkaController.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#region akka-aspnet-core-controllers

using Microsoft.AspNetCore.Mvc;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Akka.AspNetCore.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AkkaController : ControllerBase
    {
        private readonly ILogger<AkkaController> _logger;
        private readonly IActorBridge _bridge;

        public AkkaController(ILogger<AkkaController> logger, IActorBridge bridge)
        {
            _logger = logger;
            _bridge = bridge;
        }

        [HttpGet]
        public async Task<IEnumerable<string>> Get()
        {
            return await _bridge.Ask<IEnumerable<string>>("get");
        }

        // POST api/<AkkaController>
        [HttpPost]
        public void Post([FromBody] string value)
        {
            _bridge.Tell(value);
        }

    }
}

#endregion
