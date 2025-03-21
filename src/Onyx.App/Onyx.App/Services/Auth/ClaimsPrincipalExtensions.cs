﻿using System.Security.Claims;

namespace Onyx.App.Services.Auth;

public static class ClaimsPrincipalExtensions
{
    public static Task<string?> GetAuthTokenAsync(this ClaimsPrincipal user)
    {
        var claim = user.FindFirst("access_token");
        return Task.FromResult(claim?.Value);
    }
    
    public static Task<string?> GetRefreshTokenAsync(this ClaimsPrincipal user)
    {
        var claim = user.FindFirst("refresh_token");
        return Task.FromResult(claim?.Value);
    }
}