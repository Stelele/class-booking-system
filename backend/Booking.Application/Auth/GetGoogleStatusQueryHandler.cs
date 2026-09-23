using Booking.Application.Abstractions;
using Booking.Application.DTOs;

namespace Booking.Application.Auth;

public sealed class GetGoogleStatusQueryHandler(IGoogleTokenStore store)
    : IQueryHandler<GetGoogleStatusQuery, GoogleStatusDto>
{
    public async Task<GoogleStatusDto> Handle(GetGoogleStatusQuery q, CancellationToken ct)
    {
        var t = await store.GetAsync(ct);
        return new GoogleStatusDto(t is not null && !t.NeedsReconnect, t?.NeedsReconnect ?? false);
    }
}
