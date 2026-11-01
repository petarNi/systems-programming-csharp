using System;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading;
using System.Web;

namespace Zadatak24
{
    public sealed class HttpServer
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly InMemoryCache _cache = new InMemoryCache();
        private readonly GitHubClient _gitHubClient;
        private volatile bool _running;

        public HttpServer(string prefix, int maxApiParallel)
        {
            _listener.Prefixes.Add(prefix);
            _gitHubClient = new GitHubClient(maxApiParallel);
        }

        public void Start()
        {
            _listener.Start();
            _running = true;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                while (_running)
                {
                    HttpListenerContext ctx = null;
                    try
                    {
                        ctx = _listener.GetContext();
                    }
                    catch (HttpListenerException) when (!_running)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        RequestLogger.Log("ACCEPT ERROR: " + ex.Message);
                        continue;
                    }

                    ThreadPool.QueueUserWorkItem(state =>
                    {
                        try { Handle((HttpListenerContext)state); }
                        catch (Exception ex)
                        {
                            try { WriteJson((HttpListenerContext)state, 500, JsonUtil.Serialize(new { error = "Internal server error", details = ex.Message }), false); }
                            catch { /* ignore */ }
                            RequestLogger.Log("UNHANDLED: " + ex);
                        }
                    }, ctx);
                }
            });
        }

        public void Stop()
        {
            _running = false;
            try { _listener.Stop(); } catch { /* ignore */ }
        }

        private void Handle(HttpListenerContext ctx)
        {
            var sw = Stopwatch.StartNew();
            var req = ctx.Request;
            try
            {
                if (req.HttpMethod != "GET")
                {
                    WriteJson(ctx, 405, JsonUtil.Serialize(new { error = "Only GET is allowed" }), false);
                    return;
                }

                if (req.Url.AbsolutePath.TrimEnd('/') != "/commits")
                {
                    WriteJson(ctx, 404, JsonUtil.Serialize(new { error = "Not found" }), false);
                    return;
                }

                var q = HttpUtility.ParseQueryString(req.Url.Query ?? "");
                var owner = (q["owner"] ?? "").Trim();
                var repo = (q["repo"] ?? "").Trim();

                if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo))
                {
                    WriteJson(ctx, 400, JsonUtil.Serialize(new { error = "Missing required query params: owner, repo" }), false);
                    return;
                }

                var sinceStr = Safe(q["since"]);
                var untilStr = Safe(q["until"]);
                var anonStr = Safe(q["anon"]);
                var forceStr = Safe(q["force"]);
                bool forceBypass = StringUtil.EqualsOrdinalIgnoreCase(forceStr, "true");

                var cacheKey = BuildCacheKey(req.Url.AbsolutePath, q);

                if (!forceBypass && _cache.TryGet(cacheKey, out var cached))
                {
                    WritePrepared(ctx, cached.StatusCode, cached.Json, cached.Headers, true);
                    return;
                }

                int? sinceEpoch = DateUtil.ParseYmdToUnix(sinceStr);
                int? untilEpoch = DateUtil.ParseYmdToUnix(untilStr);
                bool? anon = BoolUtil.ParseNullable(anonStr);

                int status;
                string json;

                try
                {
                    var contributors = _gitHubClient.GetContributorsStats(owner, repo, anon);
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
                catch (WebException wex)
                {
                    var http = wex.Response as HttpWebResponse;
                    var code = http != null ? (int)http.StatusCode : 502;

                    string msg = code == 404 ? "Repository or endpoint not found."
                              : code == 403 ? "Forbidden or rate-limited by GitHub."
                              : "Upstream error: " + wex.Message;

                    status = code;
                    json = JsonUtil.Serialize(new { error = msg });
                }

                var headers = HeaderUtil.DefaultHeaders();
                headers["X-Cache"] = "MISS";
                if (status == 200)
                    _cache.Set(cacheKey, status, json, headers);

                WritePrepared(ctx, status, json, headers, false);
            }
            finally
            {
                sw.Stop();
                RequestLogger.Log($"{req.RemoteEndPoint} {req.HttpMethod} {req.RawUrl} -> {ctx.Response.StatusCode} in {sw.ElapsedMilliseconds}ms");
            }
        }

        private static void WriteJson(HttpListenerContext ctx, int status, string json, bool cacheHit)
        {
            var headers = HeaderUtil.DefaultHeaders();
            headers["X-Cache"] = cacheHit ? "HIT" : "MISS";
            WritePrepared(ctx, status, json, headers, cacheHit);
        }

        private static void WritePrepared(HttpListenerContext ctx, int status, string json, ConcurrentDictionary<string, string> headers, bool cacheHit)
        {
            var res = ctx.Response;
            var bytes = Encoding.UTF8.GetBytes(json);

            res.StatusCode = status;
            res.ContentType = "application/json; charset=utf-8";
            foreach (var kv in headers)
                res.AddHeader(kv.Key, kv.Value);

            using (var os = res.OutputStream)
            {
                os.Write(bytes, 0, bytes.Length);
            }
        }

        private static string Safe(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static string BuildCacheKey(string path, NameValueCollection q)
        {
            return CacheKeyUtil.FromQuery(path, q);
        }
    }
}
