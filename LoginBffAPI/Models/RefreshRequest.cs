namespace BffAuth.Models;

public record RefreshRequest(string RefreshToken, string? Username = null);
