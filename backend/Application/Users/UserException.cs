namespace Application.Users;

/// Validation failure on a user edit — surfaces as 400 with the message.
public sealed class UserException(string message) : Exception(message);

/// The targeted user does not exist — surfaces as 404.
public sealed class UserNotFoundException(string message) : Exception(message);
