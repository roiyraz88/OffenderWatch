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

/// <summary>Requested ScenarioResult does not exist, or does not belong to the requested Run (6.18). Maps to 404.</summary>
public class ScenarioResultNotFoundException : Exception
{
    public ScenarioResultNotFoundException(int runId, int scenarioResultId)
        : base($"ScenarioResult {scenarioResultId} was not found in Run {runId}.")
    {
    }
}

/// <summary>Requested TestCase does not exist. Maps to 404.</summary>
public class TestCaseNotFoundException : Exception
{
    public TestCaseNotFoundException(int id) : base($"TestCase {id} was not found.")
    {
    }
}

/// <summary>Requested TestDataRecord does not exist. Maps to 404.</summary>
public class TestDataRecordNotFoundException : Exception
{
    public TestDataRecordNotFoundException(int id) : base($"TestDataRecord {id} was not found.")
    {
    }
}

/// <summary>An invalid TM-06 cleanup request, e.g. an empty batch id list. Maps to 400.</summary>
public class TestDataValidationException : Exception
{
    public TestDataValidationException(string message) : base(message)
    {
    }
}
