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
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Transmitly.Template.Configuration;
namespace Transmitly;

/// <summary>
/// Extension methods for registering Contentful-backed templates.
/// </summary>
/// <remarks>
/// This extension is intended to be folded into the core Transmitly library in a future release.
/// </remarks>
public static class ContentfulTemplateExtensions
{
    /// <summary>
    /// Gets or sets the default credentials resolver used when none is provided.
    /// </summary>
    public static ContentfulCredentialsResolver? DefaultCredentialResolver { get; set; }

    /// <summary>
    /// Gets or sets the default renderer used when none is provided.
    /// </summary>
    public static ContentfulTemplateRenderer? DefaultRenderer { get; set; }

    /// <summary>
    /// Gets or sets the default request options used when none are provided.
    /// </summary>
    public static ContentfulRequestOptions DefaultRequestOptions { get; set; } = new();

    /// <summary>
    /// Configures the default credentials resolver using static values.
    /// </summary>
    /// <param name="spaceId">Contentful space identifier.</param>
    /// <param name="apiKey">Contentful delivery API token.</param>
    /// <param name="environment">Optional environment name; defaults to "master".</param>
    public static void UseDefaultCredentials(string spaceId, string apiKey, string environment = "master")
    {
        DefaultCredentialResolver = ctx =>
            Task.FromResult(new ContentfulCredentials(spaceId, apiKey, environment));
    }

    /// <summary>
    /// Registers a Contentful template by entry identifier.
    /// </summary>
    /// <param name="templates">Template configuration to update.</param>
    /// <param name="entryId">Contentful entry identifier.</param>
    /// <param name="creds">Optional credentials resolver.</param>
    /// <param name="renderer">Optional renderer to extract the template from the entry.</param>
    /// <param name="cultureInfo">Optional culture name used to prioritize locale selection.</param>
    /// <param name="httpClientFactory">Optional HTTP client factory.</param>
    /// <param name="requestOptions">Optional request behavior overrides.</param>
    /// <returns>The updated template configuration.</returns>
    public static IContentTemplateConfiguration AddContentfulTemplate(
        this IContentTemplateConfiguration templates,
        string entryId,
        ContentfulCredentialsResolver? creds = null,
        ContentfulTemplateRenderer? renderer = null,
        string? cultureInfo = null,
        Func<HttpClient>? httpClientFactory = null,
        ContentfulRequestOptions? requestOptions = null)
    {
        var c = creds ?? DefaultCredentialResolver
            ?? throw new InvalidOperationException("No Contentful credentials resolver configured.");
        var options = requestOptions ?? DefaultRequestOptions ?? new ContentfulRequestOptions();

        templates.TemplateRegistrations.Add(
            new ContentfulTemplateRegistration(
                _ => Task.FromResult(entryId),
                c,
                renderer ?? DefaultRenderer,
                cultureInfo,
                httpClientFactory,
                options));

        return templates;
    }

    /// <summary>
    /// Registers a Contentful template by entry identifier and field name.
    /// </summary>
    /// <param name="templates">Template configuration to update.</param>
    /// <param name="entryId">Contentful entry identifier.</param>
    /// <param name="fieldName">Field name to extract from the entry.</param>
    /// <param name="creds">Optional credentials resolver.</param>
    /// <param name="cultureInfo">Optional culture name used to prioritize locale selection.</param>
    /// <param name="httpClientFactory">Optional HTTP client factory.</param>
    /// <param name="requestOptions">Optional request behavior overrides.</param>
    /// <returns>The updated template configuration.</returns>
    public static IContentTemplateConfiguration AddContentfulTemplate(
        this IContentTemplateConfiguration templates,
        string entryId,
        string fieldName,
        ContentfulCredentialsResolver? creds = null,
        string? cultureInfo = null,
        Func<HttpClient>? httpClientFactory = null,
        ContentfulRequestOptions? requestOptions = null)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return templates.AddContentfulTemplate(entryId, creds, renderer: null, cultureInfo, httpClientFactory, requestOptions);
        }

        return templates.AddContentfulTemplate(
            entryId,
            creds,
            CreateFieldRenderer(fieldName, cultureInfo),
            cultureInfo,
            httpClientFactory,
            requestOptions);
    }

    /// <summary>
    /// Registers a Contentful template using a context-based entry resolver.
    /// </summary>
    /// <param name="templates">Template configuration to update.</param>
    /// <param name="entryResolver">Resolver that returns the entry ID for the current dispatch context.</param>
    /// <param name="creds">Optional credentials resolver.</param>
    /// <param name="renderer">Optional renderer to extract the template from the entry.</param>
    /// <param name="cultureInfo">Optional culture name used to prioritize locale selection.</param>
    /// <param name="httpClientFactory">Optional HTTP client factory.</param>
    /// <param name="requestOptions">Optional request behavior overrides.</param>
    /// <returns>The updated template configuration.</returns>
    public static IContentTemplateConfiguration AddContentfulTemplate(
        this IContentTemplateConfiguration templates,
        Func<IDispatchCommunicationContext, string> entryResolver,
        ContentfulCredentialsResolver? creds = null,
        ContentfulTemplateRenderer? renderer = null,
        string? cultureInfo = null,
        Func<HttpClient>? httpClientFactory = null,
        ContentfulRequestOptions? requestOptions = null)
    {
        var c = creds ?? DefaultCredentialResolver
            ?? throw new InvalidOperationException("No Contentful credentials resolver configured.");
        var options = requestOptions ?? DefaultRequestOptions ?? new ContentfulRequestOptions();

        templates.TemplateRegistrations.Add(
            new ContentfulTemplateRegistration(
                ctx => Task.FromResult(entryResolver(ctx)),
                c,
                renderer ?? DefaultRenderer,
                cultureInfo,
                httpClientFactory,
                options));

        return templates;
    }

    /// <summary>
    /// Registers a Contentful template using a context-based entry resolver and field name.
    /// </summary>
    /// <param name="templates">Template configuration to update.</param>
    /// <param name="entryResolver">Resolver that returns the entry ID for the current dispatch context.</param>
    /// <param name="fieldName">Field name to extract from the entry.</param>
    /// <param name="creds">Optional credentials resolver.</param>
    /// <param name="cultureInfo">Optional culture name used to prioritize locale selection.</param>
    /// <param name="httpClientFactory">Optional HTTP client factory.</param>
    /// <param name="requestOptions">Optional request behavior overrides.</param>
    /// <returns>The updated template configuration.</returns>
    public static IContentTemplateConfiguration AddContentfulTemplate(
        this IContentTemplateConfiguration templates,
        Func<IDispatchCommunicationContext, string> entryResolver,
        string fieldName,
        ContentfulCredentialsResolver? creds = null,
        string? cultureInfo = null,
        Func<HttpClient>? httpClientFactory = null,
        ContentfulRequestOptions? requestOptions = null)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return templates.AddContentfulTemplate(entryResolver, creds, renderer: null, cultureInfo, httpClientFactory, requestOptions);
        }

        return templates.AddContentfulTemplate(
            entryResolver,
            creds,
            CreateFieldRenderer(fieldName, cultureInfo),
            cultureInfo,
            httpClientFactory,
            requestOptions);
    }

    /// <summary>
    /// Registers a Contentful template field with optional renderer fallback.
    /// </summary>
    /// <param name="templates">Template configuration to update.</param>
    /// <param name="entryId">Contentful entry identifier.</param>
    /// <param name="fieldName">Field name to extract from the entry.</param>
    /// <param name="creds">Optional credentials resolver.</param>
    /// <param name="renderer">Optional renderer used as a fallback.</param>
    /// <param name="cultureInfo">Optional culture name used to prioritize locale selection.</param>
    /// <param name="httpClientFactory">Optional HTTP client factory.</param>
    /// <param name="requestOptions">Optional request behavior overrides.</param>
    /// <returns>The updated template configuration.</returns>
    public static IContentTemplateConfiguration AddContentfulTemplateField(
        this IContentTemplateConfiguration templates,
        string entryId,
        string fieldName,
        ContentfulCredentialsResolver? creds = null,
        ContentfulTemplateRenderer? renderer = null,
        string? cultureInfo = null,
        Func<HttpClient>? httpClientFactory = null,
        ContentfulRequestOptions? requestOptions = null)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return templates.AddContentfulTemplate(entryId, creds, renderer ?? DefaultRenderer, cultureInfo, httpClientFactory, requestOptions);
        }

        var baseRenderer = renderer ?? DefaultRenderer;
        var selectedRenderer = baseRenderer != null
            ? WrapRenderer(baseRenderer, fieldName, cultureInfo)
            : CreateFieldRenderer(fieldName, cultureInfo);

        return templates.AddContentfulTemplate(entryId, creds, selectedRenderer, cultureInfo, httpClientFactory, requestOptions);
    }

    /// <summary>
    /// Registers a Contentful template field using a context-based entry resolver.
    /// </summary>
    /// <param name="templates">Template configuration to update.</param>
    /// <param name="entryResolver">Resolver that returns the entry ID for the current dispatch context.</param>
    /// <param name="fieldName">Field name to extract from the entry.</param>
    /// <param name="creds">Optional credentials resolver.</param>
    /// <param name="renderer">Optional renderer used as a fallback.</param>
    /// <param name="cultureInfo">Optional culture name used to prioritize locale selection.</param>
    /// <param name="httpClientFactory">Optional HTTP client factory.</param>
    /// <param name="requestOptions">Optional request behavior overrides.</param>
    /// <returns>The updated template configuration.</returns>
    public static IContentTemplateConfiguration AddContentfulTemplateField(
        this IContentTemplateConfiguration templates,
        Func<IDispatchCommunicationContext, string> entryResolver,
        string fieldName,
        ContentfulCredentialsResolver? creds = null,
        ContentfulTemplateRenderer? renderer = null,
        string? cultureInfo = null,
        Func<HttpClient>? httpClientFactory = null,
        ContentfulRequestOptions? requestOptions = null)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return templates.AddContentfulTemplate(entryResolver, creds, renderer ?? DefaultRenderer, cultureInfo, httpClientFactory, requestOptions);
        }

        var baseRenderer = renderer ?? DefaultRenderer;
        var selectedRenderer = baseRenderer != null
            ? WrapRenderer(baseRenderer, fieldName, cultureInfo)
            : CreateFieldRenderer(fieldName, cultureInfo);

        return templates.AddContentfulTemplate(entryResolver, creds, selectedRenderer, cultureInfo, httpClientFactory, requestOptions);
    }

    private static ContentfulTemplateRenderer WrapRenderer(ContentfulTemplateRenderer baseRenderer, string fieldName, string? cultureInfo)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
            return baseRenderer;

        var fieldRenderer = CreateFieldRenderer(fieldName, cultureInfo);

        return async (ctx, entry) =>
        {
            var fieldValue = await fieldRenderer(ctx, entry).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(fieldValue))
            {
                return fieldValue;
            }

            return await baseRenderer(ctx, entry).ConfigureAwait(false);
        };
    }

    private static ContentfulTemplateRenderer CreateFieldRenderer(string fieldName, string? cultureInfo)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return (_, _) => Task.FromResult<string?>(null);
        }

        var localePreference = BuildLocalePriority(CreateCulture(cultureInfo));

        return (_, entry) =>
        {
            var normalized = NormalizeEntry(entry.RootElement);
            if (normalized.TryGetProperty("fields", out var fields) &&
                TryReadField(fields, fieldName, localePreference, out var value))
            {
                return Task.FromResult(value);
            }

            return Task.FromResult<string?>(null);
        };
    }

    private static CultureInfo CreateCulture(string? cultureInfo)
    {
        if (string.IsNullOrWhiteSpace(cultureInfo))
        {
            return CultureInfo.CurrentCulture;
        }

        return new CultureInfo(cultureInfo);
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
}
