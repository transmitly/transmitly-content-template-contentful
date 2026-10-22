# Transmitly.ContentTemplate.Contentful

`Transmitly.ContentTemplate.Contentful` lets the core [Transmitly](https://github.com/transmitly/transmitly) library load message templates from [Contentful](https://www.contentful.com/) entries at dispatch time.

Use it anywhere Transmitly accepts a content template (email subject and bodies, SMS and voice messages, push titles and bodies) in place of `AddStringTemplate(...)` or `AddEmbeddedResourceTemplate(...)`. Content editors change the copy in Contentful; your pipelines stay the same.

This package is typically used alongside:

- `Transmitly`
- a Transmitly channel provider such as `Transmitly.ChannelProvider.Smtp`, `Transmitly.ChannelProvider.SendGrid`, or `Transmitly.ChannelProvider.Twilio`
- a template engine such as `Transmitly.TemplateEngine.Fluid` or `Transmitly.TemplateEngine.Scriban`, when your Contentful content contains placeholders

## Install

```shell
dotnet add package Transmitly
dotnet add package Transmitly.ContentTemplate.Contentful
dotnet add package Transmitly.TemplateEngine.Fluid
dotnet add package Transmitly.ChannelProvider.Smtp
```

## Quick Start

Given a Contentful entry with `subject`, `htmlBody`, and `textBody` fields:

| Field | Value |
| --- | --- |
| `subject` | `Order {{orderNumber}} has shipped` |
| `htmlBody` | `<p>Hello {{firstName}}, your order is on its way.</p>` |
| `textBody` | `Hello {{firstName}}, your order is on its way.` |

```csharp
using Transmitly;
using Transmitly.ChannelProvider.Smtp.Configuration;

const string orderShippedEntryId = "4mY1bZ0wXcQ8fK2nL7pRsT";

ContentfulTemplateExtensions.UseDefaultCredentials(
	spaceId: "your-space-id",
	apiKey: "your-content-delivery-api-token");

ICommunicationsClient client = new CommunicationsClientBuilder()
	.AddSmtpSupport(options =>
	{
		options.Host = "smtp.example.com";
		options.Port = 587;
		options.UserName = "smtp-user";
		options.Password = "smtp-password";
	})
	.AddFluidTemplateEngine()
	.AddPipeline("order-shipped", pipeline =>
	{
		pipeline.AddEmail("orders@my.app".AsIdentityAddress("Orders"), email =>
		{
			email.Subject.AddContentfulTemplate(orderShippedEntryId, "subject");
			email.HtmlBody.AddContentfulTemplate(orderShippedEntryId, "htmlBody");
			email.TextBody.AddContentfulTemplate(orderShippedEntryId, "textBody");
		});
	})
	.BuildClient();

var result = await client.DispatchAsync(
	"order-shipped",
	"customer@example.com",
	new
	{
		firstName = "Ava",
		orderNumber = "A10023"
	});
```

The fetched text goes through the registered template engine, so the `{{...}}` placeholders render against the dispatch model. Without a template engine, the text is sent as-is.

## Registration

All overloads extend `IContentTemplateConfiguration`, so they work on any template slot (`email.Subject`, `email.HtmlBody`, `sms.Message`, `push.Body`, ...).

| Method | Content used |
| --- | --- |
| `AddContentfulTemplate(entryId)` | The default renderer (see [Choosing Content](#choosing-content)). |
| `AddContentfulTemplate(entryId, fieldName)` | The named field only. `null` when the field is missing or empty. |
| `AddContentfulTemplate(entryId, renderer: ...)` | Your own `ContentfulTemplateRenderer`. |
| `AddContentfulTemplateField(entryId, fieldName, renderer: ...)` | The named field, falling back to the renderer (or `DefaultRenderer`) when the field is empty. |

Each method also has an overload that takes `Func<IDispatchCommunicationContext, string>` instead of a fixed entry ID, so the entry can be chosen per dispatch. For example, to keep entry IDs in configuration:

```csharp
email.HtmlBody.AddContentfulTemplate(ctx => entryIds[ctx.PipelineIntent], "htmlBody");
```

Every overload also accepts optional `creds`, `cultureInfo`, `httpClientFactory`, and `requestOptions` arguments, described below.

## Credentials

Credentials come from a `ContentfulCredentialsResolver`, which runs on every fetch.

Set a default for all registrations:

```csharp
ContentfulTemplateExtensions.UseDefaultCredentials("your-space-id", "your-content-delivery-api-token", environment: "master");
```

Or pass a resolver to a single registration, for example to load the token from a secret store:

```csharp
email.HtmlBody.AddContentfulTemplate(
	orderShippedEntryId,
	"htmlBody",
	creds: async ctx => new ContentfulCredentials(
		SpaceId: "your-space-id",
		Token: await secrets.GetAsync("contentful-delivery-token"),
		Environment: "staging"));
```

- Use a **Content Delivery API** token. Requests go to `https://cdn.contentful.com`, so only published content is returned. The Preview API is not supported.
- `Environment` defaults to `master`.
- Defaults are read when the template is registered. Call `UseDefaultCredentials` (or set `DefaultCredentialResolver`) before building pipelines. Registering a template with no resolver throws `InvalidOperationException`.

## Choosing Content

When no field name or renderer is given, the default renderer returns the first non-empty field from this list:

`template`, `htmlTemplate`, `htmlBody`, `body`, `textTemplate`, `textBody`, `content`, `message`

If none of those exist, it returns the first non-empty field of any name. Because this picks the same field for every slot, pass a `fieldName` when one entry holds the subject and the body.

Field values are read as follows:

- Text: returned as-is.
- Numbers and booleans: returned as their JSON text.
- Lists: the first non-empty item.
- Locale-keyed objects (`{ "en-US": "...", "fr": "..." }`): picked by locale (see [Localization](#localization)).

Rich Text fields are not converted to HTML. Store templates in Short or Long text fields, or write a renderer.

### Custom renderer

A renderer receives the dispatch context and the Contentful entry JSON (`sys` and `fields`) and returns the template text:

```csharp
using System.Text.Json;

email.HtmlBody.AddContentfulTemplate(
	orderShippedEntryId,
	renderer: (ctx, entry) =>
	{
		var fields = entry.RootElement.GetProperty("fields");
		var heading = fields.GetProperty("heading").GetString();
		var body = fields.GetProperty("body").GetString();
		return Task.FromResult<string?>($"<h1>{heading}</h1>{body}");
	});
```

Set `ContentfulTemplateExtensions.DefaultRenderer` to use a renderer for every registration that does not pass one.

## Localization

The dispatch culture is sent to Contentful as the `locale` query parameter, so one registration serves every locale in your space:

```csharp
await client.DispatchAsync(
	"order-shipped",
	"client@example.fr",
	new { firstName = "Chloé", orderNumber = "A10024" },
	cultureInfo: "fr-FR");
```

Use culture names that match the locale codes in your space.

The `cultureInfo` argument on a registration works like it does for `AddStringTemplate(...)`: Transmitly uses the registration whose culture matches the dispatch, and falls back to the one registered without a culture. It also sets the locale order used for locale-keyed values: that culture, its two-letter language, `en-US`, `en`, then the first non-empty value.

## Request Options

`ContentfulRequestOptions` controls retries and cancellation. Pass it per registration with `requestOptions:`, or set `ContentfulTemplateExtensions.DefaultRequestOptions`.

| Property | Default | Purpose |
| --- | --- | --- |
| `MaxRetries` | `2` | Retries after the first attempt. |
| `BaseRetryDelay` | `200 ms` | Base delay for exponential backoff. |
| `MaxRetryDelay` | `2 s` | Upper limit for the backoff delay. |
| `ShouldRetryResponse` | `408`, `429`, `5xx` | Decides which responses are retried. |
| `ShouldRetryException` | `HttpRequestException`, `TaskCanceledException` | Decides which exceptions are retried. |
| `CancellationTokenResolver` | none | Supplies a cancellation token per dispatch. |

A `Retry-After` header on the response takes precedence over the backoff delay.

```csharp
email.HtmlBody.AddContentfulTemplate(
	orderShippedEntryId,
	"htmlBody",
	requestOptions: new ContentfulRequestOptions
	{
		MaxRetries = 4,
		MaxRetryDelay = TimeSpan.FromSeconds(5)
	});
```

### HTTP client

By default, all registrations share one pooled `HttpClient`. Pass `httpClientFactory:` to supply your own, for example from `IHttpClientFactory`:

```csharp
email.HtmlBody.AddContentfulTemplate(
	orderShippedEntryId,
	"htmlBody",
	httpClientFactory: () => httpClientFactory.CreateClient("contentful"));
```

The factory is called on every fetch, and the package does not dispose the returned client.

## Behavior Notes

- Entries are fetched on every render, with no caching. An email with a Contentful subject, HTML body, and text body makes three requests per dispatch.
- The token is sent as the `access_token` query parameter.
- A request that still fails after retries throws `HttpRequestException`. A missing entry (`404`) is not retried.
- Linked entries and assets are not resolved.
- When the field or renderer finds nothing, the registration returns `null` content to the template engine.
- `DefaultCredentialResolver`, `DefaultRenderer`, and `DefaultRequestOptions` are static and apply to the whole process.

## Related Packages

- [Transmitly](https://github.com/transmitly/transmitly)
- [Transmitly.TemplateEngine.Fluid](https://github.com/transmitly/transmitly-template-engine-fluid)
- [Transmitly.TemplateEngine.Scriban](https://github.com/transmitly/transmitly-template-engine-scriban)

---
_Copyright (c) Code Impressions, LLC. This open-source project is sponsored and maintained by Code Impressions and is licensed under the [Apache License, Version 2.0](http://apache.org/licenses/LICENSE-2.0.html)._
