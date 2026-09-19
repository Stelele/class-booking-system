using Booking.Application.Abstractions;
using Booking.Application.DTOs;

namespace Booking.Application.Auth;

public sealed record GetMeQuery : IQuery<UserDto?>;

public sealed class GetMeQueryHandler(ICurrentUser user) : IQueryHandler<GetMeQuery, UserDto?>
{
    public Task<UserDto?> Handle(GetMeQuery q, CancellationToken ct) =>
        Task.FromResult<UserDto?>(new UserDto(user.UserId, user.Name, "", user.IsAdmin ? "Admin" : "Student"));
}
