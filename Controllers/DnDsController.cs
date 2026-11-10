using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using CapstoneDesign.Services.Galaxy;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

namespace CapstoneDesign.Controllers
{
    [ApiController]
    [Route("api/dnds")]
    public class DnDsController : ControllerBase
    {
        private readonly GalaxyBustedPipeline _busted;
        private readonly GalaxyOrthoFinderPipeline _ortho;
        private readonly GalaxyClient _galaxy;

        public DnDsController(
            GalaxyBustedPipeline busted,
            GalaxyOrthoFinderPipeline ortho,
            GalaxyClient galaxy)
        {
            _busted = busted;
            _ortho = ortho;
            _galaxy = galaxy;
        }

        // 1. Start dN/dS Analysis
        [HttpPost("start")]
        public async Task<IActionResult> StartDnDs([FromBody] StartDnDsRequest request)
        {
            if (request == null)
                return BadRequest(new { success = false, error = "Invalid request body" });

            var job = await _busted.StartAsync(
                request.Gene,
                new List<string> { request.Species },
                request.NcbiApiKey,
                CancellationToken.None,
                request.Alignment,
                request.SelectedIds?.ToDictionary(id => id, id => id)
            );

            return Ok(new { success = true, result = job });
        }

        // 2. Get Job Status (GalaxyClient.GetJobAsync)
        [HttpGet("job/{id}")]
        public async Task<IActionResult> GetJobStatus(string id)
        {
            var job = await _galaxy.GetJobAsync(id, CancellationToken.None);

            if (job.ValueKind == JsonValueKind.Undefined)
                return NotFound(new { success = false, error = "Job not found" });

            return Ok(job);
        }

        // 3. Start Genome Analysis
        [HttpPost("genome-analysis/start")]
        public async Task<IActionResult> StartGenomeAnalysis([FromBody] StartGenomeAnalysisRequest request)
        {
            if (request == null)
                return BadRequest(new { success = false, error = "Invalid request body" });

            var result = await _ortho.StartGenomeAnalysisAsync(
                new List<string> { request.Species },
                request.NcbiApiKey,
                CancellationToken.None
            );

            return Ok(new { success = true, result });
        }

        // 4. Get Orthologous Groups
        [HttpGet("orthologous-groups/{historyId}/{jobId}")]
        public async Task<IActionResult> GetOrthoGroups(string historyId, string jobId, [FromQuery] List<string>? selectedGroupIds)
        {
            var normalizedSelection = selectedGroupIds?
                .SelectMany(val => (val ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var groups = await _ortho.GetOrthologousGroupsAsync(
                historyId,
                jobId,
                normalizedSelection,
                CancellationToken.None
            );

            return Ok(new { success = true, groups });
        }
    }

    // Request DTOs
    public class StartDnDsRequest
    {
        public string Gene { get; set; }
        public string Species { get; set; }
        public string NcbiApiKey { get; set; }
        public string Alignment { get; set; }
        public string[] SelectedIds { get; set; }
    }

    public class StartGenomeAnalysisRequest
    {
        public string Species { get; set; }
        public string NcbiApiKey { get; set; }
    }
}
