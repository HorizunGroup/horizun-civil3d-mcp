using System.Text.Json.Nodes;
using Horizun.Civil3D.Core;
using Xunit;

namespace Horizun.Civil3D.Core.Tests;

public class CorridorEngineeringTests
{
    [Theory]
    [InlineData("Surface", 2, "unsupported_target_type")]
    [InlineData("OffsetPipe", 2, "unsupported_target_type")]
    [InlineData("Elevation", 2, null)]
    [InlineData("Offset", 0, "insufficient_target_count")]
    [InlineData("Offset", 1, "insufficient_target_count")]
    [InlineData("Offset", 2, null)]
    public void OptionsOnlyReadForVerifiedOffsetAndMultipleTargets(string type, int count, string? reason) =>
        Assert.Equal(reason, CorridorEngineeringInputs.TargetOptionReadability(type, count));
    [Fact] public void OffsetRejectsSlopeSelectionOptions()
    {
        Assert.NotNull(CorridorEngineeringInputs.TargetOptionRefusal("Offset", 2, "Flattest"));
        Assert.NotNull(CorridorEngineeringInputs.TargetOptionRefusal("Surface", 2, "Nearest"));
        Assert.Null(CorridorEngineeringInputs.TargetOptionRefusal("Offset", 2, "Nearest"));
        Assert.Null(CorridorEngineeringInputs.TargetOptionRefusal("Elevation", 2, "Steepest"));
    }
    [Fact] public void ConstantSectionVolumeIsAreaTimesLength()
    { var r = CorridorEngineeringInputs.Integrate(new[] { 0d, 5d, 20d }, new double?[] { 3, 3, 3 }); Assert.Equal(60, Hz.Num(r, "estimated_volume")); Assert.True(Hz.Bool(r, "complete")); }
    [Fact] public void VaryingSectionUsesEndAreaAverage()
    { var r = CorridorEngineeringInputs.Integrate(new[] { 0d, 10d }, new double?[] { 2, 6 }); Assert.Equal(40, Hz.Num(r, "estimated_volume")); }
    [Fact] public void MissingShapeNeverBridgesTheGap()
    { var r = CorridorEngineeringInputs.Integrate(new[] { 0d, 5d, 10d, 15d }, new double?[] { 2, 2, null, 2 }); Assert.Null(r["estimated_volume"]); Assert.Equal(10, Hz.Num(r, "covered_volume_estimate")); Assert.Equal(10, Hz.Num(r, "missing_length")); }
    [Fact] public void SingleStationCannotProduceVolume()
    { var r = CorridorEngineeringInputs.Integrate(new[] { 1d }, new double?[] { 4 }); Assert.Null(r["estimated_volume"]); Assert.False(Hz.Bool(r, "complete")); }
    [Fact] public void InvalidStationOrderAndNegativeAreasRefused()
    { Assert.Throws<ArgumentException>(() => CorridorEngineeringInputs.Integrate(new[] { 2d, 1d }, new double?[] { 1, 1 })); Assert.Throws<ArgumentException>(() => CorridorEngineeringInputs.Integrate(new[] { 0d, 1d }, new double?[] { 1, -1 })); }
    [Fact] public void OverflowIsRefusedBeforeSerialization()
    { Assert.Throws<ArgumentException>(() => CorridorEngineeringInputs.Integrate(new[] { -1e308, 1e308 }, new double?[] { 1, 1 })); Assert.Throws<ArgumentException>(() => CorridorEngineeringInputs.Integrate(new[] { 0d, 100d }, new double?[] { 1e308, 1e308 })); }
    [Theory]
    [InlineData("{\"name\":\"C\",\"region_index\":0.5}")]
    [InlineData("{\"name\":\"C\",\"max_stations\":2001}")]
    [InlineData("{\"name\":\"C\",\"start_station\":10,\"end_station\":2}")]
    [InlineData("{\"name\":\"C\",\"shape_codes\":[\"Pave\",\"Pave\"]}")]
    public void GeometryInputsRejectUnsafeSelection(string json) => Assert.NotNull(CorridorEngineeringInputs.Validate("applied_geometry", JsonNode.Parse(json)!.AsObject()));
    [Fact] public void EmptyHandlesExplicitlyClearsTarget()
    { Assert.Null(CorridorEngineeringInputs.Validate("set_targets", JsonNode.Parse("{\"name\":\"C\",\"targets\":[{\"target_index\":0,\"handles\":[]}]}")!.AsObject())); }
    [Fact] public void TargetSelectionOptionNeedsMultipleHandles()
    {
        Assert.NotNull(CorridorEngineeringInputs.Validate("set_targets", JsonNode.Parse("{\"name\":\"C\",\"targets\":[{\"target_index\":0,\"handles\":[\"AF\"],\"target_to_option\":\"Nearest\"}]}")!.AsObject()));
        Assert.Null(CorridorEngineeringInputs.Validate("set_targets", JsonNode.Parse("{\"name\":\"C\",\"targets\":[{\"target_index\":0,\"handles\":[\"AF\",\"BC\"],\"target_to_option\":\"Nearest\"}]}")!.AsObject()));
    }
    [Theory]
    [InlineData("[{\"target_index\":0,\"handles\":[\"AF\",\"af\"]}]")]
    [InlineData("[{\"target_index\":0,\"handles\":[\"ZZ\"]}]")]
    [InlineData("[{\"target_index\":0,\"handles\":[],\"target_to_option\":\"Unknown\"}]")]
    [InlineData("[{\"target_index\":0,\"handles\":[]},{\"target_index\":0,\"handles\":[]}]")]
    public void TargetUpdatesRejectAmbiguousOrInvalidDefinitions(string rows)
    { Assert.NotNull(CorridorEngineeringInputs.Validate("set_targets", JsonNode.Parse("{\"name\":\"C\",\"targets\":" + rows + "}")!.AsObject())); }
}
