using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;

namespace LiveCaptionsTranslator.utils
{
    public static class CookieBridge
    {
        private const string DEFAULT_PREFIX = "http://127.0.0.1:";
        private static HttpListener? listener;
        private static CancellationTokenSource? cts;

        public static event Action<string>? CookiesUpdated;
        public static string LatestCookieHeader { get; private set; } = string.Empty;
        public static int Port { get; private set; } = 17891;

        public static void Start(int? portOverride = null)
        {
            if (!HttpListener.IsSupported)
                return;

            lock (typeof(CookieBridge))
            {
                if (listener != null)
                    return;

                Port = portOverride ?? 17891;
                listener = new HttpListener();
                listener.Prefixes.Add($"{DEFAULT_PREFIX}{Port}/");

                try
                {
                    listener.Start();
                }
                catch
                {
                    listener = null;
                    return;
                }

                cts = new CancellationTokenSource();
                _ = Task.Run(() => ListenAsync(cts.Token));
            }
        }

        public static void Stop()
        {
            lock (typeof(CookieBridge))
            {
                cts?.Cancel();
                listener?.Stop();
                listener = null;
            }
        }

        private static async Task ListenAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && listener != null)
            {
                HttpListenerContext? context = null;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch
                {
                    break;
                }

                if (context == null)
                    continue;

                _ = Task.Run(() => HandleContextAsync(context), token);
            }
        }

        private static async Task HandleContextAsync(HttpListenerContext context)
        {
            try
            {
                if (context.Request.HttpMethod != "POST" ||
                    !string.Equals(context.Request.Url?.AbsolutePath, "/cookies", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                    context.Response.Close();
                    return;
                }

                string body;
                using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
                    body = await reader.ReadToEndAsync();

                if (string.IsNullOrWhiteSpace(body))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    context.Response.Close();
                    return;
                }

                var payload = JsonSerializer.Deserialize<CookiePayload>(body);
                if (payload?.cookies == null || payload.cookies.Length == 0)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    context.Response.Close();
                    return;
                }

                var builder = new StringBuilder();
                foreach (var cookie in payload.cookies)
                {
                    if (string.IsNullOrEmpty(cookie.name) || cookie.value == null)
                        continue;
                    if (builder.Length > 0)
                        builder.Append("; ");
                    builder.Append(cookie.name).Append('=').Append(cookie.value);
                }

                if (builder.Length > 0)
                {
                    LatestCookieHeader = builder.ToString();
                    CookiesUpdated?.Invoke(LatestCookieHeader);
                }

                byte[] okBytes = Encoding.UTF8.GetBytes("ok");
                context.Response.StatusCode = (int)HttpStatusCode.OK;
                context.Response.ContentType = "text/plain";
                context.Response.ContentLength64 = okBytes.Length;
                await context.Response.OutputStream.WriteAsync(okBytes, 0, okBytes.Length);
                context.Response.Close();
            }
            catch
            {
                try
                {
                    context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    context.Response.Close();
                }
                catch
                {
                }
            }
        }

        private class CookiePayload
        {
            public CookieItem[]? cookies { get; set; }
        }

        private class CookieItem
        {
            public string? name { get; set; }
            public string? value { get; set; }
        }
    }
}
