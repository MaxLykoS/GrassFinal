using UnityEngine;
namespace GrassFinal.PBDGrass
{
    // Optional shape on a listed interaction transform; legacy transforms remain spheres.
    public sealed class GrassInteractionCollider : MonoBehaviour
    {
        public enum Shape { Sphere, Capsule }
        public Shape Type = Shape.Capsule;
        [Min(.001f)] public float Radius = .3f;
        [Min(0)] public float HalfSegment = .4f;
        [Range(0, 1)] public float Friction = .45f;
        public Vector3 LocalCenter;
        public void Endpoints(out Vector3 a, out Vector3 b)
        {
            Vector3 c = transform.TransformPoint(LocalCenter);
            Vector3 axis = transform.up * (Type == Shape.Capsule ? HalfSegment * Mathf.Abs(transform.lossyScale.y) : 0);
            a = c-axis; b = c+axis;
        }
        public float WorldRadius => Radius*Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));
        private void OnDrawGizmosSelected()
        {
            Endpoints(out Vector3 a, out Vector3 b); Gizmos.color = new Color(1,.5f,0);
            Gizmos.DrawWireSphere(a, WorldRadius); Gizmos.DrawWireSphere(b, WorldRadius); Gizmos.DrawLine(a,b);
        }
    }
}
