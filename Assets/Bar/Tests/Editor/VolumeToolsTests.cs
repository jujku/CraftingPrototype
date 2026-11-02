using System;
using System.Linq;
using Bar.Unity;
using Bar.Unity.Editor;
using NUnit.Framework;
using UnityEngine;
namespace Bar.Tests
{
    internal sealed class VolumeToolsTests
    {
        [Test] public void BoxIntegrationMatchesAnalyticVolumeAndHeight()
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.transform.localScale=new Vector3(.1f,.2f,.1f);var root=new GameObject("reference");VolumeProfile profile=null;
            try{profile=VolumeProfileBaker.Bake(go.GetComponent<MeshFilter>(),root.transform);Assert.That(profile.CapacityMl,Is.EqualTo(2000).Within(.01));Assert.That(profile.HeightForMl(500),Is.EqualTo(-.05).Within(1e-5));Assert.That(profile.HeightForMl(0),Is.EqualTo(-.1).Within(1e-6));Assert.That(profile.HeightForMl(3000),Is.EqualTo(.1).Within(1e-6));}
            finally{UnityEngine.Object.DestroyImmediate(profile);UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void TaperedCavityUsesNonlinearVolumeInsteadOfBoundingBox()
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);var mesh=UnityEngine.Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);var vs=mesh.vertices;
            for(int i=0;i<vs.Length;i++)vs[i]=new Vector3(vs[i].x*(vs[i].y>0?.1f:.05f),(vs[i].y+.5f)*.1f,vs[i].z*(vs[i].y>0?.1f:.05f));mesh.vertices=vs;mesh.RecalculateBounds();go.GetComponent<MeshFilter>().sharedMesh=mesh;
            var profile=VolumeProfileBaker.Bake(go.GetComponent<MeshFilter>(),go.transform);
            try{Assert.That(profile.CapacityMl,Is.EqualTo(583.333333).Within(.01));Assert.That(profile.HeightForMl(profile.CapacityMl/2),Is.GreaterThan(.06f));}
            finally{UnityEngine.Object.DestroyImmediate(profile);UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(go);}
        }
        [Test] public void OpenCavityIsRejectedWithRepairInstructions()
        {var go=GameObject.CreatePrimitive(PrimitiveType.Cube);var mesh=UnityEngine.Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);mesh.triangles=mesh.triangles.Skip(3).ToArray();go.GetComponent<MeshFilter>().sharedMesh=mesh;try{Assert.That(()=>VolumeProfileBaker.Bake(go.GetComponent<MeshFilter>(),go.transform),Throws.ArgumentException.With.Message.Contains("封闭"));}finally{UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(go);}}
        [Test] public void EmptySelectionProducesActionableFailure(){Assert.That(PrefabValidator.Validate(null).Single(),Does.Contain("ItemView"));}
        [Test] public void EverySavedGrayboxPrefabPassesTheSameValidator()
        {foreach(var guid in UnityEditor.AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/Bar/Prefabs/Resources/BarPrefabs"})){var root=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));Assert.That(PrefabValidator.Validate(root).Where(x=>x.StartsWith("失败")),Is.Empty,root.name);}}
        [Test] public void WrongMarkingAndMissingAnchorAreReported()
        {
            var source=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Bar/Prefabs/Resources/BarPrefabs/jigger.dual.prefab");var go=UnityEngine.Object.Instantiate(source);
            try{UnityEngine.Object.DestroyImmediate(go.transform.Find("Grip_L").gameObject);go.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Mark_15ml").localPosition+=Vector3.up*.01f;var failures=PrefabValidator.Validate(go).Where(x=>x.StartsWith("失败")).ToArray();Assert.That(failures.Any(x=>x.Contains("Grip_L")),Is.True);Assert.That(failures.Any(x=>x.Contains("刻度")),Is.True);}
            finally{UnityEngine.Object.DestroyImmediate(go);}
        }

        [Test] public void InvalidScaleColliderAndLiquidBindingGiveSeparateRepairItems()
        {
            var source=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Bar/Prefabs/Resources/BarPrefabs/glass.highball.prefab");var go=UnityEngine.Object.Instantiate(source);
            try{go.transform.localScale=Vector3.one*2;foreach(var collider in go.GetComponentsInChildren<Collider>())UnityEngine.Object.DestroyImmediate(collider);go.GetComponentInChildren<ContainerVisual>().LiquidVolume=null;var errors=PrefabValidator.Validate(go).Where(x=>x.StartsWith("失败")).ToArray();Assert.That(errors.Any(x=>x.Contains("缩放")),Is.True);Assert.That(errors.Any(x=>x.Contains("碰撞")),Is.True);Assert.That(errors.Any(x=>x.Contains("液体网格")),Is.True);}
            finally{UnityEngine.Object.DestroyImmediate(go);}
        }
        [Test] public void CapacityIsMeasuredFromGeometryAndCannotBeFakedByItemViewSize()
        {
            var source=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Bar/Prefabs/Resources/BarPrefabs/glass.highball.prefab");var go=UnityEngine.Object.Instantiate(source);
            try{go.GetComponentInChildren<ContainerVisual>().InnerCavity.transform.localScale=new Vector3(.7f,1,.7f);go.GetComponent<ItemView>().Size=Vector3.one*99;var errors=PrefabValidator.Validate(go).Where(x=>x.StartsWith("失败")).ToArray();Assert.That(errors.Any(x=>x.Contains("内腔")),Is.True);}
            finally{UnityEngine.Object.DestroyImmediate(go);}
        }
        [Test] public void SameCapacityButChangedTaperInvalidatesStoredHeightCurve()
        {
            var source=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Bar/Prefabs/Resources/BarPrefabs/glass.highball.prefab");var go=UnityEngine.Object.Instantiate(source);var binding=go.GetComponentInChildren<ContainerVisual>();var mesh=UnityEngine.Object.Instantiate(binding.InnerCavity.sharedMesh);binding.InnerCavity.sharedMesh=mesh;
            try{var vertices=mesh.vertices;float middle=mesh.bounds.center.y;float bottom=(-1.3f+Mathf.Sqrt(6.93f))/2;for(int i=0;i<vertices.Length;i++){float scale=vertices[i].y>middle?1.3f:bottom;vertices[i]=new Vector3(vertices[i].x*scale,vertices[i].y,vertices[i].z*scale);}mesh.vertices=vertices;mesh.RecalculateBounds();var errors=PrefabValidator.Validate(go).Where(x=>x.StartsWith("失败")).ToArray();Assert.That(errors.Any(x=>x.Contains("液面曲线")),Is.True);}
            finally{UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(mesh);}
        }
        [Test] public void HorizontalSurfaceMatchesCavitySectionAndDoesNotDeformOutsideWalls()
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.transform.localScale=new Vector3(.1f,.2f,.1f);var surface=go.AddComponent<LiquidSurface>();var material=new Material(Shader.Find("Bar/LiquidFill"));
            try{surface.Draw(go.GetComponent<MeshFilter>(),0,material,true);var mesh=go.transform.Find("Liquid surface (baked level)").GetComponent<MeshFilter>().sharedMesh;Assert.That(mesh.triangles.Length,Is.EqualTo(6));foreach(var v in mesh.vertices)Assert.That(go.transform.TransformPoint(v).y,Is.EqualTo(0).Within(1e-6));double area=0;var vs=mesh.vertices;var ts=mesh.triangles;for(int i=0;i<ts.Length;i+=3){var x=go.transform.TransformPoint(vs[ts[i]]);var y=go.transform.TransformPoint(vs[ts[i+1]]);var z=go.transform.TransformPoint(vs[ts[i+2]]);area+=Vector3.Cross(y-x,z-x).magnitude/2;}Assert.That(area,Is.EqualTo(.01).Within(1e-6));}
            finally{UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(material);}
        }

        [Test] public void RoundGrayboxInnerCavityFitsInsideItsCircularSilhouette()
        {var prefab=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Bar/Prefabs/Resources/BarPrefabs/glass.highball.prefab");var binding=prefab.GetComponentInChildren<ContainerVisual>();foreach(var v in binding.InnerCavity.sharedMesh.vertices)Assert.That(new Vector2(v.x,v.z).magnitude,Is.LessThan(.035f));}

        [Test] public void VisibleJiggerMarkingsUseTheirBakedHeight()
        {var prefab=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Bar/Prefabs/Resources/BarPrefabs/jigger.dual.prefab");foreach(var binding in prefab.GetComponentsInChildren<ContainerVisual>())foreach(var mark in binding.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Mark_"))){double ml=double.Parse(mark.name.Substring(5).Replace("ml",""),System.Globalization.CultureInfo.InvariantCulture);Assert.That(mark.localPosition.y,Is.EqualTo(binding.Profile.HeightForMl(ml)).Within(1e-6));var ring=mark.GetComponentInChildren<LineRenderer>();Assert.That(ring,Is.Not.Null);Assert.That(ring.loop,Is.True);}}
    }
}
