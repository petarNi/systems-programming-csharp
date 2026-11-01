namespace RxFootballApp.Models
{
    public class FixtureInfo
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? StartingAt { get; set; }
        public string? ResultInfo { get; set; }
        public string HomeTeam { get; set; } = "?";
        public string AwayTeam { get; set; } = "?";
        public int? HomeGoals { get; set; }
        public int? AwayGoals { get; set; }
        public int? HomeGoalsHalfTime { get; set; }
        public int? AwayGoalsHalfTime { get; set; }
        public List<PlayerInfo> Lineup { get; set; } = new();

        public override string ToString()
        {
            var score = HomeGoals.HasValue && AwayGoals.HasValue
                ? $"{HomeGoals} : {AwayGoals}"
                : "no score";
            var halfTime = HomeGoalsHalfTime.HasValue && AwayGoalsHalfTime.HasValue
                ? $" (HT {HomeGoalsHalfTime} : {AwayGoalsHalfTime})"
                : "";

            var lines = new List<string>
            {
                $"Fixture #{Id}  |  {StartingAt} UTC",
                $"  {HomeTeam}  {score}{halfTime}  {AwayTeam}",
                $"  {ResultInfo}"
            };

            if (Lineup.Count > 0)
            {
                lines.Add($"  Lineup ({Lineup.Count} players):");
                lines.AddRange(Lineup.Select(p => "    " + p));
            }
            else
            {
                lines.Add("  Lineup: not available (not included in the SportMonks free plan)");
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
}
