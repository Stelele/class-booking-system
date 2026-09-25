namespace Application.Abstractions;

public interface IGoogleOAuthStateStore
{
    string Issue(string purpose, string binding = "");
    bool Consume(string state, string purpose, string binding = "");
}
