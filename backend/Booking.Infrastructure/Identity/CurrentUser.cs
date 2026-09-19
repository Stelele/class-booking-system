using System.Security.Claims;
using Booking.Application.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Booking.Infrastructure.Identity;

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;
    public Guid UserId =>
        Guid.TryParse(Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id
        : throw new UnauthorizedAccessException("Not signed in.");
    public string Name => Principal?.FindFirst(ClaimTypes.Name)?.Value ?? "?";
    public bool IsAdmin => Principal?.IsInRole("Admin") ?? false;
}
