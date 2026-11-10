namespace BffAuth.Models;

public record LogoutRequest(string? AccessToken, string? RefreshToken);
