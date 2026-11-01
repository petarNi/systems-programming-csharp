namespace RxFootballApp.Services
{
    public static class Logger
    {
        private static readonly object lockObj = new();
        private static readonly string logDirectory = "logs";

        public static void Log(string message)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [thread {Environment.CurrentManagedThreadId}] {message}";

            lock (lockObj)
            {
                Directory.CreateDirectory(logDirectory);
                var file = Path.Combine(logDirectory, $"log-{DateTime.Now:yyyy-MM-dd}.txt");
                File.AppendAllText(file, line + Environment.NewLine);
                Console.WriteLine(line);
            }
        }
    }
}
