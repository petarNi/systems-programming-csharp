using RxFootballApp.Rx;
using RxFootballApp.Services;

class Program
{
    static async Task Main()
    {
        Console.WriteLine("RxFootballApp – SportMonks Football API + Rx.NET");
        Console.WriteLine("Enter one or more fixture IDs separated by commas (e.g. 216268, 216269):");

        var input = Console.ReadLine() ?? "";
        var ids = input
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var id) ? id : (int?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            Console.WriteLine("No valid fixture IDs entered.");
            return;
        }

        try
        {
            Logger.Log($"Start fetching {ids.Count} fixture(s): {string.Join(", ", ids)}");
            await new RxProcessor().RunAsync(ids);
        }
        catch (Exception ex)
        {
            Logger.Log("Error: " + ex.Message);
        }

        Console.WriteLine("Press ENTER to exit.");
        Console.ReadLine();
    }
}
