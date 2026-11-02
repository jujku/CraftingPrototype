using System.Linq;
using Bar.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Bar.Tests
{
    /// <summary>从外部观察方向检查柜体真实遮挡，防止接缝成为透空，并验证龙头朝向。</summary>
    internal sealed class CounterAssemblyTests
    {
        private static float NearestHit(MeshFilter[] parts,Ray ray)
        {
            float nearest=float.PositiveInfinity;
            foreach(var part in parts)
            {
                var renderer=part.GetComponent<Renderer>();
                if(!renderer||!renderer.enabled||!renderer.bounds.IntersectRay(ray))continue;
                var vertices=part.sharedMesh.vertices.Select(v=>part.transform.TransformPoint(v)).ToArray();var triangles=part.sharedMesh.triangles;
                for(int i=0;i<triangles.Length;i+=3)
                {
                    var a=vertices[triangles[i]];var e1=vertices[triangles[i+1]]-a;var e2=vertices[triangles[i+2]]-a;
                    var cross=Vector3.Cross(ray.direction,e2);float determinant=Vector3.Dot(e1,cross);
                    if(Mathf.Abs(determinant)<1e-8f)continue;
                    var offset=ray.origin-a;float u=Vector3.Dot(offset,cross)/determinant;if(u<0||u>1)continue;
                    var q=Vector3.Cross(offset,e1);float v=Vector3.Dot(ray.direction,q)/determinant;if(v<0||u+v>1)continue;
                    float distance=Vector3.Dot(e2,q)/determinant;if(distance>=0)nearest=Mathf.Min(nearest,distance);
                }
            }
            return nearest;
        }
        [Test] public void CounterTopCornersAndDoorJointsDoNotLeaveThroughGaps()
        {
            var go=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Bar/Prefabs/Resources/BarPrefabs/fixture.counter.prefab"));
            try
            {
                var parts=go.GetComponentsInChildren<MeshFilter>();
                var top=go.transform.Find("Visual/WalnutTop").GetComponent<Renderer>().bounds;
                float seamY=top.min.y-.012f;
                foreach(var point in new[]{new Vector3(0,seamY,-.6f),new Vector3(0,.07f,-.6f),new Vector3(-.39f,.5f,-.6f),new Vector3(.39f,.5f,-.6f),new Vector3(-1.164f,.5f,-.6f),new Vector3(1.164f,.5f,-.6f)})
                    Assert.That(NearestHit(parts,new Ray(point,Vector3.forward)),Is.LessThan(.3f),"柜门侧的接缝应被实体木框封住 "+point);
                Assert.That(NearestHit(parts,new Ray(new Vector3(0,seamY,.6f),Vector3.back)),Is.LessThan(.4f),"顾客侧背板上沿不能透空");
                foreach(float sign in new[]{-1f,1f})
                    Assert.That(NearestHit(parts,new Ray(new Vector3(sign*1.4f,seamY,top.center.z),Vector3.left*sign)),Is.LessThan(.3f),"左右侧板应接到台面下沿");
            }
            finally{Object.DestroyImmediate(go);}
        }
        [Test] public void FaucetOutletFacesTheBartenderInBothScenes()
        {
            try
            {
                foreach(string path in new[]{"Assets/Bar/Scenes/Bar.unity","Assets/Bar/Scenes/BarPresentation.unity"})
                {
                    EditorSceneManager.OpenScene(path);var views=Object.FindObjectsByType<ItemView>(FindObjectsSortMode.None);
                    var sink=views.Single(v=>v.DefinitionId=="fixture.sink");var counter=views.Single(v=>v.DefinitionId=="fixture.counter");var player=Object.FindFirstObjectByType<PlayerController>();
                    var faucet=sink.transform.Find("Visual/Faucet").GetComponent<MeshFilter>();
                    var points=faucet.sharedMesh.vertices.Select(v=>faucet.transform.TransformPoint(v)).ToArray();float height=points.Max(v=>v.y);
                    var tip=points.Where(v=>v.y>height-.06f).ToArray();var upper=tip.Aggregate(Vector3.zero,(a,v)=>a+v)/tip.Length;
                    var basis=sink.transform.Find("Visual/FaucetBase").GetComponent<Renderer>().bounds.center;
                    var outlet=Vector3.ProjectOnPlane(upper-basis,Vector3.up).normalized;
                    var bartender=Vector3.ProjectOnPlane(player.transform.position-counter.transform.position,Vector3.up).normalized;
                    Assert.That(Vector3.Dot(outlet,bartender),Is.GreaterThan(.95f),path+" 龙头应从顾客侧伸向调酒师");
                }
            }
            finally{EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
        }
    }
}
