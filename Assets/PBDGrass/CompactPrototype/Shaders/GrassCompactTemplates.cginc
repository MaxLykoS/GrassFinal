// Included after the common XPBD data types and quaternion helpers.
// Prototype supports a homogeneous response type, with one local material template.
StructuredBuffer<float4> CompactYaw;
StructuredBuffer<float4> CompactAnchors;
Rod CompactRod(uint index)
{
    uint blade=index/7;
    Rod rod=RodsBuffer[index%7];
    rod.Rotation=index%7==0?CompactAnchors[blade/32]:QMul(CompactYaw[blade/32],rod.Rotation);
    return rod;
}
BoneInfo CompactRest(uint index)
{
    uint blade=index/8;
    BoneInfo node=RestNodesBuffer[index%8];
    node.Position=QRotate(CompactYaw[blade/32],node.Position);
    node.Predicted=node.Position;
    node.OriginPos=BladesBuffer[blade].Root+node.Position;
    return node;
}
