using System;
using System.Threading;
using System.Threading.Tasks;

namespace Zadatak24_Async
{
    internal static class Program
    {
        static void Main(string[] args)
        {
            AsyncMain().GetAwaiter().GetResult();
        }

        private static async Task AsyncMain()
        {
            RequestLogger.Init("logs");
            RequestLogger.Log("Server starting (async)...");

            // (opcionalno) podigni minimalan broj niti jer imamo blokirajuće delove / burst-ove
            ThreadPool.GetMinThreads(out var wMin, out var cMin);
            ThreadPool.SetMinThreads(Math.Max(wMin, 32), Math.Max(cMin, 32));

            var cts = new CancellationTokenSource();
            HttpServerAsync server = null;
            try
            {
                server = new HttpServerAsync("http://localhost:8080/", maxApiParallel: 4);

                Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                };

                await server.StartAsync(cts.Token);
                RequestLogger.Log("Listening on http://localhost:8080/ (press Ctrl+C to exit)");

                await Task.Delay(Timeout.Infinite, cts.Token);
            }
            catch (TaskCanceledException)
            {
                // ok – Ctrl+C
            }
            catch (Exception ex)
            {
                RequestLogger.Log("FATAL: " + ex);
            }
            finally
            {
                if (server != null) await server.StopAsync();
                cts.Dispose();
            }
        }
    }
}
