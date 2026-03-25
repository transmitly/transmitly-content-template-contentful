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

using System.Text.Json;
using System.Threading.Tasks;
namespace Transmitly;

/// <summary>
/// Represents Contentful credentials required to access entries.
/// </summary>
/// <param name="SpaceId">Contentful space identifier.</param>
/// <param name="Token">Contentful delivery API token.</param>
/// <param name="Environment">Optional environment name; defaults to "master".</param>
public record ContentfulCredentials(string SpaceId, string Token, string? Environment = "master");

/// <summary>
/// Resolves Contentful credentials for the current dispatch context.
/// </summary>
/// <param name="ctx">Dispatch context for the outgoing message.</param>
/// <returns>Contentful credentials to use for the request.</returns>
public delegate Task<ContentfulCredentials> ContentfulCredentialsResolver(IDispatchCommunicationContext ctx);

/// <summary>
/// Renders template content from a Contentful entry document.
/// </summary>
/// <param name="ctx">Dispatch context for the outgoing message.</param>
/// <param name="entry">Contentful entry JSON document.</param>
/// <returns>Rendered template content, or <see langword="null" /> when no content is found.</returns>
public delegate Task<string?> ContentfulTemplateRenderer(IDispatchCommunicationContext ctx, JsonDocument entry);
