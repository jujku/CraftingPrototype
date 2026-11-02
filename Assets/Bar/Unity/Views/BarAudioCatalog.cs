using System;
using UnityEngine;
namespace Bar.Unity
{
    [Serializable] public sealed class AudioBank {public string id;public string[] paths;public bool loop;public int loopStartSample;}
    /// <summary>许可采样、离线 WAV 和混音初值的数据清单。</summary>
    [Serializable] public sealed class BarAudioCatalog
    {
        public string note;public AudioBank[] banks;
        public int poolSize,iceConcurrency,otherConcurrency,loopPaddingSamples,sampleRate,minnaertVariants;
        public float pairCooldown,pitchVariation,volumeVariationDb,spatialBlend,loopFadeSeconds,pourPitchLow,pourPitchHigh,flowFullMlPerSecond,gulpAngle,gulpInterval,dilutionFullPercent;
        public float bubbleRadiusMin,bubbleRadiusMax,bubbleSeconds,waterSeconds,waterBubblesPerSecond,bubbleDecay,bubbleSweep,bubbleNoise,bubbleGain;
        public float audibleDistance,minimumDistance,actionGain,pourThinGain,pourThickGain,tailGain,gulpGain,shakerGain,stirGain,fizzGain,pressGain,sloshGain,iceMassFullGrams,sloshPitch,observationSeconds,gainChange,pitchChange;
        public float strongImpactThreshold,smallIcePitch,largeIcePitch;
        public float masterGain,attackSeconds,releaseSeconds,gainSmoothingSeconds,pourLowFlowGain;
        public float scheduleSeconds=.03f,impactMinimum=.004f,impactFull=.015f,strokeGain=.7f,strokeSpeedFull=12,shakerBedGain=.05f,strokeBottomBright=.7f,strokeCapDull=.7f,catchGain=.6f,bedMidStroke=.6f,stirBedStill=.3f,stirClinkGain=.18f,stirClinkPitchLow=1.05f,stirClinkPitchHigh=1.2f,placeGain=.5f,iceLandingGain=.5f,landingSpacingSeconds=.025f,stealDecaySeconds=.15f;public bool openIceImpactSounds,iceLandingSounds=true,sealedIceOnlyWhileShaking=true,stirClinks=true;
        public static BarAudioCatalog Load()=>JsonUtility.FromJson<BarAudioCatalog>(Resources.Load<TextAsset>("BarAudioCatalog").text);
    }
}
