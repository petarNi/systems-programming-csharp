namespace RxFootballApp.Models
{
    public class PlayerInfo
    {
        public string? Name { get; set; }
        public int? JerseyNumber { get; set; }
        public string? Team { get; set; }

        public override string ToString()
        {
            var number = JerseyNumber.HasValue ? $"#{JerseyNumber}" : "#-";
            return $"{number,-4} {Name} ({Team})";
        }
    }
}
