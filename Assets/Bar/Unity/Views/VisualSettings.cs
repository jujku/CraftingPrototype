using System;
namespace Bar.Unity
{
    /// <summary>表现 A 的数据参数；人为设定、未标定。</summary>
    [Serializable] public sealed class VisualSettings
    {
        public float iceEntrySeconds=.22f,iceEntryArc=.06f,iceSide=.037f;
        public float tiltGain=2;
        public float springFrequency=12,damping=.22f,syrupDamping=.35f,maxTilt=35,accelerationGain=.02f,meniscus=.002f,foamBand=.003f;
        public float minimumPathCm=1.5f,maximumPathCm=6.5f,scatteringDistanceCm=1.5f,scatteringGain=.8f;
        public float gravity=9.81f,accelerationClamp=25,accelerationFilter=12,wallThickness=.03f,contactOffset=.004f,depenetration=1.5f,sleep=.002f;
        public float dryDrag=.1f,wetDrag=3.5f,dryAngular=.05f,wetAngular=2.5f,spoonRadius=.004f,vortex=.12f,seedJitter=0,removeScale=.12f;
        public int wallCount=16,solverIterations=10,maxIce=20,streamSegments=12,streamSides=8;
        public float physicsStep=1f/60,shakeStep=1f/120,initialStreamSpeed=.5f,streamRadiusScale=1,splashRate=18,rippleSeconds=.5f;
    }
}
