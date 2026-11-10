using Microsoft.AspNetCore.Mvc;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SwaggerUI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TestSearchController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public TestSearchController(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        [HttpGet("check")]
        public async Task<IActionResult> TestCheckQuery([FromQuery] string query, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(query))
                return BadRequest("Query cannot be empty");

            var client = _httpClientFactory.CreateClient("CapstoneDesignAPI");
            var response = await client.GetAsync($"api/Search/check?query={Uri.EscapeDataString(query)}", ct);

            if (!response.IsSuccessStatusCode)
                return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync(ct));

            var content = await response.Content.ReadAsStringAsync(ct);
            return Ok(JsonSerializer.Deserialize<object>(content));
        }

        [HttpGet("search")]
        public async Task<IActionResult> TestSearch([FromQuery] string query, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(query))
                return BadRequest("Query cannot be empty");

            var client = _httpClientFactory.CreateClient("CapstoneDesignAPI");
            var response = await client.GetAsync($"api/Search/search?query={Uri.EscapeDataString(query)}", ct);

            if (!response.IsSuccessStatusCode)
                return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync(ct));

            var content = await response.Content.ReadAsStringAsync(ct);
            return Ok(JsonSerializer.Deserialize<object>(content));
        }
    }
}