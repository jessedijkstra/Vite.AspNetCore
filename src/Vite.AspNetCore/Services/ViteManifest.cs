﻿// Copyright (c) 2023 Quetzal Rivera.
// Licensed under the MIT License, See LICENCE in the project root for license information.

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections;
using System.Text.Json;
using Vite.AspNetCore.Abstractions;

namespace Vite.AspNetCore.Services;

/// <summary>
/// This class is used to read the manifest.json file generated by Vite.
/// </summary>
public sealed class ViteManifest : IViteManifest
{
	// The logger is used to log messages.
	private readonly ILogger<ViteManifest> _logger;
	// The chunks dictionary is used to store the chunks read from the manifest.json file.
	private readonly IReadOnlyDictionary<string, ViteChunk> _chunks;

	private static bool _warnAboutManifestOnce = true;

	/// <summary>
	/// Initializes a new instance of the <see cref="ViteManifest"/> class.
	/// </summary>
	/// <param name="logger">The service used to log messages.</param>
	/// <param name="options">The vite configuration options.</param>
	/// <param name="environment">Information about the web hosting environment.</param>
	public ViteManifest(ILogger<ViteManifest> logger, IOptions<ViteOptions> options, IWebHostEnvironment environment)
	{
		this._logger = logger;

		// If the middleware is enabled, don't read the manifest.json file.
		if (ViteStatusService.IsMiddlewareRegistered)
		{
			if (_warnAboutManifestOnce)
			{
				logger.LogInformation("The manifest file won't be read because the middleware is enabled. The service will always return null chunks");
				_warnAboutManifestOnce = false;
			}

			this._chunks = new Dictionary<string, ViteChunk>();
			return;
		}

		// Get vite options.
		var viteOptions = options.Value;

		// Read tha name of the manifest file from the configuration.
		var manifestName = viteOptions.Manifest;

		// If the manifest file is in a subfolder, get the subfolder path.
		var basePath = viteOptions.Base?.TrimStart('/');

		// Get the manifest.json file path
		var manifestPath = Path.Combine(environment.WebRootPath, basePath ?? string.Empty, manifestName);

		// If the manifest file doesn't exist, try to remove the ".vite/" prefix from the manifest file name. The default name for Vite 5 is ".vite/manifest.json" but for Vite 4 is "manifest.json".
		if (!File.Exists(manifestPath) && manifestName.StartsWith(".vite"))
		{
			// Get the manifest.json file name without the ".vite/" prefix.
			var legacyManifestName = Path.GetFileName(manifestName);

			// Get the manifest.json file path
			manifestPath = Path.Combine(environment.WebRootPath, basePath ?? string.Empty, legacyManifestName);
		}

		// If the manifest.json file exists, deserialize it into a dictionary.
		if (File.Exists(manifestPath))
		{
			// Read the manifest.json file and deserialize it into a dictionary
			this._chunks = JsonSerializer.Deserialize<IReadOnlyDictionary<string, ViteChunk>>(File.ReadAllBytes(manifestPath), new JsonSerializerOptions()
			{
				PropertyNameCaseInsensitive = true
			})!;

			// If the base path is not null, add it to the chunks keys and values.
			if (!string.IsNullOrEmpty(basePath))
			{
				// Create a new dictionary.
				var chunks = new Dictionary<string, ViteChunk>();

				// Iterate through the chunks.
				foreach (var chunk in this._chunks)
				{
					// Add the base path to the key.
					var key = CombineUri(basePath, chunk.Key);

					// Add the base path to the value.
					var value = chunk.Value with
					{
						Css = chunk.Value.Css?.Select(css => CombineUri(basePath, css)),
						File = CombineUri(basePath, chunk.Value.File),
						Imports = chunk.Value.Imports?.Select(imports => CombineUri(basePath, imports)),
						Src = string.IsNullOrEmpty(chunk.Value.Src) ? null : CombineUri(basePath, chunk.Value.Src),
						Assets = chunk.Value.Assets?.Select(assets => CombineUri(basePath, assets)),
						DynamicImports = chunk.Value.DynamicImports?.Select(dynamicImports => CombineUri(basePath, dynamicImports)),
						IsDynamicEntry = chunk.Value.IsDynamicEntry,
						IsEntry = chunk.Value.IsEntry,
					};

					// Add the chunk to the dictionary.
					chunks.Add(key, value);
				}

				// Replace the chunks dictionary.
				this._chunks = chunks;
			}
		}
		else
		{
			if (_warnAboutManifestOnce)
			{
				logger.LogWarning(
					"The manifest file was not found. Did you forget to build the assets? ('npm run build')");
				_warnAboutManifestOnce = false;
			}

			// Create an empty dictionary.
			this._chunks = new Dictionary<string, ViteChunk>();
		}
	}

	/// <summary>
	/// Gets the Vite chunk for the specified entry point if it exists.
	/// If Dev Server is enabled, this will always return <see langword="null"/>.
	/// </summary>
	/// <param name="key"></param>
	/// <returns>The chunk if it exists, otherwise <see langword="null"/>.</returns>
	public IViteChunk? this[string key]
	{
		get
		{
			if (ViteStatusService.IsMiddlewareRegistered)
			{
				this._logger.LogWarning("Attempted to get a record from the manifest file while the Vite development server is activated. Null was returned");
				return null;
			}

			// Try to get the chunk from the dictionary.
			if (!this._chunks.TryGetValue(key, out var chunk))
			{
				this._logger.LogWarning("The chunk '{Key}' was not found", key);
			}

			return chunk;
		}
	}

	/// <inheritdoc/>
	IEnumerator<IViteChunk> IEnumerable<IViteChunk>.GetEnumerator()
	{
		return this._chunks.Values.GetEnumerator();
	}

	/// <inheritdoc/>
	IEnumerator IEnumerable.GetEnumerator()
	{
		return this._chunks.Values.GetEnumerator();
	}

	/// <inheritdoc/>
	IEnumerable<string> IViteManifest.Keys => this._chunks.Keys;

	/// <inheritdoc/>
	bool IViteManifest.ContainsKey(string key)
	{
		return this._chunks.ContainsKey(key);
	}

	/// <summary>
	/// Combines the specified URI and paths.
	/// </summary>
	/// <param name="uri">The base URI.</param>
	/// <param name="path">The path to combine.</param>
	/// <returns>A new URI.</returns>
	private static string CombineUri(string uri, string path)
	{
		if (string.IsNullOrEmpty(uri))
		{
			return path;
		}

		return uri.EndsWith('/') ? uri + path : uri + "/" + path;
	}
}
