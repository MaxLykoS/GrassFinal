using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PBD
{
    public class FixedConstraint : Constraint
    {
        public int i0 { get; private set; }
        public Vector3 fixedPos;

        public FixedConstraint(int i0, PBDGrassBody body) : base(body)
        {
            this.i0 = i0;
            this.fixedPos = body.Positions[i0];
        }
        public override void DoConstraint(float dt)
        {
            body.Positions[i0] = fixedPos;
            body.Predicted[i0] = fixedPos;
        }
    }
}