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

        public List<GrassBody> Bodies { get; private set; }
        public List<SphereCollision> Collisions { get; private set; } // balls

        public PBDSolver(float friction, int solverIteration = 4, int collisionIterations = 2)
        {
            this.SolverIteration = solverIteration;
            this.CollisionIterations = collisionIterations;
            this.Friction = friction;
            this.StopThreshold = 0.1f;

            this.Bodies = new List<GrassBody>();
            this.Collisions = new List<SphereCollision>();
        }

        public void AddGrass(GrassBody body)
        {
            if(!Bodies.Contains(body))
                Bodies.Add(body);
        }
        public void RemoveGrass(GrassBody body)
        {
            Bodies.Remove(body);
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
            foreach (GrassBody body in Bodies)
            {
                #region ApplyForce And Calculate NewPosition
                for (int i = 0; i < body.GrassMesh.vertexCount; i++)
                {
                    // air friction
                    body.Velocities[i] -= body.Velocities[i] * Friction * dt;

                    // gravity
                    body.Velocities[i] += Gravity * dt;

                    // recovery force
                    body.Velocities[i] += (body.OriginPos[i] - body.NewPositions[i]) * dt;

                    // wind force
                    body.Velocities[i] += WindForce * dt;

                    // update position with new velocity
                    body.NewPositions[i] = body.Positions[i] + dt * body.Velocities[i];
                }
                #endregion

                #region Resolve Collisions
                List<BodySphereContact> contacts = new List<BodySphereContact>();

                for (int i = 0; i < Collisions.Count; ++i)
                    Collisions[i].FindContacts(Bodies, contacts);

                float di = 1.0f / CollisionIterations;

                for (int i = 0; i < CollisionIterations; ++i)
                    for (int j = 0; j < contacts.Count; ++j)
                        contacts[j].ResolveContact(di);
                #endregion

                #region Do Constraints
                // constraints
                float stepDT = 1.0f / SolverIteration;
                for (int i = 0; i < SolverIteration; i++)
                {
                    foreach (DistanceConstraint c in body.Dcons) c.DoConstraint(stepDT);
                    foreach (FixedConstraint c in body.Fcons) c.DoConstraint(stepDT);
                }
                #endregion

                #region Floor Checking
                // bounds checking
                for (int i = 0; i < body.NewPositions.Length; ++i)
                {
                    if (body.NewPositions[i].y < 0)
                    {
                        body.NewPositions[i].y = 0.1f;
                        body.Positions[i].y = 0.1f;
                    }
                }
                #endregion

                #region Update Velocities
                float threshold2 = StopThreshold * dt;
                threshold2 *= threshold2;
                for (int i = 0; i < body.GrassMesh.vertexCount; i++)
                {
                    Vector3 dist = body.NewPositions[i] - body.Positions[i];
                    body.Velocities[i] = dist / dt;
                    if (body.Velocities[i].sqrMagnitude < threshold2)
                        body.Velocities[i].x = body.Velocities[i].y = body.Velocities[i].z = 0;
                }
                #endregion

                #region Save NewPosition to Position
                for (int i = 0; i < body.GrassMesh.vertexCount; i++)
                    body.Positions[i] = body.NewPositions[i];
                #endregion

                // save calculated positions to mesh
                body.UpdateMesh();
            }
        }
    }
}