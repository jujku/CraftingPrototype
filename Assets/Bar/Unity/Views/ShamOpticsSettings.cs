using System;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>高球杯厚底的表现参数；不改变模型或内腔。数值人为设定、未标定。</summary>
    [Serializable]public sealed class ShamOpticsSettings
    {
        public float innerFilletMetres=.006f, absorptionScale=.12f, refractionScale=.15f, internalReflection=1;
        public static ShamOpticsSettings Load()=>JsonUtility.FromJson<ShamOpticsSettings>(Resources.Load<TextAsset>("BarShamOptics").text);
    }
}
