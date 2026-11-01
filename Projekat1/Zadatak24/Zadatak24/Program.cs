using System;
using System.Threading;

namespace Zadatak24
{
    internal static class Program
    {
        static void Main(string[] args)
        {
            RequestLogger.Init("logs"); // folder za logove
            RequestLogger.Log("Server starting...");

            var server = new HttpServer("http://localhost:8080/", maxApiParallel: 4);

            try
            {
                server.Start();
                RequestLogger.Log("Listening on http://localhost:8080/ (press Ctrl+C to exit)");

                var quit = new ManualResetEvent(false);
                Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true;
                    quit.Set();
                };
                quit.WaitOne();
            }
            catch (Exception ex)
            {
                RequestLogger.Log("FATAL: " + ex);
            }
            finally
            {
                server.Stop();
                RequestLogger.Log("Server stopped.");
            }
        }
    }
}
