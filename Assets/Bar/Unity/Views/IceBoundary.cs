using UnityEngine;
namespace Bar.Unity
{
    /// <summary>仅本地物理场景的包围触发器，报告冰块越界；不访问模型。</summary>
    public sealed class IceBoundary : MonoBehaviour
    {
        public int Exits {get;private set;}
        private void OnTriggerExit(Collider other){if(other.attachedRigidbody&&!other.attachedRigidbody.isKinematic)Exits++;}
    }
}
