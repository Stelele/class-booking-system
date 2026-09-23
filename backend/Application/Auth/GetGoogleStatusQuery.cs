using Application.Abstractions;
using Application.DTOs;

namespace Application.Auth;

public sealed record GetGoogleStatusQuery : IQuery<GoogleStatusDto>;
