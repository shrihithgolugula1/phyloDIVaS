using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PhyloDivas.Data.New;
using PhyloDivas.Data.New.Entities;
using PhyloDivas.Data.New.Repositories;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using PhylodivasContext = PhyloDivas.Data.New.Data.PhylodivasContext;


namespace AccountAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AdminController : ControllerBase
    {

        //method to get all error log data from error log db table
        //(optional) check that the user is in group:admin in Cognito user pool; may already be done in auth process
        [HttpPost("GetErrorLogs")]
        public async Task <IActionResult> GetErrorLogs([FromBody]string Authorization)
        {
            try
            {
                string userGroup = GetCognitoGroup(Authorization);


                if (userGroup == "admin")
                {

                    PhylodivasContext ctx = new PhylodivasContext();

                    //from PhyloDivas.Data.New --> GenericRepository.cs --> public async Task<IReadOnlyList<T>> ListAsync()
                    //returns a list of errors with fields: id, api_call, request_obj, response_obj, date_time

                    GenericRepository<apiErrorLog> errorRepo = new GenericRepository<apiErrorLog>(ctx);
                    IReadOnlyList<apiErrorLog> errors = await errorRepo.ListAsync();

                    return Ok(errors);
                }
                else
                {
                    return BadRequest(userGroup);
                }
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }

        }


        //method to get user data from Cognito User Pool + their associated personal info from the db table
        //(optional) check that the user is in group:admin in Cognito user pool; may already be done in auth process
        [HttpPost("GetAllUsers")]
        public async Task <IActionResult> GetAllUsers([FromBody] string Authorization)
        {
            try
            {
                string userGroup = GetCognitoGroup(Authorization);


                if (userGroup == "admin")
                {
                    

                //grab user id and email of users from Cognito User Pool
                //for each user that exists in the user pool, retrieve their db table data

                PhylodivasContext ctx = new PhylodivasContext();
                GenericRepository<User> userRepo = new GenericRepository<User>(ctx);
                var users = await userRepo.ListAsync();

                //create model class to map the frontend elements by name to the associated public properties of backend
                //see SearchController --> MapGenomeResults method in main

                return Ok(users);
                }
                else
                {
                    return BadRequest(userGroup);
                }
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }


        //method to remove a user from Cognito User Pool and their db table data
        [HttpPost("RemoveUser")]
        public async void RemoveUser(Guid userId, [FromBody] string token)
        {
            try
            {
                string userGroup = GetCognitoGroup(token);
                if (userGroup == "admin")
                {


                    PhylodivasContext ctx = new PhylodivasContext();
                    GenericRepository<User> userRepo = new GenericRepository<User>(ctx);
                    User user = new User();
                    user.UserId = userId;
                    userRepo.Remove(user);
                    ctx.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {

            }

        }          

        private static string? GetCognitoGroup(string idToken)
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(idToken);
            return jwt.Claims.FirstOrDefault(c => c.Type == "cognito:groups")?.Value;
        }

        private static string? GetCognitoSub(string idToken)
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(idToken);
            return jwt.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
        }
    }
}
