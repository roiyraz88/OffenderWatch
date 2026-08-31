namespace OffenderWatch.TestManagement.Server.Services;

/// <summary>Requested Run id does not exist. Maps to 404.</summary>
public class RunNotFoundException : Exception
{
    public RunNotFoundException(int id) : base($"Run {id} was not found.")
    {
    }
}

/// <summary>Stop requested on a Run that is already finished. Maps to 409.</summary>
public class RunConflictException : Exception
{
    public RunConflictException(string message) : base(message)
    {
    }
}
