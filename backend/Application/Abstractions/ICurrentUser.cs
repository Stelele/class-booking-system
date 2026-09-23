namespace Application.Abstractions;

public interface ICurrentUser
{
    Guid UserId { get; }
    string Name { get; }
    bool IsAdmin { get; }
}
