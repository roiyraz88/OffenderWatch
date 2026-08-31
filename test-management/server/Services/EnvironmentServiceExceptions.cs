namespace OffenderWatch.TestManagement.Server.Services;

/// <summary>Request failed backend validation (Name/BaseUrl rules). Maps to 400.</summary>
public class EnvironmentValidationException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public EnvironmentValidationException(IEnumerable<string> errors)
        : base(string.Join(" ", errors))
    {
        Errors = errors.ToList();
    }

    public EnvironmentValidationException(string error)
        : this(new[] { error })
    {
    }
}

/// <summary>Requested Environment id does not exist. Maps to 404.</summary>
public class EnvironmentNotFoundException : Exception
{
    public EnvironmentNotFoundException(int id)
        : base($"Environment {id} was not found.")
    {
    }
}

/// <summary>Request conflicts with existing state (e.g. duplicate name). Maps to 409.</summary>
public class EnvironmentConflictException : Exception
{
    public EnvironmentConflictException(string message) : base(message)
    {
    }
}
