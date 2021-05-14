using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PBD
{
    public class DistanceConstraint : Constraint
    {
        private float RestLength;

        private float ElasticModulus;

        private readonly int i0, i1;

        internal DistanceConstraint(int i0, int i1, float stiffness, GrassBody body) : base(body)
        {
            this.i0 = i0;
            this.i1 = i1;

            this.ElasticModulus = stiffness;
            this.RestLength = (body.Positions[i0] - body.Positions[i1]).magnitude;
        }

        public override void DoConstraint(float dt)
        {
            float invMass = 1.0f / body.Mass;
            float sum = body.Mass * 2.0f;

            Vector3 n = body.Predicted[i1] - body.Predicted[i0];
            float d = n.magnitude;
            n.Normalize();

            Vector3 corr = ElasticModulus * n * (d - RestLength) * sum;

            body.Predicted[i0] += invMass * corr * dt;

            body.Predicted[i1] -= invMass * corr * dt;

        }
    }
}