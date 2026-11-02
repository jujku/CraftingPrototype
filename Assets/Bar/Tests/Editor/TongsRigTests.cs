using System.Linq;
using Bar.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace Bar.Tests
{
    internal sealed class TongsRigTests
    {
        private static GameObject Make(out TongsViewRig rig,out Transform holder,out Transform cube)
        {
            var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Bar/Prefabs/Resources/BarPrefabs/tool.tongs.prefab"));
            var hold=root.GetComponentsInChildren<Transform>().Single(t=>t.name=="HoldPoint");
            rig=root.AddComponent<TongsViewRig>();rig.Initialize(hold);Assert.That(rig.Ready,Is.True);
            holder=new GameObject("Test held ice").transform;holder.SetParent(root.transform,false);holder.SetPositionAndRotation(hold.position,hold.rotation);
            cube=GameObject.CreatePrimitive(PrimitiveType.Cube).transform;cube.name="Test ice";cube.SetParent(holder,false);Object.DestroyImmediate(cube.GetComponent<Collider>());
            return root;
        }
        [TestCase(.037f)][TestCase(.043f)] public void JawsAndTeethClearTheActualIceSize(float side)
        {
            var root=Make(out var rig,out var holder,out var cube);
            try
            {
                cube.localScale=Vector3.one*side;
                for(int frame=0;frame<30;frame++)
                {
                    rig.Advance(true,holder,1f/60);
                    if(!rig.IceVisible)continue;
                    foreach(var mesh in root.GetComponentsInChildren<MeshFilter>().Where(m=>m.name.StartsWith("Arm")||m.name.StartsWith("Tooth")))
                        foreach(var vertex in mesh.sharedMesh.vertices)
                        {
                            var p=cube.InverseTransformPoint(mesh.transform.TransformPoint(vertex));
                            Assert.That(Mathf.Abs(p.x)<.499f&&Mathf.Abs(p.y)<.499f&&Mathf.Abs(p.z)<.499f,Is.False,mesh.name+" 冰块可见时不能穿入夹臂或夹齿");
                        }
                }
                Assert.That(rig.IceVisible,Is.True);Assert.That(rig.OpeningDegrees,Is.GreaterThan(1));
                Assert.That(cube.lossyScale.x,Is.EqualTo(side).Within(1e-6),"不能靠缩小冰块规避穿模");
            }
            finally{Object.DestroyImmediate(root);}
        }
        [Test] public void ReleasingIceRestoresTheAuthoredPoseWithoutMovingAnchors()
        {
            var root=Make(out var rig,out var holder,out var cube);
            try
            {
                var nodes=root.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Arm")||t.name.StartsWith("Tooth")||t.name=="Spring"||t.name=="HoldPoint"||t.name=="Grip_R").ToArray();
                var positions=nodes.Select(t=>t.localPosition).ToArray();var rotations=nodes.Select(t=>t.localRotation).ToArray();cube.localScale=Vector3.one*.037f;
                for(int i=0;i<25;i++)rig.Advance(true,holder,1f/60);
                Assert.That(rig.IceVisible,Is.True);
                for(int i=0;i<35;i++)rig.Advance(false,holder,1f/60);
                Assert.That(rig.OpeningDegrees,Is.LessThan(.001f));
                for(int i=0;i<nodes.Length;i++)
                {
                    Assert.That(Vector3.Distance(nodes[i].localPosition,positions[i]),Is.LessThan(1e-6),nodes[i].name);
                    Assert.That(Quaternion.Angle(nodes[i].localRotation,rotations[i]),Is.LessThan(.02f),nodes[i].name);
                }
            }
            finally{Object.DestroyImmediate(root);}
        }
    }
}
