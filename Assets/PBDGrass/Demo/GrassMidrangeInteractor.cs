using UnityEngine;
namespace GrassFinal.PBDGrass
{
    public sealed class GrassMidrangeInteractor : MonoBehaviour
    {
        public GrassHandoffDemo Demo;
        private float phase;
        private bool enabledContact=true;
        private void Update()
        {
            if(Demo==null || Demo.Cam==null)return;
            if(Input.GetKeyDown(KeyCode.I))enabledContact=!enabledContact;
            var shape=GetComponent<GrassInteractionCollider>();if(shape!=null)shape.enabled=enabledContact;
            phase+=Time.deltaTime;
            Vector3 forward=Vector3.ProjectOnPlane(Demo.Cam.transform.forward,Vector3.up).normalized;
            if(forward.sqrMagnitude<.01f)forward=Vector3.forward;
            Vector3 p=Demo.Cam.transform.position+forward*24+Vector3.Cross(Vector3.up,forward)*(Mathf.Sin(phase*.8f)*3);
            p.y=Demo.transform.position.y+.4f;transform.position=p;
        }
        private void OnGUI()
        {
            if(Demo==null || Demo.Renderer==null)return;
            GUI.Box(new Rect(12,296,850,52),GUIContent.none);
            GUI.Label(new Rect(24,300,820,24),"I: middle collider "+(enabledContact?"ON":"OFF")+". Local interaction uses the same fixed 32768-blade XPBD pool.");
            GUI.Label(new Rect(24,324,820,24),"Middle: full to 28m, radial morph / swap 28-36m. Warm lookahead: "+Demo.Renderer.Handoff.WarmLookAheadMeters.ToString("F1")+"m.");
        }
    }
}
