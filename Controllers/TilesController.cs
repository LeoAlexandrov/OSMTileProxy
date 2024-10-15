using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

using OSMTileProxy.Services;


namespace OSMTileProxy.Controllers
{

	[Route("[controller]")]
	[ApiController]
	public class TilesController(IHttpClientFactory httpClientFactory, TileManager tileManager) : ControllerBase
	{
		private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
		private readonly TileManager _tileManager = tileManager;


		[HttpGet("{provider}/{level:int}/{x:int}/{y:int}")]
		[ResponseCache(Duration = 3600)]
		public async Task<IActionResult> Get(string provider, int level, int x, int y)
		{
			var result = await _tileManager.GetAsync(provider, level, x, y, _httpClientFactory);

			if (result.BadRequestInfo != null)
				return BadRequest(result.BadRequestInfo);

			if (result.ServerError != null)
#if DEBUG
				return StatusCode((int)HttpStatusCode.InternalServerError, result.ServerError.Message);
#else
				return StatusCode((int)HttpStatusCode.InternalServerError);
#endif

			if (!string.IsNullOrEmpty(result.PhysicalFile))
				return PhysicalFile(result.PhysicalFile, result.ContentType);

			return File(result.Content, result.ContentType);
		}

		[HttpGet("stats")]
		public IActionResult Stats()
		{
			var result = _tileManager.Stats();

			return Ok(result);
		}
	}
}
