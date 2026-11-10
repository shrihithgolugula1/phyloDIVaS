using Microsoft.AspNetCore.Mvc;
using PhyloDivas.Data.New;
using PhyloDivas.Data.New.Entities;
using PhyloDivas.Data.New.Data;
using PhyloDivas.Data.New.Repositories;
using System.IdentityModel.Tokens.Jwt;

namespace AccountAPI.Controllers
{
    public class ProfileController : Controller
    {
        [HttpGet("GetGrantInfo")]
        public async Task<IActionResult> GetGrantInfo()
        {
            var idToken = HttpContext.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
            if (string.IsNullOrEmpty(idToken))
                return Unauthorized("Missing ID token.");

            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(idToken);

            // Extract the Cognito sub claim
            var cognitoSub = jwtToken.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
            var username = jwtToken.Claims.FirstOrDefault(c => c.Type == "cognito:username")?.Value;
            var email = jwtToken.Claims.FirstOrDefault(c => c.Type == "email")?.Value;

            if (string.IsNullOrEmpty(cognitoSub))
                return Unauthorized("Invalid token: Missing Cognito sub.");

            // 2. Locate the user in the database by their Cognito sub (which is stored as UserId)
            using var context = new PhylodivasContext();
            var userRepository = new GenericRepository<User>(context);

            var existingUsers = await userRepository.FindAsync(u => u.UserId.ToString() == cognitoSub);
            var user = existingUsers.FirstOrDefault();

            if (user == null)
                return NotFound("User not found.");

            return Ok(new
            {
                username = username,
                email = email,
                age = user.Age,
                currentStatus = user.CurrentStatus,
                highestDegree = user.HighestDegree,
                associatedInstitution = user.AssociatedInstitution,
                country = user.Country,
                state = user.State
            });
        }
    }
}
