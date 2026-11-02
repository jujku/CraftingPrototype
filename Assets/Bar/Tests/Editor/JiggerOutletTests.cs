using System.Collections.Generic;
using System.IO;
using Bar.Data;
using Bar.Domain;
using Bar.Unity;
using CraftingResearch.CoreV2;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace Bar.Tests
{
    /// <summary>仅检查量酒器两端的细流首环与重力收束，不启动场景或核心步进。</summary>
    internal sealed class JiggerOutletTests
    {
        [Test]public void BothCupsHaveNarrowGravityDrivenOutletsWithoutFlaredFirstRings()
        {
            var go=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Bar/Prefabs/Resources/BarPrefabs/jigger.dual.prefab"));
            try
            {
                var model=JsonUtility.FromJson<ModelData>(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Bar/Core/Data/model-v2.json").text);
                var catalog=ItemCatalog.Load(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Bar/Data/Json/items.json").text,model);
                var factory=new ItemFactory(catalog,id=>20,()=>.5);var view=go.GetComponent<ItemView>();
                view.Bind(factory.Create("jigger.dual","test:jigger-outlet"),model.icePortionGrams,model.iceDensity);
                var jigger=(Jigger)view.Item;var p=PourSettings.Load();var rows=new List<string>{"cup,flow,angle,oldMouthRadius,newOutletRadius,largestRadius,sidewaysTravel"};
                for(int cup=0;cup<2;cup++)
                {
                    float mouth=PourPose.Binding(view,jigger.ActiveCup).Shape.RadiusAt(PourPose.Binding(view,jigger.ActiveCup).Shape.Top);
                    Assert.That(mouth,Is.GreaterThan(.01f),"真实口沿不能被缩小来掩盖问题");
                    Assert.That(PourPose.OutletRadius(view),Is.EqualTo(.003f).Within(1e-6));
                    foreach(float q in new[]{3f,20f,50f,75f})foreach(float angle in new[]{110f,125f,160f})
                    {
                        var axis=Quaternion.AngleAxis(angle,Vector3.forward)*Vector3.up;var velocity=PourPose.ExitVelocity(view,axis,q);
                        Assert.That(velocity.y,Is.LessThan(0));Assert.That(Vector3.ProjectOnPlane(velocity,Vector3.up).magnitude,Is.LessThanOrEqualTo(.020001f));
                        Assert.That(Mathf.PI*.003f*.003f*velocity.magnitude*1e6f,Is.EqualTo(q).Within(.0001f),"表现流量仍与确认量一致");
                        Assert.That(PourTrajectory.PlaneTime(Vector3.up*.06f,velocity,0,p.gravity,out float duration),Is.True);
                        float maximum=0;
                        for(int i=0;i<=32;i++)maximum=Mathf.Max(maximum,PourTrajectory.Radius(q,PourTrajectory.Velocity(velocity,duration*i/32,p.gravity).magnitude,p.minimumStreamRadius));
                        Assert.That(maximum,Is.LessThanOrEqualTo(.003001f),"水柱首环不能撑到整个杯口宽度");
                        var end=PourTrajectory.Point(Vector3.up*.06f,velocity,duration,p.gravity);float lateral=new Vector2(end.x,end.z).magnitude;
                        Assert.That(lateral,Is.LessThan(.003f),"收窄后也不能朝外喷射");
                        rows.Add(jigger.ActiveCup.Definition.Id+","+q+","+angle+","+mouth+","+PourPose.OutletRadius(view)+","+maximum+","+lateral);
                    }
                    jigger.Flip();
                }
                string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../JiggerOutletReports"));Directory.CreateDirectory(output);File.WriteAllLines(Path.Combine(output,"outlet-cases.csv"),rows);
            }
            finally{Object.DestroyImmediate(go);}
        }
    }
}
