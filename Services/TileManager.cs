using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;


namespace OSMTileProxy.Services
{

	public class TileManager
	{
		private readonly TimeSpan _expiration = new(7, 0, 0, 0); // seven days - recommended expiration time for cached tiles
		private readonly string _cacheFolder;
		private readonly Dictionary<string, Provider> _providers;
		private readonly ConcurrentDictionary<string, int> _tilesToLoad = new();

		private event EventHandler<TileReadyEventArgs> TileReady = null;


		class Provider
		{
			private string _contentType;
			private string _fileType;

			public string Id { get; set; }
			public string Url { get; set; }
			public string UserAgent { get; set; }
			public string ContentType { get => UseWebp ? "image/webp" : _contentType ; set => _contentType = value; }
			public bool UseWebp { get; set; }
			public int MinZoom { get; set; }
			public int MaxZoom { get; set; }
			public long Requests { get; set; }
			public long Hits { get; set; }

			public string FileType
			{
				get
				{
					if (string.IsNullOrEmpty(_fileType))
						if (UseWebp)
							_fileType = "webp";
						else
							_fileType = ContentType switch
							{
								"image/png" => "png",
								"image/webp" => "webp",
								_ => "jpg"
							};

					return _fileType;
				}
			}

			public double HitsRate => Math.Round(100.0 * Hits / Requests, 2);

			public Provider(IConfigurationSection s)
			{
				if (s != null)
				{
					Id = s.GetValue<string>("Id");
					Url = s.GetValue<string>("Url");
					UserAgent = s.GetValue("UserAgent", string.Empty);
					ContentType = s.GetValue("ContentType", "image/png");
					UseWebp = s.GetValue("UseWebp", false);
					MinZoom = s.GetValue("MinZoom", 1);
					MaxZoom = s.GetValue("MaxZoom", 19);
				}
			}
		}



		struct TileReadyEventArgs
		{
			public string Key { get; set; }
			public byte[] Content { get; set; }
			public Exception ServerError { get; set; }
		}


		public struct Result
		{
			public IReadOnlyDictionary<string, string[]> BadRequestInfo { get; set; }
			public Exception ServerError { get; set; }
			public string PhysicalFile { get; set; }
			public byte[] Content { get; set; }
			public string ContentType { get; set; }
		}


		public struct Statistics
		{
			public long TotalRequests { get; set; }
			public long TotalHits { get; set; }
			public double HitsRate { get => Math.Round(100.0 * TotalHits / TotalRequests, 2); }
			public object[] Providers { get; set; }
		}



		public TileManager(IConfiguration configuration)
		{
			_cacheFolder = configuration.GetValue<string>("Tiles:Cache");

			_providers = configuration
				.GetSection("Tiles:Providers")?
				.GetChildren()?
				.Select(s => new Provider(s))
				.ToDictionary(p => p.Id, p => p) ?? [];
		}

		public async Task<Result> GetAsync(string providerId, int level, int x, int y, IHttpClientFactory httpClientFactory)
		{
			Dictionary<string, string[]> badRequestInfo = null;

			if (!_providers.TryGetValue(providerId, out Provider provider))
				badRequestInfo = new() { [nameof(providerId)] = ["tile provider not found"] };
			else if (level < provider.MinZoom || level > provider.MaxZoom)
				badRequestInfo = new() { [nameof(level)] = [string.Format("this parameter must be in range [{0}, {1}]", provider.MinZoom, provider.MaxZoom)] };
			else
			{
				int max = (1 << level) - 1;

				if (x < 0 || x > max)
					badRequestInfo = new() { [nameof(x)] = [string.Format("this parameter must be in range [0, {0}]", max)] };

				if (y < 0 || y > max)
					(badRequestInfo ?? (badRequestInfo = new()))[nameof(y)] = [string.Format("this parameter must be in range [0, {0}]", max)];
			}

			if (badRequestInfo != null)
				return new() { BadRequestInfo = badRequestInfo };


			string relPath = Path.Combine(providerId, level.ToString(), x.ToString(), string.Format("{0}.{1}", y, provider.FileType));
			string fullPath = Path.Combine(_cacheFolder, relPath);
			bool existsAndNotExpired;

			provider.Requests++;

			try
			{
				existsAndNotExpired = File.GetLastWriteTime(fullPath).Add(_expiration) >= DateTime.Now;
			}
			catch
			{
				existsAndNotExpired = false;
			}

			Result result = new() { ContentType = provider.ContentType };


			if (existsAndNotExpired)
			{
				provider.Hits++;
				result.PhysicalFile = fullPath;

				return result;
			}


			if (_tilesToLoad.TryAdd(relPath, 1))
			{
				byte[] content;

				try
				{
					var client = httpClientFactory.CreateClient();

					using HttpRequestMessage request = new()
					{
						Method = HttpMethod.Get,
						RequestUri = new Uri(string.Format(provider.Url, level, x, y))
					};

					if (!string.IsNullOrEmpty(provider.UserAgent))
						request.Headers.Add("User-Agent", provider.UserAgent);

					using HttpResponseMessage response = await client.SendAsync(request);

					response.EnsureSuccessStatusCode();

					if (provider.UseWebp)
					{
						using MemoryStream ms = new();

						var img = await Image.LoadAsync<Rgba32>(response.Content.ReadAsStream());
						
						img.SaveAsWebp(ms);
						content = ms.ToArray();
					}
					else
					{
						content = await response.Content.ReadAsByteArrayAsync();
					}

					Task.Run(() => SaveTile(fullPath, content));
				}
				catch (Exception ex)
				{
					result.ServerError = ex;
					content = null;
				}
				finally
				{
					_tilesToLoad.Remove(relPath, out _);
				}

				result.Content = content;

				RaiseTileReadyEvent(new TileReadyEventArgs() { Key = relPath, Content = content, ServerError = result.ServerError });
			}
			else
			{
				using CancellationTokenSource cts = new();

				void onTileReady(object sender, TileReadyEventArgs e)
				{
					if (e.Key == relPath)
					{
						result.Content = e.Content;
						result.ServerError = e.ServerError;
						cts.Cancel();
					}
				}

				TileReady += onTileReady;

				try
				{
					await Task.Delay(1000, cts.Token);
				}
				catch
				{
				}
				finally
				{
					TileReady -= onTileReady;
				}

				provider.Hits++;
			}

			return result;
		}

		public Statistics Stats()
		{
			long total = 0;
			long hits = 0;
			var pStats = new object[_providers.Count];
			int i = 0;

			foreach (var p in _providers)
			{
				total += p.Value.Requests;
				hits += p.Value.Hits;
				pStats[i++] = new { Id = p.Key, p.Value.Requests, p.Value.Hits, p.Value.HitsRate };
			}

			var result = new Statistics()
			{
				TotalRequests = total,
				TotalHits = hits,
				Providers = pStats
			};

			return result;
		}

		private static void SaveTile(string fullName, byte[] content)
		{
			for (int i = 0; i < 4; i++)
				try
				{
					File.WriteAllBytes(fullName, content);
					break;
				}
				catch (DirectoryNotFoundException ex)
				{
					Directory.CreateDirectory(Path.GetDirectoryName(fullName));
				}
				catch
				{
					Thread.Sleep(250);
				}
		}

		private void RaiseTileReadyEvent(TileReadyEventArgs e)
		{
			EventHandler<TileReadyEventArgs> handler = TileReady;

			if (handler != null)
			{
				Delegate[] handlers = handler.GetInvocationList();

				foreach (var h in handlers.Cast<EventHandler<TileReadyEventArgs>>())
					h(this, e);
			}
		}
	}

}