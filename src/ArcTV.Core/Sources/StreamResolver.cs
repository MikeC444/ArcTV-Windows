using System.Net;

namespace ArcTV.Core.Sources;

/// <summary>
/// Follows a debrid "resolve" link (an address that answers with a redirect to the real file, and can take a while to do so the first time) before the player gets it, so the wait happens
/// behind the loading cover with a retry, instead of inside the video engine (which sat silent on it). Any failure returns the address it was given: the player then tries it as before.
/// </summary>
public static class StreamResolver
{
    public const int Attempts = 3;

    public static async Task<Uri> ResolveAsync(Uri url, Action<string>? report = null, CancellationToken cancel = default, HttpMessageHandler? handler = null, TimeSpan? attemptTimeout = null)
    {
        if (url.Scheme is not ("http" or "https")) return url;
        using var http = handler is null
            ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = true })
            : new HttpClient(handler, disposeHandler: false);
        http.Timeout = Timeout.InfiniteTimeSpan;
        var current = url;
        for (var hop = 0; hop < 6; hop++)
        {
            Uri? next = null;
            var done = false;
            for (var attempt = 1; attempt <= Attempts && !done; attempt++)
            {
                cancel.ThrowIfCancellationRequested();
                report?.Invoke(attempt == 1 ? "Getting the file ready..." : $"Getting the file ready... (try {attempt} of {Attempts})");
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                limit.CancelAfter(attemptTimeout ?? TimeSpan.FromSeconds(30));
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, current);
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0); // the headers are all that is wanted
                    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);
                    if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
                    {
                        next = location.IsAbsoluteUri ? location : new Uri(current, location);
                        done = true;
                    }
                    else if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                    {
                        return current; // the real file
                    }
                    // an error answer: try again (a link being made ready often fails once)
                }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
                {
                    // slow or dropped: try again
                }

                if (!done && attempt < Attempts) await Task.Delay(attemptTimeout is null ? 1500 : 10, cancel).ConfigureAwait(false);
            }

            if (next is null) return current == url ? url : current; // nothing better was found: the player tries what it has
            current = next;
        }

        return current;
    }
}
