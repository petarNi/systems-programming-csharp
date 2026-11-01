using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;

namespace Zadatak24
{
    public sealed class GitHubClient
    {
        private readonly SemaphoreSlim _apiSlots;

        public GitHubClient(int maxParallel)
        {
            _apiSlots = new SemaphoreSlim(maxParallel, maxParallel);
        }

        public object GetContributorsStats(string owner, string repo, bool? anon)
        {
            string url = $"https://api.github.com/repos/{owner}/{repo}/stats/contributors";
            if (anon.HasValue)
                url += "?anon=" + (anon.Value ? "true" : "false");

            const int maxTries = 6;
            const int delayMs = 1200;

            _apiSlots.Wait();
            try
            {
                for (int attempt = 1; attempt <= maxTries; attempt++)
                {
                    var req = (HttpWebRequest)WebRequest.Create(url);
                    req.Method = "GET";
                    req.UserAgent = "Zadatak24-Client";
                    req.Accept = "application/vnd.github+json";
                    req.Headers["X-GitHub-Api-Version"] = "2022-11-28";

                    req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    {
                        int code = (int)resp.StatusCode;
                        if (code == 202)
                        {
                            Thread.Sleep(delayMs);
                            continue;
                        }

                        using (var s = resp.GetResponseStream())
                        using (var r = new StreamReader(s))
                        {
                            var text = r.ReadToEnd();
                            return JsonUtil.Deserialize(text); // object (array/dict)
                        }
                    }
                }
                return null; // i dalje 202
            }
            finally
            {
                _apiSlots.Release();
            }
        }

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
