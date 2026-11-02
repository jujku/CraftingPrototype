using System;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>人物碰撞和手持分层的显示参数；人为设定、未标定，不进入调酒核心。</summary>
    [Serializable]public sealed class PlayerEnvironmentSettings
    {
        public string note;
        public float radius=.18f,minimumHeight=1.6f,headMargin=.12f,skin=.015f,stepOffset=.12f,gravity=9.81f;
        public string heldLayer="BarHeld",heldLiquidLayer="BarHeldLiquid";
        public int Held=>LayerMask.NameToLayer(heldLayer);
        public int Liquid=>LayerMask.NameToLayer(heldLiquidLayer);
        public int Mask=>(1<<Held)|(1<<Liquid);
        private static PlayerEnvironmentSettings cached;
        public static PlayerEnvironmentSettings Load()=>cached??=JsonUtility.FromJson<PlayerEnvironmentSettings>(Resources.Load<TextAsset>("BarPlayerEnvironment").text);
    }
}
