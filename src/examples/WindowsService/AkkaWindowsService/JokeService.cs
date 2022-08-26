//-----------------------------------------------------------------------
// <copyright file="JokeService.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#region akka-windows-joke-service
using System.Net.Http.Json;
using System.Text.Json;

namespace AkkaWindowsService
{
    public class JokeService
    {
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _options = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private const string JokeApiUrl =
            "https://karljoke.herokuapp.com/jokes/programming/random";

        public JokeService(HttpClient httpClient) => _httpClient = httpClient;

        public async Task<string> GetJokeAsync()
        {
            try
            {
                // The API returns an array with a single entry.
                var jokes = await _httpClient.GetFromJsonAsync<Joke[]>(
                    JokeApiUrl, _options);

                var joke = jokes?[0];

                return joke is not null
                    ? $"{joke.Setup}{Environment.NewLine}{joke.Punchline}"
                    : "No joke here...";
            }
            catch (Exception ex)
            {
                return $"That's not funny! {ex}";
            }
        }
    }

    public record Joke(int Id, string Type, string Setup, string Punchline);
}
#endregion
