using System;
using UnityEngine;
namespace Bar.Unity
{
    [Serializable]public sealed class GuidedPourSettings
    {
        public float tiltDegrees,tipDepth,entryClearance,contactFraction,mouthGap,frequency,zeta,returnSeconds,filmRadius,directFizzGain,directFoamMetres,foamDecaySeconds,foamLossGrams,fullFizzLossGramsPerSecond;
        public void Validate(){foreach(float x in new[]{tiltDegrees,tipDepth,entryClearance,contactFraction,mouthGap,frequency,zeta,returnSeconds,filmRadius,directFizzGain,directFoamMetres,foamDecaySeconds,foamLossGrams,fullFizzLossGramsPerSecond})if(float.IsNaN(x)||float.IsInfinity(x)||x<=0)throw new InvalidOperationException("BarPourSettings.guided requires finite positive values");if(contactFraction>=1||tiltDegrees>=60||directFizzGain>1)throw new InvalidOperationException("BarPourSettings.guided geometry/gain out of range");}
    }
    /// <summary>P 段的表现、几何近似与光学参数。JSON 中的初值均人为设定、未标定；不写入核心。</summary>
    [Serializable]public sealed class PourSettings
    {
        public string note;
        public GuidedPourSettings guided;
        public float alignSeconds=.25f,returnSeconds=.25f,mouthClearance=.06f,aimResponse=20;
        public float entrySideGap=.015f,impactRimInset=.0005f;
        public float uprightDegreesPerSecond=480;
        public Vector3 heldReceiverPosition=new Vector3(0,-.18f,.48f);
        public float outletFallbackRadius=.009f,minimumStreamRadius=.00045f,dropletThresholdMlPerSecond=2,gravity=9.81f;
        public float shakerOpenStreamRadius=.0045f,shakerStrainerStreamRadius=.004f;
        public float shakerMaximumLateralSpeed=.02f,shakerLateralSpeedFraction=.1f;
        public float jiggerStreamRadius=.003f,jiggerMaximumLateralSpeed=.02f,jiggerLateralSpeedFraction=.1f;
        public int streamSegments=32,streamSides=8;
        public float clickTiltDegrees=125,confirmationWindowSeconds=.15f,flowTransitionSeconds=.1f;
        public float bottleSurfaceHighlight=.12f;
        public float splashLifetimeSeconds=.18f,splashSpeed=.12f,splashSize=.002f,splashRadius=.004f,splashReferenceFlowMlPerSecond=45;
        public float dropletDiameter=.004f,rippleLineWidth=.001f,rippleInnerRadius=.004f,rippleGrowth=.018f;
        public int maxStreamParticles=48;
        public float bottleWallMetres=.002f,innerBottomInsetMetres=.0005f,sliceInsetMetres=.00001f;
        public int bottleSlices=193,bottleSides=64,bottleAngleSamples=401;
        public float iceIor=1.31f,iceCoreDensity=65,iceCoreSoftness=.65f,iceRefractionGain=1,iceEdgeWidth=.12f,iceTransmissionIntensity=.24f,iceTransmissionRadius=.026f;
        public Vector3 iceAbsorption=new Vector3(2.8f,.6f,.35f),iceCoreRadii=new Vector3(.40f,.52f,.40f);
        public float nucleation=0,frost=0;
        public float iceTransmissionContactDistance=.025f,iceTransmissionPenetrationTolerance=.005f,iceTransmissionRayRange=.05f;
        private static PourSettings cached;
        public static PourSettings Load(){if(cached!=null)return cached;var asset=Resources.Load<TextAsset>("BarPourSettings");if(!asset)throw new InvalidOperationException("Required BarPourSettings asset missing");var parsed=JsonUtility.FromJson<PourSettings>(asset.text);if(parsed.guided==null)throw new InvalidOperationException("Required BarPourSettings.guided config missing");parsed.guided.Validate();return cached=parsed;}
    }
}
