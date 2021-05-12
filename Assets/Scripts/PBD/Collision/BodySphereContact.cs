using System;
using UnityEngine;

namespace PBD
{
    internal class BodySphereContact : IDisposable
    {
        private readonly GrassBody body;
        private readonly int i0;  // collided vertex index
        private readonly float offset;
        private Vector3 b2g; // normalized

        internal BodySphereContact(GrassBody grassBody, int i0, float offset, Vector3 b2g)
        {
            this.body = grassBody;
            this.i0 = i0;
            this.offset = offset;
            this.b2g = b2g;
        }

        public void Dispose()
        {
            
        }

        internal void ResolveContact(float di)
        {
            Vector3 delta = b2g * offset * di;
            body.NewPositions[i0] += delta;
            body.Positions[i0] += delta;
            body.Velocities[i0] = Vector3.zero;

            Dispose();
        }
    }
}
