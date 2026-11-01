using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Zadatak24_Async
{
    // Mali specifični izuzetak da prenesemo HTTP status
    public sealed class GitHubApiException : Exception
    {
        public int StatusCode { get; }
        public GitHubApiException(int statusCode, string message = null) : base(message) => StatusCode = statusCode;
    }

    public sealed class GitHubClient
    {
        private readonly SemaphoreSlim _apiSlots;
        private static readonly HttpClient _http;

        static GitHubClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            _http = new HttpClient(handler, disposeHandler: true);
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Zadatak24-Async-Client");
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            // napomena: za autentikaciju bi se dodao Authorization header (npr. token) – nije obavezno ovde
        }

        public GitHubClient(int maxParallel)
        {
            _apiSlots = new SemaphoreSlim(maxParallel, maxParallel);
        }

        public async Task<object> GetContributorsStatsAsync(string owner, string repo, bool? anon, CancellationToken ct)
        {
            string url = $"https://api.github.com/repos/{owner}/{repo}/stats/contributors";
            if (anon.HasValue)
                url += "?anon=" + (anon.Value ? "true" : "false");

            const int maxTries = 6;
            const int delayMs = 1200;

            await _apiSlots.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                for (int attempt = 1; attempt <= maxTries; attempt++)
                {
                    using (var resp = await _http.GetAsync(url, ct).ConfigureAwait(false))
                    {

                        if (resp.StatusCode == HttpStatusCode.Accepted) // 202 – statistika se priprema
                        {
                            await Task.Delay(delayMs, ct).ConfigureAwait(false);
                            continue;
                        }

                        if (resp.StatusCode == HttpStatusCode.NotFound)
                            throw new GitHubApiException(404, "Not Found");

                        if (resp.StatusCode == HttpStatusCode.Forbidden)
                            throw new GitHubApiException(403, "Forbidden / rate limited");

                        if ((int)resp.StatusCode >= 400)
                            throw new GitHubApiException((int)resp.StatusCode, "Upstream error");

                        // OK (200)
                        var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        return JsonUtil.Deserialize(text); // object (array/dict)
                    }
                }

                // i dalje 202 posle više pokušaja
                return null;
            }
            finally
            {
                _apiSlots.Release();
            }
        }

        // isto kao i u sync varijanti – CPU-bound, ostaje sinhrono
        public static int SumCommitsTotal(object contributorsJson)
        {
            var arr = contributorsJson as object[];
            if (arr == null) return 0;

            int sum = 0;
            foreach (var item in arr)
            {
                var d = item as Dictionary<string, object>;
                if (d == null) continue;
                if (d.TryGetValue("total", out var t))
                    sum += Convert.ToInt32(t, CultureInfo.InvariantCulture);
            }
            return sum;
        }

        public static int SumCommitsFiltered(object contributorsJson, int? sinceEpoch, int? untilEpoch)
        {
            var arr = contributorsJson as object[];
            if (arr == null) return 0;

            int sum = 0;
            foreach (var item in arr)
            {
                var d = item as Dictionary<string, object>;
                if (d == null) continue;

                if (!d.TryGetValue("weeks", out var weeksObj)) continue;
                var weeks = weeksObj as object[];
                if (weeks == null) continue;

                foreach (var w in weeks)
                {
                    var wd = w as Dictionary<string, object>;
                    if (wd == null) continue;

                    if (!wd.TryGetValue("w", out var wEpochObj)) continue;
                    int wEpoch = Convert.ToInt32(wEpochObj, CultureInfo.InvariantCulture);

                    if (sinceEpoch.HasValue && wEpoch < sinceEpoch.Value) continue;
                    if (untilEpoch.HasValue && wEpoch > untilEpoch.Value) continue;

                    if (wd.TryGetValue("c", out var cObj))
                        sum += Convert.ToInt32(cObj, CultureInfo.InvariantCulture);
                }
            }
            return sum;
        }
    }
}
