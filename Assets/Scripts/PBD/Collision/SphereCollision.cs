﻿using System.Collections.Generic;
using UnityEngine;

namespace PBD
{
    public class SphereCollision
    {
        private Transform Tr { get; set; }
        private float Radius { get; set; }

        public SphereCollision(Transform tr, float r)
        {
            this.Tr = tr;
            this.Radius = r;
        }

        // TODO 用哈希优化
        internal void FindContacts(IList<GrassBody> bodies, List<BodySphereContact> contacts)
        {
            for (int j = 0; j < bodies.Count; j++)
            {
                GrassBody grassBody = bodies[j];

                int numParticles = grassBody.GrassMesh.vertexCount;

                for (int i = 0; i < numParticles; ++i)
                {
                    Vector3 b2g = grassBody.NewPositions[i] - Tr.position;
                    float offset = b2g.magnitude - Radius;

                    if (offset <= 0)
                        contacts.Add(new BodySphereContact(grassBody, i, -offset, b2g.normalized));
                }
            }
        }
    }
}