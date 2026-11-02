using System;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>B 段的显示参数，唯一来源 Resources/BarBSettings.json；人为设定、未标定。</summary>
    [Serializable] public sealed class BPresentationSettings
    {
        public int version;public string note;
        public ShakeDisplaySettings shake=new ShakeDisplaySettings();
        public IceImpactSettings impacts=new IceImpactSettings();
        public BubbleDisplaySettings bubbles=new BubbleDisplaySettings();
        public SurfaceDisplaySettings surface=new SurfaceDisplaySettings();
        public ScoopDisplaySettings scoop=new ScoopDisplaySettings();
        public FoamDisplaySettings foam=new FoamDisplaySettings();
        public static BPresentationSettings Load()=>JsonUtility.FromJson<BPresentationSettings>(Resources.Load<TextAsset>("BarBSettings").text);
    }
    [Serializable] public sealed class ScoopDisplaySettings
    {public float clearanceMetres=.0005f,blockGapMetres=.0015f;}
    [Serializable] public sealed class ShakeDisplaySettings
    {
        // The shake motion itself moved to BarHoldView.json "shake" (HoldShakeSettings, 2026-10-28); only the ice-settling values stay here.
        public float settleSeconds,settleHardSeconds,settleSpeed,settleAngularSpeed,wakeAcceleration,wakeRotationDegrees,wakeIceGrams,wakeLiquidMl;
    }
    [Serializable] public sealed class IceImpactSettings
    {public float minimumImpulse,fullImpulse,pairCooldown;public int eventsPerSecond,historyCount;}
    [Serializable] public sealed class BubbleDisplaySettings
    {
        public int sourceCount,maxParticlesPerSource;public float criticalCo2,fullCo2,rate,referenceTemperature,temperatureFactor;
        public float acceleration,smallSize,largeSize,noiseStrength,noiseFrequency,noiseScroll,burstSeconds,burstGain,popSeconds,seedRadius,microStrength,microTiling;
        public float initialSpeed=.04f,growthPerSecond=.00035f,minimumPixelSize=1.2f,rateLogSigma=.6f;
    }
    [Serializable] public sealed class SurfaceDisplaySettings
    {
        public float frostOnset,frostFull,frostGrowth,frostMelt,wetDry,mistDewPoint,mistRange,dropOnset,dropRange,mistSeconds,dropSeconds,runSeconds;
        public float wipeRadius,wipeFeather,wipeRecovery,textureTiling,mistBlurPixels,mistTintGain,dropNormalGain,dropRefraction,runSpeed,frostSoftness,iceFrost;
        public float frostCap=.8f,frostWipeRecovery=18,frostGrowthTiling=4,frostNormalStrength=.2f,frostWhiteBlend=.4f,dropHighlight=1,scatteringGain=5;
        public float frostWipeRadius=.014f,metalContactOffset=.001f;
        // the tin fogs at the dew point, then frosts.
        // 2026-10-28 (Tools/WallFrost): with core 2.2 the shaken wall really goes below 0 °C, so frost starts at metalFrostOnset 0 °C and builds at a rate:
        // full in metalFrostSeconds at metalFrostFull or colder while moving at metalFrostAirSpeed m/s, metalFrostRestFactor of that when still;
        // above 0 °C it melts in metalFrostMeltSeconds once metalFrostMeltRange degrees above. Game settings, not calibrated.
        // After the playtest the same day: full in 5 s (was 8), and no new fog or frost once the ice is gone.
        // Third pass (the user's design): frost grows at (metalFrostRestRate + metalFrostShakeRate × shake intensity) × coldness per second,
        // so even a full-strength shake needs about 9 s from bare to full; a wipe locks fog and frost until the tin is shaken harder than
        // metalWipeUnlockIntensity again, and while locked fog only creeps back at metalWipedFogGrowth. Growth needs shaking above
        // metalShakeThreshold or ice in the tin.
        public float metalDewPoint=15,metalFogRange=6,metalFogGrowth=.5f,metalFogDry=.04f,metalFrostOnset=0,metalFrostFull=-4;
        public float metalFrostRestRate=.012f,metalFrostShakeRate=.1f,metalShakeThreshold=.1f,metalWipeUnlockIntensity=.2f,metalWipedFogGrowth=.005f,metalFrostMeltSeconds=20,metalFrostMeltRange=3;
        /// <summary>V2 擦干：雾、霜、水在 metalWipeSeconds 内擦掉（人为设定、未标定）。</summary>
        public float metalWipeSeconds=.45f;
    }
    [Serializable] public sealed class FoamDisplaySettings
    {public string note="人为设定、未标定，不是物理";public float shakeBuildSeconds=8,decaySeconds=100,transferRetention=.85f,maximumHeightMetres=.007f,tauForFullFoam=1;}
}
