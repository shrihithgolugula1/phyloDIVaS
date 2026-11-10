using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using CapstoneServices;
using APIInterface;
using APIInterface.SupportingClasses;
using System.Text.Json;
using System.Net;

namespace SearchAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SearchController : ControllerBase
    {
        private readonly DataCheckService _dataCheckService;
        private readonly APICalls _api;
        private readonly ILogger<SearchController> _logger;
        private static readonly HttpClient NcbiHttp = new() { Timeout = TimeSpan.FromSeconds(30) };

        public SearchController(DataCheckService dataCheckService, ILogger<SearchController> logger)
        {
            _dataCheckService = dataCheckService;
            _logger = logger;
            _api = new APICalls();
        }

        [HttpGet("check")]
        public async Task<IActionResult> CheckQuery(string query, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(query))
                return BadRequest("Query cannot be empty");

            bool hasData = await _dataCheckService.HasExistingDataAsync(query, ct);
            return Ok(new { query, existsLocally = hasData });
        }

        [HttpGet("search")]
        public async Task<IActionResult> Search(string query, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(query))
                return BadRequest("Query cannot be empty");

            query = query.Trim();

            // Check local database first
            IEnumerable<object> localResults = Array.Empty<object>();
            try
            {
                localResults = await _dataCheckService.SearchLocalGenomeDataAsync(query, ct);
            }
            catch (InvalidOperationException ex) when (
                ex.Message.Contains("ConnectionString", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Local genome cache is unavailable; searching NCBI directly.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Local genome lookup failed; searching NCBI directly.");
            }
            if (localResults.Any())
            {
                return Ok(new
                {
                    source = "local",
                    type = "genome",
                    data = localResults
                });
            }

            // Fallback to NCBI API based on query type
            GenomeResponse genomeResponse = null;

            // Check if query is an accession (GCF_ or GCA_)
            if (query.StartsWith("GCF_", StringComparison.OrdinalIgnoreCase) ||
                query.StartsWith("GCA_", StringComparison.OrdinalIgnoreCase))
            {
                genomeResponse = await _api.GetGenomeByIDs(new List<string> { query });
            }
            else
            {
                // Try to resolve query as a taxon (common name, scientific name, or tax_id)
                var taxonResponse = await _api.GetTaxonsBySuggestion(query);
                if (taxonResponse?.SciNameAndIds?.Any() == true)
                {
                    // Use the top suggested taxon ID
                    var taxonId = taxonResponse.SciNameAndIds.First().taxId.ToString();
                    genomeResponse = await _api.GetGenomeByTaxons(new List<string> { taxonId });
                }
                else if (int.TryParse(query, out _))
                {
                    // Assume query is a tax_id if it's numeric and no taxon suggestions were found
                    genomeResponse = await _api.GetGenomeByTaxons(new List<string> { query });
                }
            }

            if (genomeResponse?.Reports?.Count > 0)
            {
                var apiResults = MapGenomeResults(genomeResponse);
                return Ok(new
                {
                    source = "api",
                    type = "genome",
                    data = apiResults
                });
            }

            return NotFound(new { query, message = "No genome found locally or via NCBI." });
        }

        [HttpGet("searchGene")]
        public async Task<IActionResult> SearchGene(string query, string taxon, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return BadRequest("Query cannot be empty");
            }


            // optional : check local database first (implemented here)


            GeneResponse geneResponse = null;

            // Determine if query is a gene ID (numeric) or symbol (alphanumeric)
            if (query.All(char.IsDigit))
            {              
                int? taxonId = await _api.GetTaxonIdByCommonName(taxon);

                if (taxonId.HasValue)
                {
                    bool IsWithinTaxon = await _api.CheckWithinTaxon(int.Parse(query), taxonId.Value);

                    if (IsWithinTaxon)
                    {
                        geneResponse = await _api.GetGeneByGeneIds(new List<int> { int.Parse(query) });
                    }
                }
            }
            else
            {
                geneResponse = await _api.GetGeneBySymbolsandTaxon(new List<string> { query }, taxon);
                if (geneResponse?.Reports?.Count is null or 0)
                {
                    var geneIds = await FindGeneIdsAsync(query, taxon, ct);
                    if (geneIds.Count > 0)
                        geneResponse = await _api.GetGeneByGeneIds(geneIds);
                }
            }

            if (geneResponse?.Reports?.Count > 0)
            {
                var apiResults = MapGeneResults(geneResponse);
                return Ok(new
                {
                    source = "api",
                    type = "gene",
                    data = apiResults
                });
            }

            return NotFound(new { query, message = "No gene found locally or via NCBI." });
        }

        private static async Task<List<int>> FindGeneIdsAsync(string query, string taxon, CancellationToken ct)
        {
            var term = string.IsNullOrWhiteSpace(taxon)
                ? $"{query}[All Fields]"
                : $"{query}[All Fields] AND {taxon}[Organism]";
            var url = "https://eutils.ncbi.nlm.nih.gov/entrez/eutils/esearch.fcgi" +
                      $"?db=gene&retmode=json&retmax=25&term={WebUtility.UrlEncode(term)}";
            using var response = await NcbiHttp.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return new List<int>();
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!document.RootElement.TryGetProperty("esearchresult", out var result) ||
                !result.TryGetProperty("idlist", out var ids))
                return new List<int>();

            return ids.EnumerateArray()
                .Select(id => int.TryParse(id.GetString(), out var value) ? value : 0)
                .Where(id => id > 0)
                .ToList();
        }

        private IEnumerable<object> MapGenomeResults(GenomeResponse genomeResponse)
        {
            return genomeResponse.Reports.Select(g => new
            {
                accession = g.Accession,
                assembly = g.AssemblyInfo?.AssemblyName,
                assemblyName = g.AssemblyInfo?.AssemblyName,
                assemblyLevel = g.AssemblyInfo?.AssemblyLevel,
                assemblyStatus = g.AssemblyInfo?.AssemblyStatus,
                assemblyType = g.AssemblyInfo?.assembly_type,
                submitter = g.AssemblyInfo?.Submitter,
                releaseDate = g.AssemblyInfo?.ReleaseDate,
                bioprojectAccession = g.AssemblyInfo?.bioproject_accession,


                scientificName = g.Organism?.OrganismName,
                commonName = g.Organism?.CommonName,


                genomeSize = g.AssemblyStats?.TotalSequenceLength,


                totalUngappedLength = g.AssemblyStats?.total_ungapped_length,
                gapsBetweenScaffolds = g.AssemblyStats?.gaps_between_scaffolds_count,
                numChromosomes = g.AssemblyStats?.TotalChromosomes,
                numOrganelles = g.AssemblyStats?.number_of_organelles,
                numScaffolds = g.AssemblyStats?.number_of_scaffolds,
                scaffoldN50 = g.AssemblyStats?.scaffold_n50,
                scaffoldL50 = g.AssemblyStats?.scaffold_l50,
                numContigs = g.AssemblyStats?.number_of_contigs,
                contigN50 = g.AssemblyStats?.contig_n50,
                contigL50 = g.AssemblyStats?.contig_l50,
                gcPercent = g.AssemblyStats?.GCPercent,
                annotationProvider = g.AnnotationInfo?.Provider,
                annotationName = g.AnnotationInfo?.Name,
                annotationDate = g.AnnotationInfo?.ReleaseDate,
                buscoComplete = g.AnnotationInfo?.Busco?.Complete
            });
        }

        private IEnumerable<object> MapGeneResults(GeneResponse geneResponse)
        {
            return geneResponse.Reports.Select(g => new
            {
                geneName = g.Gene?.Description,
                geneId = g.Gene?.GeneId,
                symbol = g.Gene?.Symbol,
                geneType = g.Gene?.Type.ToString(),
                fullGene = g.Gene
            });
        }
    }
}

    //private IEnumerable<object> MapGenomeResults(GenomeResponse genomeResponse)
    //{
    //    return genomeResponse.Reports.Select(g => new
    //    {
    //        accession = g.Accession,
    //        scientificName = g.Organism?.OrganismName,
    //        commonName = g.Organism?.CommonName,
    //        assembly = g.AssemblyInfo?.AssemblyName
    //    });
    //}
