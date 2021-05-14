using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PBD
{
    public abstract class Constraint
    {
        protected GrassBody body { get; private set; }

        protected Constraint(GrassBody body)
        {
            this.body = body;
        }

        public abstract void DoConstraint(float dt);
    }
}