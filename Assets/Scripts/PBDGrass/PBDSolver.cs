using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PBD
{
    public class PBDSolver
    {
        public Vector3 Gravity { get; set; }
        public Vector3 WindForce { get; set; }
        public float Friction { get; set; }
        public float StopThreshold { get; set; }
        public int SolverIteration { get; private set; }
        public int CollisionIterations { get; private set; }

        public List<GrassPatch> Patches { get; private set; }
        public List<SphereCollision> Collisions { get; private set; } // balls

        public PBDSolver(float friction, int solverIteration = 4, int collisionIterations = 1)
        {
            this.SolverIteration = solverIteration;
            this.CollisionIterations = collisionIterations;
            this.Friction = friction;
            this.StopThreshold = 0.1f;

            this.Patches = new List<GrassPatch>();
            this.Collisions = new List<SphereCollision>();
        }

        public void AddGrassPatch(GrassPatch patch)
        {
            if(!Patches.Contains(patch))
                Patches.Add(patch);
        }
        public void RemoveGrassPatch(GrassPatch patch)
        {
            Patches.Remove(patch);
        }

        public void AddCollider(SphereCollision sc)
        {
            if(!Collisions.Contains(sc))
                Collisions.Add(sc);
        }

        public void RemoveCollider(SphereCollision sc)
        {
            Collisions.Remove(sc);
        }

        public void Update(float dt)
        {
            if (dt == 0)
                return;
            foreach (GrassPatch patch in Patches)
            {
                ApplyForce(patch, dt);

                EstimatePositions(patch, dt);

                ResolveCollisions(patch);

                DoConstraints(patch);

                //FloorChecking(patch);

                UpdateVelocities(patch, dt);

                UpdatePositions(patch);

                patch.UpdateMesh();
            }
        }

        private void ApplyForce(GrassPatch patch, float dt)
        {
            foreach (GrassBody body in patch.Bodies)
            {
                for (int i = 0; i < body.BoneCounts; i++)
                {
                    // air friction
                    body.Velocities[i] -= body.Velocities[i] * Friction * dt;

                    // gravity
                    body.Velocities[i] += Gravity * dt;

                    // recovery force
                    body.Velocities[i] += (body.OriginPos[i] - body.Predicted[i]) * dt * 1000;

                    // wind force
                    body.Velocities[i] += WindForce * dt;
                }
            }
        }

        private void EstimatePositions(GrassPatch patch, float dt)
        {
            foreach (GrassBody body in patch.Bodies)
            {
                for (int i = 0; i < body.BoneCounts; i++)
                {
                    // update position with new velocity
                    body.Predicted[i] = body.Positions[i] + dt * body.Velocities[i];
                }
            }
        }

        private void ResolveCollisions(GrassPatch patch)
        {
            List<BodySphereContact> contacts = new List<BodySphereContact>();

            for (int i = 0; i < Collisions.Count; ++i)
                Collisions[i].FindContacts(patch.QueryNearBodies(Collisions[i].GetPos()), contacts);

            float di = 1.0f / CollisionIterations;

            for (int i = 0; i < CollisionIterations; ++i)
                for (int j = 0; j < contacts.Count; ++j)
                    contacts[j].ResolveContact(di);
        }

        private void DoConstraints(GrassPatch patch)
        {
            foreach (GrassBody body in patch.Bodies)
            {
                // constraints
                float stepDT = 1.0f / SolverIteration;
                for (int i = 0; i < SolverIteration; i++)
                {
                    foreach (DistanceConstraint c in body.Dcons) c.DoConstraint(stepDT);            
                }
                foreach (FixedConstraint c in body.Fcons) c.DoConstraint(0);
            }
        }

        private void FloorChecking(GrassPatch patch)
        {
            foreach (GrassBody body in patch.Bodies)
            {
                // bounds checking
                for (int i = 0; i < body.Predicted.Length; ++i)
                {
                    if (body.Predicted[i].y < 0)
                    {
                        body.Predicted[i].y = 0.1f;
                        body.Positions[i].y = 0.1f;
                    }
                }
            }
        }

        private void UpdateVelocities(GrassPatch patch, float dt)
        {
            float threshold2 = StopThreshold * dt;
            threshold2 *= threshold2;
            foreach (GrassBody body in patch.Bodies)
            {
                for (int i = 0; i < body.BoneCounts; i++)
                {
                    Vector3 dist = body.Predicted[i] - body.Positions[i];
                    body.Velocities[i] = dist / dt;
                    if (body.Velocities[i].sqrMagnitude < threshold2)
                        body.Velocities[i].x = body.Velocities[i].y = body.Velocities[i].z = 0;
                }
            }
        }

        private void UpdatePositions(GrassPatch patch)
        {
            foreach (GrassBody body in patch.Bodies)
            {
                for (int i = 0; i < body.BoneCounts; i++)
                {
                    body.Positions[i] = body.Predicted[i];
                }
            }
        }
    }
}