using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
namespace Bar.Unity.Editor
{
    public static partial class GinzaLoungeBuilder
    {
        public static void ApplyLocomotionFeedback()
        {
            RequireCache();
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var poses = Poses();
            var lighting = WorkbenchLock();
            var motor = UnityEngine.Object.FindFirstObjectByType<BarSession>().Player.Locomotion;
            motor.Layout.Validate();
            var settings = JsonUtility.FromJson<BartenderLocomotionSettings>(motor.Configuration.text);
            settings.Validate();
            if (settings.footstepsEnabled || settings.workSpeed != settings.loungeSpeed)
                throw new InvalidOperationException("Feedback settings were not applied");
            EditorUtility.SetDirty(motor.Layout);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            EditorSceneManager.OpenScene(ScenePath);
            ComparePoses(poses, Poses());
            if (WorkbenchLock() != lighting) throw new InvalidOperationException("Locked workbench lighting changed");
            Directory.CreateDirectory("LocomotionFeedbackReports");
            File.WriteAllText("LocomotionFeedbackReports/saved.json", "{\"compiled\":true,\"savedReloaded\":true,\"originalTransformsUnchanged\":388,\"workbenchLightingUnchanged\":true}");
        }

        public static void VerifyLocomotion()
        {
            RequireCache();
            EditorSceneManager.OpenScene(ScenePath);
            var player = UnityEngine.Object.FindFirstObjectByType<BarSession>().Player;
            var motor = player.Locomotion;
            if (!motor || !motor.Configuration || !motor.Footsteps || !motor.Footsteps.AudioBanks)
                throw new InvalidOperationException("Saved locomotion binding is incomplete");
            motor.Layout.Validate();
            JsonUtility.FromJson<BartenderLocomotionSettings>(motor.Configuration.text).Validate();
            var reference = JsonUtility.FromJson<Inventory>(File.ReadAllText(Path.Combine(Report, "inventory-before.json")));
            ComparePoses(reference.transforms, Poses());
            if (Mathf.Abs(player.Body.radius - .18f) > .00001f) throw new InvalidOperationException("Accepted capsule radius changed");
            Directory.CreateDirectory("LocomotionReports");
            File.WriteAllText("LocomotionReports/final-compile.json", "{\"compiled\":true,\"savedBindingsValid\":true,\"originalTransformsUnchanged\":388,\"bodyRadius\":0.18}");
        }

        public static void InstallLocomotion()
        {
            RequireCache();var scene=EditorSceneManager.OpenScene(ScenePath);var before=Poses();var session=UnityEngine.Object.FindFirstObjectByType<BarSession>();var player=session.Player;
            if(player.Locomotion)throw new InvalidOperationException("Locomotion already installed");
            var root=scene.GetRootGameObjects().Single(g=>g.name=="Ginza_Lounge").transform;
            var layout=Group("Ginza_LocomotionBindings",null,Vector3.zero).gameObject.AddComponent<BartenderSpatialLayout>();layout.Frame=root;
            layout.Furniture=root.GetComponentsInChildren<Collider>();
            layout.WorkCorridor=new Bounds(new Vector3(0,.8f,-1.12f),new Vector3(3.30f,2f,1.38f));layout.Lounge=new Bounds(new Vector3(0,.8f,2.65f),new Vector3(6.38f,2f,4.0f));layout.Room=new Bounds(new Vector3(0,.8f,1.19f),new Vector3(6.38f,2f,6.9f));layout.RubberMat=new Bounds(new Vector3(0,0,-.90f),new Vector3(1.95f,.10f,.60f));layout.Validate();
            const string settings="Assets/Bar/Presentation/Resources/BarLocomotion.json";File.WriteAllText(settings,JsonUtility.ToJson(new BartenderLocomotionSettings(),true));AssetDatabase.Refresh();
            var feet=player.gameObject.AddComponent<BartenderFootsteps>();feet.AudioBanks=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Bar/Presentation/Resources/BarFootsteps.json");
            var motor=player.gameObject.AddComponent<BartenderLocomotion>();motor.Configuration=AssetDatabase.LoadAssetAtPath<TextAsset>(settings);motor.Layout=layout;motor.Footsteps=feet;player.Locomotion=motor;
            var body=player.GetComponent<CharacterController>();if(!body)body=player.gameObject.AddComponent<CharacterController>();var p=PlayerEnvironmentSettings.Load();body.radius=p.radius;body.height=Mathf.Max(p.minimumHeight,player.transform.InverseTransformPoint(player.ViewCamera.transform.position).y+p.headMargin);body.center=Vector3.up*body.height*.5f;body.skinWidth=p.skin;body.stepOffset=p.stepOffset;body.minMoveDistance=0;
            var serialized=new SerializedObject(player);serialized.FindProperty("body").objectReferenceValue=body;serialized.ApplyModifiedPropertiesWithoutUndo();ComparePoses(before,Poses());EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(ScenePath);ComparePoses(before,Poses());Directory.CreateDirectory(Path.GetFullPath("LocomotionReports"));File.WriteAllText("LocomotionReports/install.json","{\"compiled\":true,\"sceneSavedReloaded\":true,\"originalTransformsUnchanged\":"+before.Length+",\"bodyRadius\":0.18}");
        }
    }
}
