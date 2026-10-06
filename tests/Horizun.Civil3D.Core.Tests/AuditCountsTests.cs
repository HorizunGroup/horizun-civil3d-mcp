using Horizun.Civil3D.Core;

namespace Horizun.Civil3D.Core.Tests;

public sealed class AuditCountsTests
{
    [Fact]
    public void AuditToolIsReadOnlyAndHasBoundedExamples()
    {
        var tool = Contract.Find("horizun_c3d_audit");
        Assert.NotNull(tool);
        Assert.Equal("audit", tool.Command);
        Assert.Equal(ToolEffect.Read, tool.MaxEffect);
        Assert.Equal(200, tool.InputSchema["properties"]?["sample_limit"]?["maximum"]?.GetValue<int>());
    }

    [Fact]
    public void UnknownAndUnreadableAreNotCountedAsHealthy()
    {
        var counts = new AuditCounts();
        counts.SetEnumerated(4);
        counts.Record(true, true, false, true, true);
        counts.Record(true, null, null, null, true);
        counts.Record(false, null, null, false, true);
        counts.MarkUnreadable();

        Assert.Equal(4, counts.Enumerated);
        Assert.Equal(3, counts.Inspected);
        Assert.Equal(1, counts.Unreadable);
        Assert.Equal(2, counts.References);
        Assert.Equal(1, counts.StaleReferences);
        Assert.Equal(1, counts.StaleUnknown);
        Assert.Equal(1, counts.InvalidReferences);
        Assert.Equal(1, counts.ValidityUnknown);
        Assert.Equal(1, counts.OutOfDate);
        Assert.Equal(1, counts.Fresh);
        Assert.Equal(1, counts.CurrencyUnknown);
    }

    [Fact]
    public void EnumerationFailureRemainsUnknown()
    {
        var counts = new AuditCounts();
        Assert.Null(counts.Enumerated);
        counts.MarkUnreadable();
        Assert.Equal(0, counts.Inspected);
        Assert.Null(counts.Enumerated);
    }
}
