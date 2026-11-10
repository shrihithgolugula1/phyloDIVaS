using PhyloDivas.Data.New.Data;
using PhyloDivas.Data.New.Entities;
using PhyloDivas.Data.New.Repositories;
using System.IdentityModel.Tokens.Jwt;
using System.Xml.Serialization;

namespace SearchHistoryAPI.Models
{
    public class DatabaseManagement
    {


        private static string? GetCognitoSub(string idToken)
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(idToken);
            return jwt.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
        }

        public async Task<string> AddSearchHistory(string userIdToken, string seachType, string queryText, int resolvedTaxId = -1, int resolvedGeneId = -1,string resolvedAssemblyId = "-1", int resultCount = -1)
        {
            try
            {
                string userIdString = GetCognitoSub(userIdToken);
                Guid userId = Guid.Parse(userIdString);

                PhylodivasContext ctx = new PhylodivasContext();
                GenericRepository<searchHistory> historyRepo = new GenericRepository<searchHistory>(ctx);
                
                searchHistory history = new searchHistory();
                history.user_id = userId;
                history.search_type = seachType;
                history.query_text = queryText;
                if(resolvedTaxId != -1)
                {
                    history.resolved_tax_id = resolvedTaxId;
                }
                if (resolvedGeneId != -1) 
                { 
                    history.resolved_gene_id = resolvedGeneId;
                }
                if (resultCount != -1) 
                { 
                    history.result_count = resultCount;
                }
                if (resolvedAssemblyId != "-1")
                {
                    history.resolved_assembly_id = resolvedAssemblyId;
                }
                await historyRepo.AddAsync(history);
                await ctx.SaveChangesAsync();
                return history.ToString();
            }
            catch (Exception ex) 
            { 
                //add error logging here
                APIInterface.Databasemanagement databasemanagement = new APIInterface.Databasemanagement();
                databasemanagement.AddError(ex.TargetSite.ToString(), ex.StackTrace, ex.Message);
                return ex.ToString();
            }

        }



        public async void DeleteSearchHistory(string userIdToken, long searchHistoryId)
        {
            try
            {
                string userIdString = GetCognitoSub(userIdToken);
                Guid userId = Guid.Parse(userIdString);

                PhylodivasContext ctx = new PhylodivasContext();
                GenericRepository<searchHistory> historyRepo = new GenericRepository<searchHistory>(ctx);
                searchHistory history = new searchHistory();
                history.user_id = userId;
                history.search_id = searchHistoryId;
                historyRepo.Remove(history);
                ctx.SaveChangesAsync();
            }
            catch (Exception ex) 
            {
                //add error logging here 
                APIInterface.Databasemanagement databasemanagement = new APIInterface.Databasemanagement();
                databasemanagement.AddError(ex.TargetSite.ToString(), ex.StackTrace, ex.Message);
            }
        }

        public async Task<List<searchHistory>> GetUserSearchHistory(string userIdToken)
        {

            try
            {
                string userIdString = GetCognitoSub(userIdToken);
                Guid userId = Guid.Parse(userIdString);

                PhylodivasContext ctx = new PhylodivasContext();
                GenericRepository<searchHistory> historyRepo = new GenericRepository<searchHistory>(ctx);
                var results = await historyRepo.FindAsync(history => history.user_id == userId);
                return results.ToList();
            }
            catch(Exception ex)
            {
                //add error logging 
                APIInterface.Databasemanagement databasemanagement = new APIInterface.Databasemanagement();
                databasemanagement.AddError(ex.TargetSite.ToString(), ex.StackTrace, ex.Message);
            }

            return null;
        }

    }
}
