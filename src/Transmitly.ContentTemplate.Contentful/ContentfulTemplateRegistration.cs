// Copyright (c) Code Impressions, LLC. All Rights Reserved.
//  
//  Licensed under the Apache License, Version 2.0 (the "License")
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//  
//      http://www.apache.org/licenses/LICENSE-2.0
//  
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Transmitly.Template.Configuration;
using Transmitly.Util;
namespace Transmitly;
public sealed class ContentfulTemplateRegistration : IContentTemplateRegistration
{
    private const string ContentfulCdnBaseUrl = "https://cdn.contentful.com/";
    private static readonly string[] TemplateFieldPreference =
    [
        "template",
        "htmlTemplate",
        "htmlBody",
        "body",
        "textTemplate",
        "textBody",
        "content",
        "message"
    ];

    private readonly Func<IDispatchCommunicationContext, Task<string>> _entryResolver;
    private readonly ContentfulCredentialsResolver _credsResolver;
    private readonly Func<HttpClient> _httpClientFactory;
    private readonly ContentfulTemplateRenderer _renderer;
    private readonly ContentfulRequestOptions _requestOptions;

#if NET5_0_OR_GREATER
    private static readonly HttpClient SharedClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2)
    });
#elif NETFRAMEWORK
    private static readonly HttpClient SharedClient = CreateFrameworkClient();
	private static HttpClient CreateFrameworkClient()
	{
		var client = new HttpClient();
		var servicePoint = ServicePointManager.FindServicePoint(new Uri(ContentfulCdnBaseUrl));
		servicePoint.ConnectionLeaseTimeout = (int)TimeSpan.FromMinutes(5).TotalMilliseconds;
		servicePoint.MaxIdleTime = (int)TimeSpan.FromMinutes(2).TotalMilliseconds;
		return client;
	}
#else
    private static readonly HttpClient SharedClient = new();
#endif

    public ContentfulTemplateRegistration(
        Func<IDispatchCommunicationContext, Task<string>> entryResolver,
        ContentfulCredentialsResolver credsResolver,
        ContentfulTemplateRenderer? renderer = null,
        string? cultureInfo = null,
        Func<HttpClient>? httpClientFactory = null,
        ContentfulRequestOptions? requestOptions = null)
    {
        _entryResolver = Guard.AgainstNull(entryResolver);
        _credsResolver = Guard.AgainstNull(credsResolver);
        _httpClientFactory = httpClientFactory ?? (() => SharedClient);
        CultureInfo = GuardCulture.AgainstNull(cultureInfo);
        _renderer = renderer ?? CreateDefaultRenderer(CultureInfo);
        _requestOptions = requestOptions ?? new ContentfulRequestOptions();
    }

    public CultureInfo CultureInfo { get; }

    public async Task<string?> GetContentAsync(IDispatchCommunicationContext context)
    {
        var entryId = await _entryResolver(context);
        var creds = await _credsResolver(context);

        var client = _httpClientFactory();
        var url = $"{ContentfulCdnBaseUrl}spaces/{creds.SpaceId}/environments/{creds.Environment ?? "master"}/entries/{entryId}?access_token={creds.Token}&locale={context.CultureInfo.Name}";
        var ct = _requestOptions.CancellationTokenResolver?.Invoke(context) ?? CancellationToken.None;
        var json = await GetStringWithRetryAsync(client, url, ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(json);
        return await _renderer(context, doc); // pick the field(s) you want
    }

    internal static ContentfulTemplateRenderer CreateDefaultRenderer(CultureInfo cultureInfo)
    {
        var localePreference = BuildLocalePriority(cultureInfo);
        return (_, entry) =>
        {
            var template = ExtractTemplate(entry.RootElement, localePreference);
            return Task.FromResult(template);
        };
    }

    private async Task<string> GetStringWithRetryAsync(HttpClient client, string url, CancellationToken ct)
    {
        var maxRetries = Math.Max(0, _requestOptions.MaxRetries);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
                if (ShouldRetryResponse(response) && attempt < maxRetries)
                {
                    var delay = GetRetryDelay(attempt, response);
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, ct).ConfigureAwait(false);
                    }

                    continue;
                }

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ShouldRetryException(ex, ct) && attempt < maxRetries)
            {
                var delay = GetRetryDelay(attempt, null);
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                }
            }
        }
    }

    private bool ShouldRetryResponse(HttpResponseMessage response)
    {
        var predicate = _requestOptions.ShouldRetryResponse;
        if (predicate != null)
        {
            return predicate(response);
        }

        var status = (int)response.StatusCode;
        return status == 408 || status == 429 || (status >= 500 && status <= 599);
    }

    private bool ShouldRetryException(Exception ex, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return false;
        }

        var predicate = _requestOptions.ShouldRetryException;
        if (predicate != null)
        {
            return predicate(ex);
        }

        return ex is HttpRequestException || ex is TaskCanceledException;
    }

    private TimeSpan GetRetryDelay(int attempt, HttpResponseMessage? response)
    {
        if (response?.Headers?.RetryAfter != null)
        {
            var retryAfter = response.Headers.RetryAfter;
            if (retryAfter.Delta.HasValue)
            {
                return retryAfter.Delta.Value;
            }

            if (retryAfter.Date.HasValue)
            {
                var timeDelay = retryAfter.Date.Value - DateTimeOffset.UtcNow;
                return timeDelay > TimeSpan.Zero ? timeDelay : TimeSpan.Zero;
            }
        }

        var baseDelay = _requestOptions.BaseRetryDelay;
        if (baseDelay <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var maxDelay = _requestOptions.MaxRetryDelay;
        if (maxDelay <= TimeSpan.Zero)
        {
            maxDelay = baseDelay;
        }

        var multiplier = Math.Pow(2, attempt);
        var delayMs = baseDelay.TotalMilliseconds * multiplier;
        var delay = TimeSpan.FromMilliseconds(delayMs);
        return delay > maxDelay ? maxDelay : delay;
    }


    private static List<string> BuildLocalePriority(CultureInfo cultureInfo)
    {
        var locales = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (string.Equals(value, "iv", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (seen.Add(value))
            {
                locales.Add(value);
            }
        }

        Add(cultureInfo.Name);
        Add(cultureInfo.TwoLetterISOLanguageName);
        Add("en-US");
        Add("en");
        Add(string.Empty);

        return locales;
    }

    private static string? ExtractTemplate(JsonElement root, IReadOnlyList<string> localePreference)
    {
        var entry = NormalizeEntry(root);
        if (!entry.TryGetProperty("fields", out var fields))
        {
            return null;
        }

        foreach (var candidate in TemplateFieldPreference)
        {
            if (TryReadField(fields, candidate, localePreference, out var value))
            {
                return value;
            }
        }

        foreach (var property in fields.EnumerateObject())
        {
            var value = GetLocalizedValue(property.Value, localePreference);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static JsonElement NormalizeEntry(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("fields", out _))
        {
            return root;
        }

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("items", out var items) &&
            items.ValueKind == JsonValueKind.Array &&
            items.GetArrayLength() > 0)
        {
            var first = items[0];
            if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("fields", out _))
            {
                return first;
            }
        }

        return root;
    }

    private static bool TryReadField(JsonElement fields, string fieldName, IReadOnlyList<string> localePreference, out string? value)
    {
        value = null;
        if (!fields.TryGetProperty(fieldName, out var fieldElement))
        {
            return false;
        }

        value = GetLocalizedValue(fieldElement, localePreference);
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string? GetLocalizedValue(JsonElement element, IReadOnlyList<string> localePreference)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                return element.GetRawText();
            case JsonValueKind.Object:
                foreach (var locale in localePreference)
                {
                    if (string.IsNullOrWhiteSpace(locale))
                    {
                        continue;
                    }

                    if (element.TryGetProperty(locale, out var localized))
                    {
                        var value = GetLocalizedValue(localized, localePreference);
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            return value;
                        }
                    }
                }

                foreach (var property in element.EnumerateObject())
                {
                    var value = GetLocalizedValue(property.Value, localePreference);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var value = GetLocalizedValue(item, localePreference);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
                break;
        }

        return null;
    }
}
