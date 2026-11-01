using Newtonsoft.Json.Linq;
using RestSharp;
using RxFootballApp.Models;

namespace RxFootballApp.Services
{
    public class SportMonksClient
    {
        private const string TokenVariable = "SPORTMONKS_API_TOKEN";

        private readonly string apiToken;
        private readonly RestClient client = new("https://api.sportmonks.com/v3/football");

        public SportMonksClient()
        {
            apiToken = Environment.GetEnvironmentVariable(TokenVariable) ?? "";
            if (string.IsNullOrWhiteSpace(apiToken))
                throw new InvalidOperationException(
                    $"API token is missing. Set the {TokenVariable} environment variable.");
        }

        public async Task<FixtureInfo> GetFixtureAsync(int fixtureId)
        {
            var request = new RestRequest($"fixtures/{fixtureId}?include=participants;scores;lineups");
            request.AddQueryParameter("api_token", apiToken);

            var response = await client.ExecuteAsync(request);

            if (!response.IsSuccessful || string.IsNullOrEmpty(response.Content))
                throw new Exception($"API call failed for fixture {fixtureId}: " +
                                    $"{(int)response.StatusCode} {response.ErrorMessage}");

            var data = JObject.Parse(response.Content)["data"];
            if (data == null || data.Type == JTokenType.Null)
                throw new Exception($"Fixture {fixtureId} not found or not available in the current plan.");

            return Parse(data);
        }

        private static FixtureInfo Parse(JToken data)
        {
            var fixture = new FixtureInfo
            {
                Id = (int)data["id"]!,
                Name = (string?)data["name"] ?? "",
                StartingAt = (string?)data["starting_at"],
                ResultInfo = (string?)data["result_info"]
            };

            var teams = new Dictionary<int, string>();
            int? homeId = null, awayId = null;

            foreach (var team in data["participants"] ?? new JArray())
            {
                int id = (int)team["id"]!;
                string name = (string?)team["name"] ?? "?";
                teams[id] = name;

                var location = (string?)team["meta"]?["location"];
                if (location == "home") { fixture.HomeTeam = name; homeId = id; }
                if (location == "away") { fixture.AwayTeam = name; awayId = id; }
            }

            foreach (var score in data["scores"] ?? new JArray())
            {
                var description = (string?)score["description"];
                var side = (string?)score["score"]?["participant"];
                var goals = (int?)score["score"]?["goals"];

                if (description == "CURRENT")
                {
                    if (side == "home") fixture.HomeGoals = goals;
                    if (side == "away") fixture.AwayGoals = goals;
                }
                else if (description == "1ST_HALF")
                {
                    if (side == "home") fixture.HomeGoalsHalfTime = goals;
                    if (side == "away") fixture.AwayGoalsHalfTime = goals;
                }
            }

            foreach (var player in data["lineups"] ?? new JArray())
            {
                var teamId = (int?)player["team_id"];
                fixture.Lineup.Add(new PlayerInfo
                {
                    Name = (string?)player["player_name"],
                    JerseyNumber = (int?)player["jersey_number"],
                    Team = teamId.HasValue && teams.TryGetValue(teamId.Value, out var t) ? t : null
                });
            }

            return fixture;
        }
    }
}
