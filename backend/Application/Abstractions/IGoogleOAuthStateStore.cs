namespace Application.Abstractions;

public interface IGoogleOAuthStateStore
{
    string Issue();
    bool Consume(string state);
}
