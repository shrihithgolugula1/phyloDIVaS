using Microsoft.AspNetCore.Mvc;
using APIInterface;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwaggerUI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class NCBIController : ControllerBase
    {
        private readonly APICalls _api;

        public NCBIController()
        {
            APICalls api = new APICalls();
            _api = api;
        }

        /// <summary>
        /// Get genome report(s) by genome IDs.
        /// </summary>
        /// <param name="ids">List of genome IDs.</param>
        [HttpPost("genomes/GetGenomesByIds")]
        public async Task<IActionResult> GetGenomesByIds([FromBody] List<string> ids)
        {
            var result = await _api.GetGenomeByIDs(ids);
            return Ok(result);
        }

        /// <summary>
        /// Get gene report(s) by accessions.
        /// </summary>
        /// <param name="accessions">List of gene accession numbers.</param>
        [HttpPost("genes/GetGenesByAccessions")]
        public async Task<IActionResult> GetGeneByAccessions([FromBody] List<string> accessions)
        {
            var result = await _api.GetGeneByAccessions(accessions);
            return Ok(result);
        }

        /// <summary>
        /// Get gene report(s) by gene IDs.
        /// </summary>
        /// <param name="ids">List of gene IDs.</param>
        [HttpPost("genes/GetGeneByIds")]
        public async Task<IActionResult> GetGeneByIDs([FromBody] List<int> ids)
        {
            var result = await _api.GetGeneByGeneIds(ids);
            return Ok(result);
        }

        [HttpPost("genes/GetGenesByTaxons")]
        public async Task<IActionResult> GetGenesByTaxon([FromBody] List<string> Taxons)
        {
            var result = await _api.GetGeneByTaxons(Taxons);
            return Ok(result);
        }

        [HttpPost("genes/GetGeneOrthologsById")]
        public async Task<IActionResult> GetGeneOrthologsByGeneId([FromBody] int GeneId)
        {
            var result = await _api.GetGeneOrthologsByGeneId(GeneId);
            return Ok(result);
        }

        [HttpPost("genes/GetGenesbySymbolsandTaxon")]
        public async Task<IActionResult> GetGeneBySymbolsandTaxon([FromBody] List<string> Symbols, string Taxon)
        {
            var result = await _api.GetGeneBySymbolsandTaxon(Symbols,Taxon);
            return Ok(result);
        }

       
        /// <summary>
        /// Get genome report(s) by assembly accessions.
        /// </summary>
        /// <param name="Taxons">List of Taxons.</param>
        [HttpPost("genomes/GetGenomeByTaxons")]
        public async Task<IActionResult> GetGenomeByTaxons([FromBody] List<string> Taxons)
        {
            GenomeResponse result = await _api.GetGenomeByTaxons(Taxons);
            return Ok(result);
        }

       

        

       

      

        
        /// <summary>
        /// Get taxon suggestions based on partial name input.
        /// </summary>
        /// <param name="name">Partial or full name for autocomplete.</param>
        [HttpGet("taxons/GetTaxonsBySuggestion")]
        public async Task<IActionResult> GetTaxonsBySuggestion([FromQuery] string name)
        {
            var result = await _api.GetTaxonsBySuggestion(name);
            return Ok(result);
        }


    }
}
