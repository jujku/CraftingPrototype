using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Bar.Unity.Editor
{
    /// <summary>离线环境装配入口；只在有标记的验证副本执行，保护既有物品和相机。</summary>
    public static partial class GinzaLoungeBuilder
    {
        public const string ScenePath="Assets/Bar/Scenes/BarPresentation.unity";
        public const string Revision="GinzaSpeakeasy";
        public const string AssetRoot="Assets/Bar/Environment/Ginza";
        private static string Report=>Path.GetFullPath(Path.Combine(Application.dataPath,"../GinzaReports"));
        private static void RequireCache(){if(!File.Exists(Path.Combine(Application.dataPath,"../.u1-validation-copy")))throw new InvalidOperationException("Ginza build requires the isolated marked project");Directory.CreateDirectory(Report);}
        [Serializable] public sealed class PoseRow{public string path,definition;public Vector3 position,scale;public Quaternion rotation;}
        [Serializable] public sealed class BoundRow{public string name;public Vector3 center,size;}
        [Serializable] public sealed class Inventory{public PoseRow[] transforms;public BoundRow[] bounds;public Vector3 counterPosition,counterRight,customer,player,camera,surface;public float counterWidth,counterDepth,fieldOfView;}
        private static string Hierarchy(Transform t)=>t.parent?Hierarchy(t.parent)+"/"+t.name:t.name;
        private static PoseRow[] Poses()=>UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(t=>t.gameObject.scene.IsValid()&&!t.root.name.StartsWith("Ginza_")).OrderBy(Hierarchy).Select(t=>new PoseRow{path=Hierarchy(t),position=t.position,rotation=t.rotation,scale=t.lossyScale,definition=t.GetComponent<ItemView>()?t.GetComponent<ItemView>().DefinitionId:""}).ToArray();
        public static void Probe()
        {
            RequireCache();EditorSceneManager.OpenScene(ScenePath);var session=UnityEngine.Object.FindFirstObjectByType<BarSession>();var counter=UnityEngine.Object.FindObjectsByType<ItemView>(FindObjectsSortMode.None).Single(v=>v.DefinitionId=="fixture.counter");
            var customer=Vector3.ProjectOnPlane(counter.transform.position-session.Player.ViewCamera.transform.position,Vector3.up).normalized;
            var data=new Inventory{transforms=Poses(),counterPosition=counter.transform.position,counterRight=Vector3.Cross(Vector3.up,customer),customer=customer,player=session.Player.transform.position,camera=session.Player.ViewCamera.transform.position,surface=counter.Anchor("Surface").position,counterWidth=counter.Size.x,counterDepth=counter.Size.z,fieldOfView=session.Player.ViewCamera.fieldOfView,bounds=UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r=>r.name=="Floor"||r.name=="Back wall"||r.GetComponentInParent<ItemView>()).Select(r=>new BoundRow{name=Hierarchy(r.transform),center=r.bounds.center,size=r.bounds.size}).ToArray()};
            File.WriteAllText(Path.Combine(Report,"inventory-before.json"),JsonUtility.ToJson(data,true));
        }
    }
}
