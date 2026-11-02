using UnityEngine;
namespace Bar.Unity
{
    /// <summary>已装配环境的明确标识：碰撞由编辑器提供，灯光保留独立的设计值。</summary>
    public sealed class GinzaEnvironmentTag:MonoBehaviour
    {
        public string revision="GinzaSpeakeasy-2026-10-30";
        public TextAsset manifest;
        public float clearBackbarCorridorMetres=1.35f,minimumSideCorridorMetres=1.3f;
    }
}
