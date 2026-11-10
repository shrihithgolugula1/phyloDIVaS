using Amazon;
using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using PhyloDivas.Data.New;
using PhyloDivas.Data.New.Data;
using PhyloDivas.Data.New.Entities;
using PhyloDivas.Data.New.Repositories;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.EntityFrameworkCore;


namespace LoginBffAPI.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IConfiguration _cfg;
        private readonly IAmazonCognitoIdentityProvider _idp;
        private readonly PhylodivasContext _context;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            IConfiguration cfg,
            IAmazonCognitoIdentityProvider idp,
            PhylodivasContext context,
            ILogger<AuthController> logger)
        {
            _cfg = cfg;
            _idp = idp;
            _context = context;
            _logger = logger;
        }
        //Login (username/password)
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest body)
        {
            if (string.IsNullOrWhiteSpace(body.Username) || string.IsNullOrWhiteSpace(body.Password))
                return BadRequest(new { error = new { code = "BAD_REQUEST", message = "username and password required" } });

            var region = _cfg["AWS:Region"]!;
            var clientId = _cfg["AWS:Cognito:UserPoolClientId"]!;
            var clientSecret = _cfg["AWS:Cognito:UserPoolClientSecret"]; // may be empty

            var authParams = new Dictionary<string, string>
            {
                ["USERNAME"] = body.Username,
                ["PASSWORD"] = body.Password
            };
            if (!string.IsNullOrWhiteSpace(clientSecret))
                authParams["SECRET_HASH"] = ComputeSecretHash(clientId, clientSecret!, body.Username);

            var req = new InitiateAuthRequest
            {
                ClientId = clientId,
                AuthFlow = AuthFlowType.USER_PASSWORD_AUTH,
                AuthParameters = authParams
            };

            try
            {
                var RESP = await _idp.InitiateAuthAsync(req);

                var ar = RESP.AuthenticationResult;
                if (ar is null)
                    return StatusCode(502, new { error = new { code = "NO_TOKENS", message = "No tokens returned by Cognito." } });

                var cognitoSub = GetCognitoSub(ar.IdToken);

                // Synchronize the optional local profile without invalidating a successful Cognito login.
                if (!string.IsNullOrEmpty(cognitoSub))
                {
                    try
                    {
                        var userId = Guid.Parse(cognitoSub);
                        if (!await _context.Users.AnyAsync(u => u.UserId == userId))
                        {
                            _context.Users.Add(new User { UserId = userId, UpdatedAt = DateTime.UtcNow });
                            await _context.SaveChangesAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Cognito login succeeded, but local profile synchronization failed for {Sub}", cognitoSub);
                    }
                }

                return Ok(new
                {
                    accessToken = ar.AccessToken,
                    idToken = ar.IdToken,
                    refreshToken = ar.RefreshToken,
                    expiresIn = ar.ExpiresIn,
                });
            }
            catch (NotAuthorizedException)
            {
                return Unauthorized(new { error = new { code = "INVALID_CREDENTIALS", message = "Invalid username or password." } });
            }
            catch (UserNotFoundException)
            {
                return Unauthorized(new { error = new { code = "INVALID_CREDENTIALS", message = "Invalid username or password." } });
            }
            catch (TooManyRequestsException)
            {
                return StatusCode(429, new { error = new { code = "RATE_LIMITED", message = "Too many attempts. Try again later." } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected Cognito login failure");
                return StatusCode(500, new { error = new { code = "SERVER_ERROR", message = "Login failed." } });
            }

        }
        //me - get user info from access token
        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            var auth = Request.Headers["Authorization"].ToString();
            if (string.IsNullOrWhiteSpace(auth) || !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return Unauthorized(new { error = new { code = "UNAUTHORIZED", message = "Missing Authorization header." } });

            try
            {
                var accessToken = auth["Bearer ".Length..].Trim();
                var response = await _idp.GetUserAsync(new GetUserRequest { AccessToken = accessToken });
                return Ok(new
                {
                    username = response.Username,
                    attributes = response.UserAttributes.ToDictionary(item => item.Name, item => item.Value)
                });
            }
            catch (NotAuthorizedException)
            {
                return Unauthorized(new { error = new { code = "UNAUTHORIZED", message = "Your session is invalid or expired." } });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to validate Cognito access token");
                return Unauthorized(new { error = new { code = "UNAUTHORIZED", message = "Your session could not be verified." } });
            }
        }

        //refresh
        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshRequest body)
        {
            if (string.IsNullOrWhiteSpace(body.RefreshToken))
                return BadRequest(new { error = new { code = "BAD_REQUEST", message = "refreshToken required" } });

            var region = _cfg["AWS:Region"]!;
            var clientId = _cfg["AWS:Cognito:UserPoolClientId"]!;
            var clientSecret = _cfg["AWS:Cognito:UserPoolClientSecret"];

            var authParams = new Dictionary<string, string>
            {
                ["REFRESH_TOKEN"] = body.RefreshToken
            };

            if (!string.IsNullOrWhiteSpace(clientSecret))
                authParams["SECRET_HASH"] = ComputeSecretHash(clientId, clientSecret!, body.Username ?? string.Empty);
            //username is optional for refresh

            var req = new InitiateAuthRequest
            {
                ClientId = clientId,
                AuthFlow = AuthFlowType.REFRESH_TOKEN_AUTH,
                AuthParameters = authParams
            };

            try
            {
                var resp = await _idp.InitiateAuthAsync(req);
                var ar = resp.AuthenticationResult;
                if (ar is null)
                    return StatusCode(502, new { error = new { code = "NO_TOKENS", message = "No tokens returned by Cognito." } });

                return Ok(new
                {
                    accessToken = ar.AccessToken,
                    idToken = ar.IdToken,
                    // refresh token is usually NOT returned on refresh; keep your old one
                    expiresIn = ar.ExpiresIn
                });
            }
            catch (NotAuthorizedException)
            {
                return Unauthorized(new { error = new { code = "INVALID_REFRESH", message = "Invalid or expired refresh token." } });
            }
            catch
            {
                return StatusCode(500, new { error = new { code = "SERVER_ERROR", message = "Refresh failed." } });
            }
        }

        //Logout
        [HttpPost("logout")]
        public async Task<IActionResult> Logout([FromBody] LogoutRequest body)
        {
            var region = _cfg["AWS:Region"]!;
            try
            {
                if (!string.IsNullOrWhiteSpace(body.AccessToken))
                {
                    //ends the user's sessions for the provided access token
                    await _idp.GlobalSignOutAsync(new GlobalSignOutRequest
                    {
                        AccessToken = body.AccessToken
                    });
                }
                //If wanting to revoke refresh tokens and have a client secret:
                //await idp.RevokeTokenAsync(new RevokeTokenRequest { ClientId = clientId, ClientSecret = clientSecret, Token = body.RefreshToken });

                return NoContent();
            }
            catch
            {
                return StatusCode(500, new { error = new { code = "SERVER_ERROR", message = "Logout failed." } });
            }
        }


        [HttpGet("is-admin")]
        public IActionResult IsAdmin()
        {
            var authHeader = Request.Headers["Authorization"].ToString();

            if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
                return Unauthorized(new { isAdmin = false, error = "Missing or invalid Authorization header" });

            var idToken = authHeader.Substring("Bearer ".Length).Trim();

            var handler = new JwtSecurityTokenHandler();
            JwtSecurityToken jwt;

            try
            {
                jwt = handler.ReadJwtToken(idToken);

                // Check token expiration
                if (jwt.ValidTo < DateTime.UtcNow)
                {
                    return Unauthorized(new { isAdmin = false, error = "Token expired" });
                }
            }
            catch (Exception ex)
            {
                return Unauthorized(new { isAdmin = false, error = "Invalid token" });
            }

            // Cognito groups claim
            var groups = jwt.Claims
                .Where(c => c.Type == "cognito:groups")
                .Select(c => c.Value)
                .ToList();

            bool isAdmin = groups.Contains("admin");

            return Ok(new { isAdmin });
        }


        //Helpers
        private static string ComputeSecretHash(string clientId, string clientSecret, string username)
        {
            var data = Encoding.UTF8.GetBytes(username + clientId);
            var key = Encoding.UTF8.GetBytes(clientSecret);
            using var hmac = new HMACSHA256(key);
            var sig = hmac.ComputeHash(data);
            return Convert.ToBase64String(sig);
        }

        //Helper to decode cognitoID (sub) from tokens
        private static string? GetCognitoSub(string idToken)
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(idToken);
            return jwt.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
        }

    }
    // request DTOs
    public record LoginRequest(string Username, string Password);
    public record RefreshRequest(string RefreshToken, string? Username = null);
    public record LogoutRequest(string? AccessToken, string? RefreshToken);
}
