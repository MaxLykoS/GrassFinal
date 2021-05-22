using System.Collections.Generic;
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

        internal void FindContacts(List<PBDGrassBody> possibleBodies, List<BodySphereContact> contacts)
        {
            if (possibleBodies == null)
                return;
            for (int j = 0; j < possibleBodies.Count; j++)
            {
                PBDGrassBody grassBody = possibleBodies[j];

                int numParticles = grassBody.BoneCounts;

                for (int i = 0; i < numParticles; ++i)
                {
                    Vector3 b2g = grassBody.Predicted[i] - Tr.position;
                    float offset = b2g.magnitude - Radius;

                    if (offset <= 0)
                    {
                        contacts.Add(new BodySphereContact(grassBody, i, Tr.position + b2g.normalized * Radius));
                    }
                }
            }
        }

        public Vector3 GetPos()
        {
            return Tr.position;
        }
    }
}
