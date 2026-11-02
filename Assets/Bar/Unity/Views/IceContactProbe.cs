using System.Collections.Generic;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>本地 PhysX 的接触采样；冲量由宿主在每步比较速度后估算。</summary>
    public sealed class IceContactProbe:MonoBehaviour
    {
        internal readonly List<IceContact> Contacts=new List<IceContact>();
        private void OnCollisionEnter(Collision hit)=>Record(hit);
        private void OnCollisionStay(Collision hit)=>Record(hit);
        private void Record(Collision hit)
        {if(hit.contactCount>0){var contact=hit.GetContact(0);Contacts.Add(new IceContact{Other=hit.collider,Normal=contact.normal,Point=contact.point});}}
    }
    internal struct IceContact {internal Collider Other;internal Vector3 Normal,Point;}
    internal sealed class IceCollisionSurface:MonoBehaviour {internal IceImpactKind Kind;}
}
