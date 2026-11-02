using UnityEngine;
namespace Bar.Unity
{
    /// <summary>为现有可见建筑网格补静态碰撞；不保存场景，不移动墙或柜台，不生成坐标边界。</summary>
    public static class SceneCollisionSetup
    {
        public static int Refresh(PlayerController player)
        {
            int added=0;
            foreach(var filter in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                var renderer=filter.GetComponent<MeshRenderer>();
                if(!filter.sharedMesh||!renderer||!renderer.enabled||!filter.gameObject.activeInHierarchy)continue;
                if(filter.transform.IsChildOf(player.transform)||filter.GetComponentInParent<PresentationRig>()||filter.GetComponentInParent<GinzaEnvironmentTag>())continue;
                var item=filter.GetComponentInParent<ItemView>();
                if(item&&item.Item is not Bar.Domain.Fixture)continue;
                bool covered=false;
                for(var t=filter.transform;t;t=t.parent)
                {
                    foreach(var collider in t.GetComponents<Collider>())if(collider.enabled&&!collider.isTrigger){covered=true;break;}
                    if(covered||item&&t==item.transform)break;
                }
                if(covered)continue;
                var mesh=filter.gameObject.AddComponent<MeshCollider>();mesh.sharedMesh=filter.sharedMesh;mesh.convex=false;added++;
            }
            Physics.SyncTransforms();return added;
        }
    }
}
