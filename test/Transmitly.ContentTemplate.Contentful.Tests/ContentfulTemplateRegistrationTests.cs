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
public sealed class ContentfulTemplateRegistrationTests
{
	private const string SampleJson = "{\"fields\":{\"template\":{\"en-US\":\"Hello\"}}}";

	[TestMethod]
	public async Task AddContentfulTemplate_AddsRegistrationAndRenders()
	{
		var handler = new SequenceHttpMessageHandler((_, __, ___) =>
			Task.FromResult(CreateJsonResponse(HttpStatusCode.OK, SampleJson)));
		var config = new TestContentTemplateConfiguration();
		var ctx = CreateContext("en-US");

		config.AddContentfulTemplate(
			entryId: "entry",
			creds: _ => Task.FromResult(new ContentfulCredentials("space", "token")),
			renderer: null,
			cultureInfo: "en-US",
			httpClientFactory: () => new HttpClient(handler),
			requestOptions: new ContentfulRequestOptions { MaxRetries = 0 });

		Assert.AreEqual(1, config.TemplateRegistrations.Count);
		Assert.IsInstanceOfType<ContentfulTemplateRegistration>(config.TemplateRegistrations.Single());

		var registration = (ContentfulTemplateRegistration)config.TemplateRegistrations.Single();
		var result = await registration.GetContentAsync(ctx);

		Assert.AreEqual("Hello", result);
		Assert.AreEqual(1, handler.CallCount);
	}

	[TestMethod]
	public async Task AddContentfulTemplate_UsesDefaultRequestOptionsWhenNotProvided()
	{
		var handler = new SequenceHttpMessageHandler((attempt, _, __) =>
		{
			if (attempt <= 2)
			{
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
			}

			return Task.FromResult(CreateJsonResponse(HttpStatusCode.OK, SampleJson));
		});
		var config = new TestContentTemplateConfiguration();
		var ctx = CreateContext("en-US");

		ContentfulTemplateExtensions.DefaultRequestOptions = new ContentfulRequestOptions
		{
			MaxRetries = 1,
			BaseRetryDelay = TimeSpan.Zero,
			MaxRetryDelay = TimeSpan.Zero
		};

		try
		{
			config.AddContentfulTemplate(
				entryId: "entry",
				creds: _ => Task.FromResult(new ContentfulCredentials("space", "token")),
				httpClientFactory: () => new HttpClient(handler));

			var registration = (ContentfulTemplateRegistration)config.TemplateRegistrations.Single();
			await Assert.ThrowsExactlyAsync<HttpRequestException>(() => registration.GetContentAsync(ctx));

			Assert.AreEqual(2, handler.CallCount);
		}
		finally
		{
			ContentfulTemplateExtensions.DefaultRequestOptions = new ContentfulRequestOptions();
		}
	}

	[TestMethod]
	public async Task AddContentfulTemplate_OverridesDefaultRequestOptions()
	{
		var handler = new SequenceHttpMessageHandler((attempt, _, __) =>
		{
			if (attempt <= 2)
			{
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
			}

			return Task.FromResult(CreateJsonResponse(HttpStatusCode.OK, SampleJson));
		});
		var config = new TestContentTemplateConfiguration();
		var ctx = CreateContext("en-US");

		ContentfulTemplateExtensions.DefaultRequestOptions = new ContentfulRequestOptions { MaxRetries = 0 };

		try
		{
			config.AddContentfulTemplate(
				entryId: "entry",
				creds: _ => Task.FromResult(new ContentfulCredentials("space", "token")),
				httpClientFactory: () => new HttpClient(handler),
				requestOptions: new ContentfulRequestOptions
				{
					MaxRetries = 2,
					BaseRetryDelay = TimeSpan.Zero,
					MaxRetryDelay = TimeSpan.Zero
				});

			var registration = (ContentfulTemplateRegistration)config.TemplateRegistrations.Single();
			var result = await registration.GetContentAsync(ctx);

			Assert.AreEqual("Hello", result);
			Assert.AreEqual(3, handler.CallCount);
		}
		finally
		{
			ContentfulTemplateExtensions.DefaultRequestOptions = new ContentfulRequestOptions();
		}
	}

	[TestMethod]
	public async Task GetContentAsync_HonorsCancellationToken()
	{
		using var cts = new CancellationTokenSource();
		cts.Cancel();

		var handler = new SequenceHttpMessageHandler((_, __, ct) =>
		{
			ct.ThrowIfCancellationRequested();
			return Task.FromResult(CreateJsonResponse(HttpStatusCode.OK, SampleJson));
		});

		var registration = new ContentfulTemplateRegistration(
			_ => Task.FromResult("entry"),
			_ => Task.FromResult(new ContentfulCredentials("space", "token")),
			renderer: null,
			cultureInfo: "en-US",
			httpClientFactory: () => new HttpClient(handler),
			requestOptions: new ContentfulRequestOptions
			{
				MaxRetries = 2,
				BaseRetryDelay = TimeSpan.Zero,
				MaxRetryDelay = TimeSpan.Zero,
				CancellationTokenResolver = _ => cts.Token
			});

		var ctx = CreateContext("en-US");
#if NETFRAMEWORK
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => registration.GetContentAsync(ctx));
#else
		await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => registration.GetContentAsync(ctx));
#endif

		Assert.AreEqual(1, handler.CallCount);
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
