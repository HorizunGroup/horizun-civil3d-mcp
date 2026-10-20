using Horizun.Civil3D.Core;
using System.Text.Json.Nodes;
namespace Horizun.Civil3D.Core.Tests;
public class SheetInputsTests
{
    [Theory]
    [InlineData(1,0,0)]
    [InlineData(0,1,Math.PI/2)]
    [InlineData(0,-1,-Math.PI/2)]
    public void Camera_follows_world_tangent(double x,double y,double twist)=>Assert.Equal(twist,SheetInputs.Twist(0,0,x,y),9);
    [Fact] public void Degenerate_tangent_refuses()=>Assert.Throws<ArgumentException>(()=>SheetInputs.Twist(1,1,1,1));
    [Theory]
    [InlineData(1,1)] [InlineData(-2,3)] [InlineData(3,-2)]
    public void Wcs_to_dcs_rotation_makes_station_chord_horizontal(double x,double y)
    {
        var twist=SheetInputs.Twist(0,0,x,y);
        Assert.Equal(0,-Math.Sin(twist)*x+Math.Cos(twist)*y,10);
        Assert.True(Math.Cos(twist)*x+Math.Sin(twist)*y>0);
    }
    [Fact] public void Required_fields_and_ignored_elevation_are_rejected()
    {
        Assert.NotNull(SheetInputs.ValidateCreate(new JsonObject()));
        var a=JsonNode.Parse("{\"layout\":\"Sheet\",\"alignment\":\"Road\",\"center\":{\"x\":0,\"y\":0,\"z\":5},\"width\":20,\"height\":10,\"scale\":1,\"station\":0}")!.AsObject();
        Assert.NotNull(SheetInputs.ValidateCreate(a));
    }
    [Fact] public void Invalid_scale_cannot_author_a_sheet()
    {
        var args=JsonNode.Parse("{\"layout\":\"Sheet\",\"alignment\":\"Road\",\"center\":{\"x\":0,\"y\":0},\"width\":20,\"height\":10,\"scale\":0,\"station\":0}")!.AsObject();
        Assert.NotNull(SheetInputs.ValidateCreate(args));
    }
}
