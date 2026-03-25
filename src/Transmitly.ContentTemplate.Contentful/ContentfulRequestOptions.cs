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
using System.Net.Http;
using System.Threading;

namespace Transmitly;

/// <summary>
/// Configures HTTP request behavior when fetching Contentful entries.
/// </summary>
public sealed class ContentfulRequestOptions
{
    /// <summary>
    /// Gets or sets the maximum number of retry attempts for transient failures.
    /// </summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>
    /// Gets or sets the base delay used for exponential backoff.
    /// </summary>
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Gets or sets the maximum delay allowed between retry attempts.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets or sets a resolver for cancellation tokens per dispatch context.
    /// </summary>
    public Func<IDispatchCommunicationContext, CancellationToken>? CancellationTokenResolver { get; set; }

    /// <summary>
    /// Gets or sets a predicate that determines whether a response should be retried.
    /// </summary>
    public Func<HttpResponseMessage, bool>? ShouldRetryResponse { get; set; }

    /// <summary>
    /// Gets or sets a predicate that determines whether an exception should be retried.
    /// </summary>
    public Func<Exception, bool>? ShouldRetryException { get; set; }
}
