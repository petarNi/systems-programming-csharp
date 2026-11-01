using System;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Globalization;
using System.Text;
using System.Web.Script.Serialization;

namespace Zadatak24
{
    public static class JsonUtil
    {
        private static JavaScriptSerializer NewSerializer()
        {
            return new JavaScriptSerializer
            {
                // ogroman odgovor za velike repoe – dignemo limit do maksimuma
                MaxJsonLength = int.MaxValue,
                RecursionLimit = 1024
            };
        }
        public static string Serialize(object o) => NewSerializer().Serialize(o);
        public static object Deserialize(string json) => NewSerializer().DeserializeObject(json);
    }

    public static class HeaderUtil
    {
        public static ConcurrentDictionary<string, string> DefaultHeaders()
        {
            var d = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
            d["Access-Control-Allow-Origin"] = "*";
            d["Cache-Control"] = "no-store";
            return d;
        }
    }

    public static class DateUtil
    {
        public static int? ParseYmdToUnix(string ymd)
        {
            if (string.IsNullOrWhiteSpace(ymd)) return null;
            if (DateTime.TryParseExact(ymd, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                       DateTimeStyles.AssumeUniversal, out var dt))
            {
                var dto = new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
                return (int)dto.ToUnixTimeSeconds();
            }
            return null;
        }
    }

    public static class BoolUtil
    {
        public static bool? ParseNullable(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)) return false;
            return null;
        }
    }

    public static class StringUtil
    {
        public static bool EqualsOrdinalIgnoreCase(string a, string b) =>
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    public static class CacheKeyUtil
    {
        public static string FromQuery(string path, NameValueCollection q)
        {
            var kvs = new StringBuilder(path);
            var keys = q.AllKeys;
            Array.Sort(keys, StringComparer.Ordinal); // stabilna normalizacija
            foreach (var k in keys)
            {
                if (k == null) continue;
                kvs.Append('|').Append(k).Append('=').Append(q[k]);
            }
            return kvs.ToString();
        }
    }   
}
