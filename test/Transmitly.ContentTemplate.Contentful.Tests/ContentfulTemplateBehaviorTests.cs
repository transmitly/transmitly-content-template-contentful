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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Transmitly.Template.Configuration;

namespace Transmitly.ContentTemplate.Contentful.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ContentfulTemplateBehaviorTests
{
	[TestMethod]
	public async Task DefaultRenderer_UsesFieldPreferenceOrder()
	{
		const string json = "{\"fields\":{\"template\":{\"en-US\":\"\"},\"htmlTemplate\":{\"en-US\":\"<p>Hi</p>\"}}}";
		var (registration, handler) = CreateRegistration(CreateJsonResponse(HttpStatusCode.OK, json));
		var result = await registration.GetContentAsync(CreateContext("en-US"));

		Assert.AreEqual("<p>Hi</p>", result);
		Assert.AreEqual(1, handler.CallCount);
	}

	[TestMethod]
	public async Task DefaultRenderer_UsesLocalePreference()
	{
		const string json = "{\"fields\":{\"template\":{\"fr\":\"Salut\",\"en\":\"Hello\"}}}";
		var (registration, handler) = CreateRegistration(CreateJsonResponse(HttpStatusCode.OK, json), cultureName: "en-US");
		var result = await registration.GetContentAsync(CreateContext("en-US"));

		Assert.AreEqual("Hello", result);
		Assert.AreEqual(1, handler.CallCount);
	}

	[TestMethod]
	public async Task DefaultRenderer_NormalizesItemsArray()
	{
		const string json = "{\"items\":[{\"fields\":{\"template\":{\"en-US\":\"FromItems\"}}}]}";
		var (registration, handler) = CreateRegistration(CreateJsonResponse(HttpStatusCode.OK, json));
		var result = await registration.GetContentAsync(CreateContext("en-US"));

		Assert.AreEqual("FromItems", result);
		Assert.AreEqual(1, handler.CallCount);
	}

	[TestMethod]
	public async Task DefaultRenderer_ReadsNonStringValues()
	{
		const string json = "{\"fields\":{\"template\":{\"en-US\":123}}}";
		var (registration, handler) = CreateRegistration(CreateJsonResponse(HttpStatusCode.OK, json));
		var result = await registration.GetContentAsync(CreateContext("en-US"));

		Assert.AreEqual("123", result);
		Assert.AreEqual(1, handler.CallCount);
	}

	[TestMethod]
	public async Task DefaultRenderer_ReadsArrayValues()
	{
		const string json = "{\"fields\":{\"template\":{\"en-US\":[\"\", \"Hi\"]}}}";
		var (registration, handler) = CreateRegistration(CreateJsonResponse(HttpStatusCode.OK, json));
		var result = await registration.GetContentAsync(CreateContext("en-US"));

		Assert.AreEqual("Hi", result);
		Assert.AreEqual(1, handler.CallCount);
	}

	[TestMethod]
	public async Task DefaultRenderer_UsesCultureOverride()
	{
		const string json = "{\"fields\":{\"template\":{\"fr\":\"Salut\",\"en\":\"Hello\"}}}";
		var (registration, handler) = CreateRegistration(CreateJsonResponse(HttpStatusCode.OK, json), cultureName: "fr-FR");
		var result = await registration.GetContentAsync(CreateContext("en-US"));

		Assert.AreEqual("Salut", result);
		Assert.AreEqual(1, handler.CallCount);
	}

	[TestMethod]
	public async Task RetryPolicy_UsesShouldRetryResponsePredicate()
	{
		var (registration, handler) = CreateRegistration(
			attempt => attempt == 1
				? Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError))
				: Task.FromResult(CreateJsonResponse(HttpStatusCode.OK, "{\"fields\":{\"template\":{\"en-US\":\"Hello\"}}}")),
			options: new ContentfulRequestOptions
			{
				MaxRetries = 3,
				BaseRetryDelay = TimeSpan.Zero,
				MaxRetryDelay = TimeSpan.Zero,
				ShouldRetryResponse = _ => false,
				ShouldRetryException = _ => false
			});

		await Assert.ThrowsExactlyAsync<HttpRequestException>(() => registration.GetContentAsync(CreateContext("en-US")));
		Assert.AreEqual(1, handler.CallCount);
	}

	[TestMethod]
	public async Task RetryPolicy_HandlesZeroOrNegativeDelays()
	{
		var (registration, handler) = CreateRegistration(
			attempt => attempt == 1
				? Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError))
				: Task.FromResult(CreateJsonResponse(HttpStatusCode.OK, "{\"fields\":{\"template\":{\"en-US\":\"Hello\"}}}")),
			options: new ContentfulRequestOptions
			{
				MaxRetries = 1,
				BaseRetryDelay = TimeSpan.Zero,
				MaxRetryDelay = TimeSpan.FromSeconds(-1)
			});

		var result = await registration.GetContentAsync(CreateContext("en-US"));

		Assert.AreEqual("Hello", result);
		Assert.AreEqual(2, handler.CallCount);
	}

	[TestMethod]
	public async Task RetryPolicy_UsesShouldRetryExceptionPredicate()
	{
		var (registration, handler) = CreateRegistration(
			attempt => attempt == 1
				? throw new HttpRequestException("boom")
				: Task.FromResult(CreateJsonResponse(HttpStatusCode.OK, "{\"fields\":{\"template\":{\"en-US\":\"Hello\"}}}")),
			options: new ContentfulRequestOptions
			{
				MaxRetries = 3,
				BaseRetryDelay = TimeSpan.Zero,
				MaxRetryDelay = TimeSpan.Zero,
				ShouldRetryException = _ => false
			});

		await Assert.ThrowsExactlyAsync<HttpRequestException>(() => registration.GetContentAsync(CreateContext("en-US")));
		Assert.AreEqual(1, handler.CallCount);
	}

	[TestMethod]
	public async Task AddContentfulTemplateField_FallsBackToBaseRenderer()
	{
		const string json = "{\"fields\":{\"template\":{\"en-US\":\"Ignored\"}}}";
		var handler = new SequenceHttpMessageHandler((_, __, ___) =>
			Task.FromResult(CreateJsonResponse(HttpStatusCode.OK, json)));

		var config = new TestContentTemplateConfiguration();
		config.AddContentfulTemplateField(
			entryId: "entry",
			fieldName: "missing",
			creds: _ => Task.FromResult(new ContentfulCredentials("space", "token")),
			renderer: (_, __) => Task.FromResult<string?>("fallback"),
			cultureInfo: "en-US",
			httpClientFactory: () => new HttpClient(handler),
			requestOptions: new ContentfulRequestOptions { MaxRetries = 0 });

		var registration = (ContentfulTemplateRegistration)config.TemplateRegistrations.Single();
		var result = await registration.GetContentAsync(CreateContext("en-US"));

		Assert.AreEqual("fallback", result);
		Assert.AreEqual(1, handler.CallCount);
	}

	[TestMethod]
	public async Task AddContentfulTemplateField_WhenFieldMissing_ReturnsNull()
	{
		const string json = "{\"fields\":{\"template\":{\"en-US\":\"Ignored\"}}}";
		var handler = new SequenceHttpMessageHandler((_, __, ___) =>
			Task.FromResult(CreateJsonResponse(HttpStatusCode.OK, json)));

		var config = new TestContentTemplateConfiguration();
		config.AddContentfulTemplate(
			entryId: "entry",
			fieldName: "missing",
			creds: _ => Task.FromResult(new ContentfulCredentials("space", "token")),
			cultureInfo: "en-US",
			httpClientFactory: () => new HttpClient(handler),
			requestOptions: new ContentfulRequestOptions { MaxRetries = 0 });

		var registration = (ContentfulTemplateRegistration)config.TemplateRegistrations.Single();
		var result = await registration.GetContentAsync(CreateContext("en-US"));

		Assert.IsNull(result);
		Assert.AreEqual(1, handler.CallCount);
	}

	[TestMethod]
	public async Task AddContentfulTemplateField_WhenFieldNameWhitespace_UsesBaseRenderer()
	{
		const string json = "{\"fields\":{\"template\":{\"en-US\":\"Ignored\"}}}";
		var handler = new SequenceHttpMessageHandler((_, __, ___) =>
			Task.FromResult(CreateJsonResponse(HttpStatusCode.OK, json)));

		var config = new TestContentTemplateConfiguration();
		config.AddContentfulTemplateField(
			entryId: "entry",
			fieldName: " ",
			creds: _ => Task.FromResult(new ContentfulCredentials("space", "token")),
			renderer: (_, __) => Task.FromResult<string?>("base"),
			cultureInfo: "en-US",
			httpClientFactory: () => new HttpClient(handler),
			requestOptions: new ContentfulRequestOptions { MaxRetries = 0 });

		var registration = (ContentfulTemplateRegistration)config.TemplateRegistrations.Single();
		var result = await registration.GetContentAsync(CreateContext("en-US"));

		Assert.AreEqual("base", result);
		Assert.AreEqual(1, handler.CallCount);
	}

	private static (ContentfulTemplateRegistration Registration, SequenceHttpMessageHandler Handler) CreateRegistration(
		Func<int, Task<HttpResponseMessage>> handler,
		ContentfulRequestOptions? options = null,
		string cultureName = "en-US")
	{
		var sequenceHandler = new SequenceHttpMessageHandler((attempt, _, __) => handler(attempt));
		var registration = new ContentfulTemplateRegistration(
			_ => Task.FromResult("entry"),
			_ => Task.FromResult(new ContentfulCredentials("space", "token")),
			renderer: null,
			cultureInfo: cultureName,
			httpClientFactory: () => new HttpClient(sequenceHandler),
			requestOptions: options ?? new ContentfulRequestOptions
			{
				MaxRetries = 0,
				BaseRetryDelay = TimeSpan.Zero,
				MaxRetryDelay = TimeSpan.Zero
			});

		return (registration, sequenceHandler);
	}

	private static (ContentfulTemplateRegistration Registration, SequenceHttpMessageHandler Handler) CreateRegistration(
		HttpResponseMessage response,
		ContentfulRequestOptions? options = null,
		string cultureName = "en-US")
	{
		return CreateRegistration(_ => Task.FromResult(response), options, cultureName);
	}

	private static HttpResponseMessage CreateJsonResponse(HttpStatusCode statusCode, string json)
		=> new(statusCode)
		{
			Content = new StringContent(json, Encoding.UTF8, "application/json")
		};

	private static IDispatchCommunicationContext CreateContext(string cultureName)
	{
		var mock = new Mock<IDispatchCommunicationContext>(MockBehavior.Strict);
		mock.SetupGet(x => x.CultureInfo).Returns(CultureInfo.GetCultureInfo(cultureName));
		return mock.Object;
	}

	private sealed class TestContentTemplateConfiguration : IContentTemplateConfiguration
	{
		public ICollection<IContentTemplateRegistration> TemplateRegistrations { get; } = [];
	}

	private sealed class SequenceHttpMessageHandler(
		Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
	{
		private int _callCount;

		public int CallCount => _callCount;

		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			var attempt = Interlocked.Increment(ref _callCount);
			return handler(attempt, request, cancellationToken);
		}
	}
}
