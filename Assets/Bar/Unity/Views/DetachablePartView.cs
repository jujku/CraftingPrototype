using System.Linq;
using UnityEngine;

namespace Bar.Unity
{
    /// <summary>把和宿主建在同一个模型里的可拆部件（例如瓶盖）分离成独立的场景物品，使它能被拿起、放下和装回。</summary>
    public static class DetachablePartView
    {
        /// <summary>从宿主视图中取出部件网格节点，建立独立视图和宿主插槽。</summary>
        /// <precondition>宿主尚未 Bind；nodeName 指向宿主层级中含渲染器的部件节点；slotName 在宿主中尚不存在。</precondition>
        /// <effects>在宿主根下新建一个插槽，位于部件底面中心；部件节点保持世界位姿，移到插槽下新建的直立根物体中，并加上碰撞体和 ItemView。返回该视图；找不到节点时返回空，不改宿主。</effects>
        public static ItemView Extract(ItemView host, string nodeName, string slotName, string definitionId, string instanceId)
        {
            var node = host.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == nodeName && t.GetComponentInChildren<MeshRenderer>(true));
            if (!node) return null;
            // Measure in the host's frame so the slot is right however the host was placed in the scene.
            var bounds = new Bounds();
            bool found = false;
            foreach (var filter in node.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.sharedMesh) continue;
                var mesh = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mesh.center + Vector3.Scale(mesh.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var point = host.transform.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    if (found) bounds.Encapsulate(point); else { bounds = new Bounds(point, Vector3.zero); found = true; }
                }
            }
            if (!found) return null;
            var slot = new GameObject(slotName).transform;
            slot.SetParent(host.transform, false);
            slot.localPosition = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            var root = new GameObject(host.name + " · " + nodeName) { layer = host.gameObject.layer };
            root.transform.SetParent(slot, false);
            node.SetParent(root.transform, true);
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0, bounds.size.y * .5f, 0);
            collider.size = bounds.size;
            var view = root.AddComponent<ItemView>();
            view.DefinitionId = definitionId;
            view.InstanceId = instanceId;
            return view;
        }
    }
}
