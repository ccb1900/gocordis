using Industrial.Core;

namespace Industrial.Unit.Tests;

public sealed class FailureDiagnosisTests
{
    [Theory]
    [InlineData("resource 'source-1' was not found", "RESOURCE_MISSING")]
    [InlineData("SMB credential conflict for the same server", "CREDENTIAL")]
    [InlineData("Connection refused 127.0.0.1:1433", "CONNECTION")]
    [InlineData("CSV parse error at row 12", "PARSE")]
    [InlineData("delivery lease expired before completion", "LEASE")]
    [InlineData("validation failed for record 7", "CONFIGURATION")]
    [InlineData("staged source file changed on disk", "IDENTITY")]
    public void ClassifiesActionableOperationalFailures(string error, string expectedCode)
    {
        var diagnosis = FailureDiagnoses.Classify(error);
        Assert.Equal(expectedCode, diagnosis.Code);
        Assert.False(string.IsNullOrWhiteSpace(diagnosis.Action));
    }
}
