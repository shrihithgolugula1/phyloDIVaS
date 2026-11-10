using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Amazon;
using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity.Data;
using PhyloDivas.Data.New;
using PhyloDivas.Data.New.Entities;
using PhyloDivas.Data.New.Data;
using PhyloDivas.Data.New.Repositories;
using System.IdentityModel.Tokens.Jwt;
using System.Globalization;

namespace RegistrationControllerAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RegistrationController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly IAmazonCognitoIdentityProvider _provider;
        private readonly ILogger<RegistrationController> _logger;
        //private readonly PhylodivasContext _context;

        public RegistrationController(
            IConfiguration config,
            IAmazonCognitoIdentityProvider provider,
            ILogger<RegistrationController> logger)
        {
            _config = config;
            _provider = provider;
            _logger = logger;
            //_context = context;
        }

        [HttpPost]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) ||
                string.IsNullOrWhiteSpace(request.Email) ||
                string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new { error = "Username, email, and password are required." });

            try
            {
                string clientId = _config["AWS:Cognito:UserPoolClientId"];
                string clientSecret = _config["AWS:Cognito:UserPoolClientSecret"];

                string secretHash = ComputeSecretHash(clientId, clientSecret, request.Username);

                var signUpRequest = new SignUpRequest
                {
                    ClientId = clientId,
                    Username = request.Username,
                    Password = request.Password,
                    SecretHash = secretHash,
                    UserAttributes = new List<AttributeType>
            {
                new AttributeType { Name = "email", Value = request.Email }
            }
                };

                await _provider.SignUpAsync(signUpRequest);

                return Ok(new { message = "Registration successful. Check your email for the verification code." });
            }
            catch (UsernameExistsException)
            {
                return Conflict(new { error = "That username is already registered." });
            }
            catch (InvalidPasswordException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (InvalidParameterException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected registration failure for {Username}", request.Username);
                return StatusCode(500, new { error = "Registration could not be completed. Please try again." });
            }
        }

        [HttpPost("verify")]
        public async Task<IActionResult> Verify([FromBody] VerificationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Code))
                return BadRequest(new { error = "Username and verification code are required." });

            try
            {
                string clientId = _config["AWS:Cognito:UserPoolClientId"];
                string clientSecret = _config["AWS:Cognito:UserPoolClientSecret"];

                string secretHash = ComputeSecretHash(clientId, clientSecret, request.Username);

                var confirmRequest = new ConfirmSignUpRequest
                {
                    ClientId = clientId,
                    Username = request.Username,
                    ConfirmationCode = request.Code,
                    SecretHash = secretHash
                };

                await _provider.ConfirmSignUpAsync(confirmRequest);
                return Ok(new { message = "Verification successful." });
            }
            catch (CodeMismatchException)
            {
                return BadRequest(new { error = "The verification code is incorrect." });
            }
            catch (ExpiredCodeException)
            {
                return BadRequest(new { error = "The verification code has expired." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected verification failure for {Username}", request.Username);
                return StatusCode(500, new { error = "Verification could not be completed. Please try again." });

            }
        }
        
        [HttpPost("SaveGrantInfo")]
        public async Task<IActionResult> SaveGrantInfo([FromBody] GrantInfoRequest model)
        {
            if (model == null)
                return BadRequest("Invalid data.");

            try
            {
                // 1. Get the user's Cognito ID (sub) from the ID token
                var idToken = HttpContext.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
                if (string.IsNullOrEmpty(idToken))
                    return Unauthorized("Missing ID token.");

                var handler = new JwtSecurityTokenHandler();
                if (!handler.CanReadToken(idToken))
                    return Unauthorized("Your sign-in session is invalid. Please sign in again.");

                var jwtToken = handler.ReadJwtToken(idToken);

                // Extract the Cognito sub claim
                var cognitoSub = jwtToken.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
                if (string.IsNullOrEmpty(cognitoSub))
                    return Unauthorized("Invalid token: Missing Cognito sub.");

                // 2. Locate the user in the database by their Cognito sub (which is stored as UserId)
                using var context = new PhylodivasContext();
                var userRepository = new GenericRepository<User>(context);

                var existingUsers = await userRepository.FindAsync(u => u.UserId.ToString() == cognitoSub);
                var user = existingUsers.FirstOrDefault();

                if (user == null)
                    return NotFound("User not found.");

                // 3. Update the user’s fields
                user.Age = (short?)model.Age;
                user.CurrentStatus = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(model.CurrentStatus);
                user.HighestDegree = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(model.HighestDegree);
                user.AssociatedInstitution = model.Institution;
                user.State = model.StateOrProvince;
                user.Country = model.Country;
                user.UpdatedAt = DateTime.UtcNow;

                userRepository.Update(user);
                await context.SaveChangesAsync();

                return Ok("User grant info updated successfully!");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error saving data: {ex.Message}");
            }
        }

        [HttpGet("HasGrantInfo")]
        public async Task<IActionResult> HasGrantInfo()
        {
            try
            {
                var idToken = HttpContext.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
                if (string.IsNullOrEmpty(idToken))
                    return Unauthorized("Missing ID token.");

                var handler = new JwtSecurityTokenHandler();
                if (!handler.CanReadToken(idToken))
                    return Unauthorized("Your sign-in session is invalid. Please sign in again.");

                var jwtToken = handler.ReadJwtToken(idToken);
                var cognitoSub = jwtToken.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;

                if (string.IsNullOrEmpty(cognitoSub))
                    return Unauthorized("Invalid token: Missing Cognito sub.");

                using var context = new PhylodivasContext();
                var userRepository = new GenericRepository<User>(context);
                var existingUsers = await userRepository.FindAsync(u => u.UserId.ToString() == cognitoSub);
                var user = existingUsers.FirstOrDefault();

                if (user == null)
                    return NotFound("User not found.");

                // Check if grant info has been filled out
                bool hasGrantInfo = !string.IsNullOrEmpty(user.CurrentStatus) ||
                                   !string.IsNullOrEmpty(user.HighestDegree) ||
                                   !string.IsNullOrEmpty(user.Country);

                return Ok(new { hasGrantInfo });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error: {ex.Message}");
            }
        }

        [HttpGet("GetAllUsers")]
        public async Task<IActionResult> GetAllUsers()
        {
            try
            {
                using var context = new PhylodivasContext();
                var userRepository = new GenericRepository<User>(context);

                var users = await userRepository.ListAsync();

                return Ok(users);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error retrieving users: {ex.Message}");
            }
        }

        // Helper to compute Cognito secret hash
        private string ComputeSecretHash(string clientId, string clientSecret, string username)
        {
            if (string.IsNullOrEmpty(clientSecret)) return null;
            string message = username + clientId;
            byte[] keyBytes = Encoding.UTF8.GetBytes(clientSecret);
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
            using (var hmac = new HMACSHA256(keyBytes))
            {
                var hash = hmac.ComputeHash(messageBytes);
                return Convert.ToBase64String(hash);
            }
        }
    }


    // Helper classes for Login and Registration form data
    public class RegisterRequest
    {
        public string Username { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
    }
    public class VerificationRequest
    {
        public string Username { get; set; }
        public string Code { get; set; }
    }

    public class LoginRequest
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }

    public class GrantInfoRequest
    {
        public int Age { get; set; }
        public string CurrentStatus { get; set; }
        public string HighestDegree { get; set; }
        public string Institution { get; set; }
        public string Country { get; set; }
        public string StateOrProvince { get; set; }
    }
}
