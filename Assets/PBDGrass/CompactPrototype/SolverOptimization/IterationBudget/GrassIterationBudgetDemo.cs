using UnityEngine;

namespace GrassFinal.PBDGrass
{
    // The accepted low-budget material has its own response bank. Debug budget
    // switches preserve live physics/plastic/history and rebind only material tables.
    [DefaultExecutionOrder(-300)]
    public sealed class GrassIterationBudgetDemo : MonoBehaviour
    {
        public enum Budget { Reference8x3, Reduced8x1, Balanced4x2, Fast3x1, Minimum2x1 }
        public GrassHandoffDemo Demo;
        public Budget Quality=Budget.Minimum2x1;
        public GrassResponseType ReferenceResponse,MinimumResponse;
        [Tooltip("Apply the support calibration only to the 2x1 budget. F10 compares it with the original material.")]
        public bool TuneMinimumBudget=true;
        [Range(.1f,1f)] public float MinimumSoftComplianceScale=.5f;
        private Budget applied;
        private bool appliedTuning;
        private float appliedScale,originalSoftCompliance;
        private XPBDGrassSettings originalSettings;

        private void OnEnable() => Apply();
        private void OnDisable()
        {
            if(originalSettings!=null)originalSettings.SoftBendCompliance=originalSoftCompliance;
            originalSettings=null;
        }
        private void Update()
        {
            if(Input.GetKeyDown(KeyCode.F5))Quality=Budget.Reference8x3;
            if(Input.GetKeyDown(KeyCode.F6))Quality=Budget.Reduced8x1;
            if(Input.GetKeyDown(KeyCode.F7))Quality=Budget.Balanced4x2;
            if(Input.GetKeyDown(KeyCode.F8))Quality=Budget.Fast3x1;
            if(Input.GetKeyDown(KeyCode.F9))Quality=Budget.Minimum2x1;
            if(Input.GetKeyDown(KeyCode.F10))TuneMinimumBudget=!TuneMinimumBudget;
            if(Quality!=applied || TuneMinimumBudget!=appliedTuning || MinimumSoftComplianceScale!=appliedScale)Apply();
        }
        public void Apply()
        {
            if(Demo==null)return;
            if(originalSettings!=Demo.Physics)
            {
                if(originalSettings!=null)originalSettings.SoftBendCompliance=originalSoftCompliance;
                originalSettings=Demo.Physics;originalSoftCompliance=Demo.Physics.SoftBendCompliance;
            }
            int steps=8,iterations=1;
            switch(Quality)
            {
                case Budget.Reference8x3:iterations=3;break;
                case Budget.Balanced4x2:steps=4;iterations=2;break;
                case Budget.Fast3x1:steps=3;break;
                case Budget.Minimum2x1:steps=2;break;
            }
            Demo.Physics.NearSubsteps=steps;Demo.Physics.ConstraintIterations=iterations;applied=Quality;
            Demo.Physics.SoftBendCompliance=originalSoftCompliance*
                (Quality==Budget.Minimum2x1 && TuneMinimumBudget?Mathf.Clamp(MinimumSoftComplianceScale,.1f,1):1);
            var response=Quality==Budget.Minimum2x1 && TuneMinimumBudget && Mathf.Approximately(MinimumSoftComplianceScale,.5f)
                ?MinimumResponse:ReferenceResponse;
            if(response!=null && response!=Demo.Handoff.BakedResponse)
            {
                if(Demo.Renderer!=null)Demo.Renderer.Handoff.SetBakedResponse(response);
                else Demo.Handoff.BakedResponse=response;
                Demo.OuterCoverage?.SetBakedResponse(response);
            }
            appliedTuning=TuneMinimumBudget;appliedScale=MinimumSoftComplianceScale;
        }
        private void OnGUI()
        {
            if(Demo==null)return;
            float left=Mathf.Max(12,Screen.width-454);
            GUI.Box(new Rect(left,12,442,144),GUIContent.none);
            GUI.Label(new Rect(left+12,18,420,22),"ITERATION EXPERIMENT   "+Demo.Physics.NearSubsteps+" substeps x "+Demo.Physics.ConstraintIterations+" iterations");
            GUI.Label(new Rect(left+12,40,420,22),"F5: 8x3 original    F6: 8x1 reduced");
            GUI.Label(new Rect(left+12,62,420,22),"F7: 4x2 balanced    F8: 3x1 fast");
            GUI.Label(new Rect(left+12,84,420,22),"F9: 2x1 minimum   bank: "+(Demo.Handoff.BakedResponse==MinimumResponse?"matched 2x1":"reference"));
            GUI.Label(new Rect(left+12,106,420,22),"F10: 2x1 support tuning ["+(TuneMinimumBudget?"ON":"OFF")+"]   soft compliance "+Demo.Physics.SoftBendCompliance.ToString("0.###"));
            GUI.Label(new Rect(left+12,128,420,22),"Switch keeps state. R resets grass and actor motion.");
        }
    }
}
