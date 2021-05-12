using System;
using UnityEngine;

namespace PBD
{
    internal class BodySphereContact : IDisposable
    {
        private readonly GrassBody body;
        private readonly int i0;  // collided vertex index

        private readonly Vector3 targetPos;

        internal BodySphereContact(GrassBody grassBody, int i0, Vector3 targetPos)
        {
            this.body = grassBody;
            this.i0 = i0;
            this.targetPos = targetPos;
        }

        public void Dispose()
        {
            
        }

        internal void ResolveContact(float di)
        {
            body.Positions[i0] = targetPos;
            body.NewPositions[i0] = targetPos;
            body.Velocities[i0] = Vector3.zero;

            Dispose();
        }
    }
}
