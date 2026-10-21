        void CompactVertex(uint index,out float3 world,out float3 normal,out float3 width)
        {
            uint blade=index/15,local=index%15,node=local==14?7:local/2,segment=min(node,6);
            int poolOffset=_GrassLodGridStates[blade/32].poolOffset;
            if(poolOffset>=0)
            {
                uint address=(poolOffset+blade%32)*15;
                world=VertexBuffer[address+local];normal=SafeNormal(NormalBuffer[address+local]);
                width=SafeNormal(VertexBuffer[address+segment*2]-VertexBuffer[address+segment*2+1]);
                return;
            }
            float3 p=CompactPosition(blade,node);
            float3 t=SafeNormal(CompactPosition(blade,segment+1)-CompactPosition(blade,segment));
            width=CompactWidth(blade,segment);width=SafeNormal(width-t*dot(width,t));
            normal=SafeNormal(cross(width,t));
            world=BladesBuffer[blade].root+p;
            if(local!=14)world+=width*CompactHalfWidths[segment]*(local%2==0?1:-1);
        }
