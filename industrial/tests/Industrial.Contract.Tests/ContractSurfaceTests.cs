using Gocordis.Core;
using Industrial.Contracts;

namespace Industrial.Contract.Tests;

public sealed class ContractSurfaceTests
{
    [Fact]
    public void CapabilityKeysAreDistinctReferenceIdentities()
    {
        var keys = new ICapabilityKey[]
        {
            IndustrialKeys.ResourceRegistry, IndustrialKeys.CredentialVault,
            IndustrialKeys.WorkflowRegistry, IndustrialKeys.NodeCatalog,
            IndustrialKeys.WorkflowValidator, IndustrialKeys.CollectionLedger,
            IndustrialKeys.HistoricalRangePlanner, IndustrialKeys.RecordSinkFactories,
            IndustrialKeys.RecordSink, IndustrialKeys.ArchiveSink,
            IndustrialKeys.FileSource, IndustrialKeys.RecordReader,
            IndustrialKeys.ElsaActivityCatalog,
        };
        Assert.Equal(keys.Length, keys.Distinct(ReferenceEqualityComparer.Instance).Count());
    }

    [Fact]
    public void PortTypesModelTheCollectorContract()
    {
        Assert.True(Enum.IsDefined(PortType.FileBatch));
        Assert.True(Enum.IsDefined(PortType.Dataset));
        Assert.True(Enum.IsDefined(PortType.RecordSet));
        Assert.True(Enum.IsDefined(PortType.DeliveryResult));
    }
}
