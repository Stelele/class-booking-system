using Booking.Application.Abstractions;
using Booking.Application.DTOs;

namespace Booking.Application.Auth;

public sealed record GetGoogleStatusQuery : IQuery<GoogleStatusDto>;
