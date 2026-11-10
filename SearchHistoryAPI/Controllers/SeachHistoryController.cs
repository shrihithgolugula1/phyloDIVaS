using Microsoft.AspNetCore.Mvc;
using APIInterface;
using System.Collections.Generic;
using System.Threading.Tasks;
using SearchHistoryAPI.Models;

namespace SearchHistory.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SearchHistoryController : ControllerBase
    {

        public SearchHistoryController()
        {
            
        }





        [HttpPost("search/AddSearch")]
        public async Task<IActionResult> AddSearch([FromBody] string userId, string searchType, string queryText, int resolvedTaxId = -1, int resolvedGeneId = -1, string resolvedAssemblyId = "-1" )
        {
            try
            {
                DatabaseManagement databaseManagement = new DatabaseManagement();



                var result = await databaseManagement.AddSearchHistory(userId,searchType,queryText,resolvedTaxId, resolvedGeneId,resolvedAssemblyId);
                
                return Ok(result);
            }
            catch (Exception ex) 
            { 
                return BadRequest(ex.Message);
            }
            
        }

        [HttpDelete("search/DeleteSearch")]
        public async Task<IActionResult> DeleteSearch(string userId, long searchHistoryId)
        {
            try
            {
                DatabaseManagement databaseManagement = new DatabaseManagement();
                databaseManagement.DeleteSearchHistory(userId,searchHistoryId);
                return Ok("search deleted");
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("search/getSearchHistory")]
        public async Task<IActionResult> getSearchHistory([FromBody] string userId)
        {
            try
            {
                DatabaseManagement databaseManagement = new DatabaseManagement();
                var history = await databaseManagement.GetUserSearchHistory(userId);
                return Ok(history);
            }
            catch (Exception ex) 
            {
                return BadRequest(ex.Message);    
            }

        }






    }
}
