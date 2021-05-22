using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PBD
{
    public class DistanceConstraint : Constraint
    {
        public float RestLength { get; private set; }

        public float ElasticModulus { get; private set; }

        public int i0 { get; private set; }
        public int i1 { get; private set; }

    internal DistanceConstraint(int i0, int i1, float stiffness, PBDGrassBody body) : base(body)
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