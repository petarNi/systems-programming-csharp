using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Zadatak24_Async
{
    public sealed class HttpServerAsync
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly InMemoryCache _cache = new InMemoryCache();
        private readonly GitHubClient _gitHubClient;
        private volatile bool _running;

        public HttpServerAsync(string prefix, int maxApiParallel)
        {
            _listener.Prefixes.Add(prefix);
            _gitHubClient = new GitHubClient(maxApiParallel);
        }

        public Task StartAsync(CancellationToken ct)
        {
            _listener.Start();
            _running = true;

            // Accept petlja – async
            _ = Task.Run(async () =>
            {
                while (_running && !ct.IsCancellationRequested)
                {
                    HttpListenerContext ctx = null;
                    try
                    {
                        // HttpListener ima GetContextAsync u .NET 4.5+
                        ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException) when (!_running || ct.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (HttpListenerException) when (!_running || ct.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        RequestLogger.Log("ACCEPT ERROR: " + ex.Message);
                        continue;
                    }

                    // per-request obrada – async
                    _ = Task.Run(async () =>
                    {
                        try { await HandleAsync(ctx, ct).ConfigureAwait(false); }
                        catch (Exception ex)
                        {
                            try
                            {
                                var json = JsonUtil.Serialize(new { error = "Internal server error", details = ex.Message });
                                await WriteJsonAsync(ctx, 500, json, cacheHit: false).ConfigureAwait(false);
                            }
                            catch { /* ignore "second error while failing" */ }
                            RequestLogger.Log("UNHANDLED: " + ex);
                        }
                    }, ct);
                }
            }, ct);

            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            _running = false;
            try { _listener.Stop(); } catch { /* ignore */ }
            return Task.CompletedTask;
        }

        // ------------------------- Obrada zahteva -------------------------

        private async Task HandleAsync(HttpListenerContext ctx, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var req = ctx.Request;

            try
            {
                if (!string.Equals(req.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteJsonAsync(ctx, 405, JsonUtil.Serialize(new { error = "Only GET is allowed" }), cacheHit: false);
                    return;
                }

                if (req.Url.AbsolutePath.TrimEnd('/') != "/commits")
                {
                    await WriteJsonAsync(ctx, 404, JsonUtil.Serialize(new { error = "Not found" }), cacheHit: false);
                    return;
                }

                var q = HttpUtility.ParseQueryString(req.Url.Query ?? "");
                var owner = (q["owner"] ?? "").Trim();
                var repo = (q["repo"] ?? "").Trim();

                if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo))
                {
                    await WriteJsonAsync(ctx, 400, JsonUtil.Serialize(new { error = "Missing required query params: owner, repo" }), cacheHit: false);
                    return;
                }

                var sinceStr = Safe(q["since"]);
                var untilStr = Safe(q["until"]);
                var anonStr = Safe(q["anon"]);
                var forceStr = Safe(q["force"]);
                bool forceBypass = StringUtil.EqualsOrdinalIgnoreCase(forceStr, "true");

                var cacheKey = CacheKeyUtil.FromQuery(req.Url.AbsolutePath, q);

                if (!forceBypass && _cache.TryGet(cacheKey, out var cached))
                {
                    await WritePreparedAsync(ctx, cached.StatusCode, cached.Json, cached.Headers, cacheHit: true);
                    return;
                }

                int? sinceEpoch = DateUtil.ParseYmdToUnix(sinceStr);
                int? untilEpoch = DateUtil.ParseYmdToUnix(untilStr);
                bool? anon = BoolUtil.ParseNullable(anonStr);

                int status;
                string json;

                try
                {
                    // async odlazak ka GitHub-u (sa WaitAsync na semaforu)
                    var contributors = await _gitHubClient.GetContributorsStatsAsync(owner, repo, anon, ct).ConfigureAwait(false);

                    if (contributors == null)
                    {
                        status = 504;
                        json = JsonUtil.Serialize(new { error = "GitHub statistics are not ready (timeout). Try again later." });
                    }
                    else
                    {
                        var total = (sinceEpoch.HasValue || untilEpoch.HasValue)
                            ? GitHubClient.SumCommitsFiltered(contributors, sinceEpoch, untilEpoch)
                            : GitHubClient.SumCommitsTotal(contributors);

                        status = 200;
                        json = JsonUtil.Serialize(new
                        {
                            owner,
                            repo,
                            total_commits = total,
                            filters = new { since = sinceStr, until = untilStr, anon = anon }
                        });
                    }
                }
                catch (GitHubApiException ghex)
                {
                    status = ghex.StatusCode;
                    var msg = ghex.StatusCode == 404 ? "Repository or endpoint not found."
                          : ghex.StatusCode == 403 ? "Forbidden or rate-limited by GitHub."
                          : $"Upstream error (HTTP {ghex.StatusCode}).";
                    json = JsonUtil.Serialize(new { error = msg });
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // gašenje servera – prekini tiho
                    return;
                }
                catch (Exception ex)
                {
                    status = 500;
                    json = JsonUtil.Serialize(new { error = "Upstream error", details = ex.Message });
                }

                var headers = HeaderUtil.DefaultHeaders();
                headers["X-Cache"] = "MISS";
                if (status == 200)
                    _cache.Set(cacheKey, status, json, headers);

                await WritePreparedAsync(ctx, status, json, headers, cacheHit: false).ConfigureAwait(false);
            }
            finally
            {
                sw.Stop();
                RequestLogger.Log($"{req.RemoteEndPoint} {req.HttpMethod} {req.RawUrl} -> {ctx.Response.StatusCode} in {sw.ElapsedMilliseconds}ms");
            }
        }

        // ------------------------- Slanje odgovora (async) -------------------------

        private static async Task WriteJsonAsync(HttpListenerContext ctx, int status, string json, bool cacheHit)
        {
            var headers = HeaderUtil.DefaultHeaders();
            headers["X-Cache"] = cacheHit ? "HIT" : "MISS";
            await WritePreparedAsync(ctx, status, json, headers, cacheHit).ConfigureAwait(false);
        }

        private static async Task WritePreparedAsync(HttpListenerContext ctx, int status, string json,
            ConcurrentDictionary<string, string> headers, bool cacheHit)
        {
            var res = ctx.Response;
            var bytes = Encoding.UTF8.GetBytes(json);

            res.StatusCode = status;
            res.ContentType = "application/json; charset=utf-8";
            foreach (var kv in headers)
                res.AddHeader(kv.Key, kv.Value);

            using (var os = res.OutputStream)
            {
                // async upis u mrežni tok
                await os.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            }
        }

        private static string Safe(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
