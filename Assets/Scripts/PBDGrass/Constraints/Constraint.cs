using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PBD
{
    public abstract class Constraint
    {
        protected PBDGrassBody body { get; private set; }

        protected Constraint(PBDGrassBody body)
        {
            this.body = body;
        }

        public abstract void DoConstraint(float dt);
    }
}