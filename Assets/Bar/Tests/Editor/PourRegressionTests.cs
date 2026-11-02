using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.Serialization.Json;
using Bar.Data;
using Bar.Domain;
using Bar.Unity;
using CraftingResearch.CoreV2;
using NUnit.Framework;
using UnityEngine;
using Object=UnityEngine.Object;
namespace Bar.Tests
{
    /// <summary>P 段线性规则的逐字节回归、真实弹道和几何数据容量报告。</summary>
    public sealed class PourRegressionTests
    {
        private static string CompleteJson<T>(T value)
        {using(var buffer=new MemoryStream()){new DataContractJsonSerializer(typeof(T)).WriteObject(buffer,value);return Encoding.UTF8.GetString(buffer.ToArray());}}
        [Test]public void LinearModeOperationReplayIsByteIdenticalToFrozenFormula()
        {
            string json=File.ReadAllText(Path.Combine(Application.dataPath,"Bar/Data/Json/controls.json"));
            var controls=ItemCatalog.LoadControls(json.Replace("\"geometric\"","\"linear\""));var model=new TiltFlowModel(controls);
            var data=JsonUtility.FromJson<ModelData>(File.ReadAllText(Path.Combine(Application.dataPath,"Bar/Core/Data/model-v2.json")));
            var before=new World(data);var after=new World(data);var originalLog=new StringBuilder();var modernLog=new StringBuilder();var oldStates=new StringBuilder();var newStates=new StringBuilder();
            foreach(var pair in new[]{("bottle.gin","spirit",750d),("bottle.syrup","syrup",500d),("bottle.soda","soda",325d),("bottle.tonic","tonic",325d)})
            {
                double remaining=pair.Item3;
                foreach(double angle in new[]{0,20,35,45,60,75,90,100,120,150,160,110,95,80,60,40,10,0})
                {
                    double start=controls.EmptyBottleStartDegrees+(controls.FullBottleStartDegrees-controls.EmptyBottleStartDegrees)*remaining/pair.Item3;
                    double legacy=remaining==0||angle<=start?0:controls.MaxFlowMlPerSecond*Math.Pow(Math.Min(1,(angle-start)/(controls.FullFlowDegrees-start)),controls.CurveExponent);
                    double actual=model.Flow(angle,remaining,pair.Item3,null,pair.Item1);
                    Assert.That(BitConverter.DoubleToInt64Bits(actual),Is.EqualTo(BitConverter.DoubleToInt64Bits(legacy)));
                    double ml=Math.Min(remaining,legacy*.1);remaining-=ml;
                    var oldOp=new Operation{type="PourSource",to="shaker",ingredient=pair.Item2,ml=ml};
                    var newOp=new Operation{type="PourSource",to="shaker",ingredient=pair.Item2,ml=actual*.1};
                    originalLog.AppendLine(CompleteJson(oldOp));modernLog.AppendLine(CompleteJson(newOp));
                    before.Apply(oldOp);after.Apply(newOp);before.Advance(.1);after.Advance(.1);
                    string oldState=CompleteJson(before.GetVessel("shaker").Snapshot()),newState=CompleteJson(after.GetVessel("shaker").Snapshot());
                    Assert.That(newState,Is.EqualTo(oldState));oldStates.AppendLine(oldState);newStates.AppendLine(newState);
                }
            }
            Assert.That(modernLog.ToString(),Is.EqualTo(originalLog.ToString()));
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../PourReports"));Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory,"linear-operation-baseline.jsonl"),originalLog.ToString());File.WriteAllText(Path.Combine(directory,"linear-operation-current.jsonl"),modernLog.ToString());
            File.WriteAllText(Path.Combine(directory,"linear-csharp-baseline.jsonl"),oldStates.ToString());File.WriteAllText(Path.Combine(directory,"linear-csharp-current.jsonl"),newStates.ToString());
            using(var sha=SHA256.Create())File.WriteAllText(Path.Combine(directory,"linear-replay-sha256.txt"),BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(modernLog.ToString()))).Replace("-","").ToLowerInvariant());
        }
        [Test]public void BallisticStreamCannotInventAHorizontalAimOrReachAPlaneAboveItsApex()
        {
            var p=PourSettings.Load();var start=new Vector3(2,1,3);var velocity=new Vector3(.2f,.1f,.07f);
            Assert.That(PourTrajectory.PlaneTime(start,velocity,.6f,p.gravity,out float time),Is.True);
            var impact=PourTrajectory.Point(start,velocity,time,p.gravity);
            Assert.That(impact.y,Is.EqualTo(.6f).Within(.000001f));Assert.That(impact.x,Is.EqualTo(start.x+velocity.x*time).Within(.000001f));
            Assert.That(PourTrajectory.PlaneTime(start,velocity,1.1f,p.gravity,out _),Is.False);
            float radius=PourTrajectory.Radius(30,.3f,0);Assert.That(Mathf.PI*radius*radius*.3f*1e6f,Is.EqualTo(30).Within(.00001f));
        }
        [Test]public void FourBakedCavitiesHaveClosedMeshesAndEnoughCapacity()
        {
            foreach(string id in new[]{"bottle.gin","bottle.syrup","bottle.soda","bottle.tonic"})
            {
                var d=BottleGeometryData.Load(id);Assert.That(d,Is.Not.Null);var mesh=d.CreateMesh(PourSettings.Load().bottleSides);
                var go=new GameObject("P offline check");var filter=go.AddComponent<MeshFilter>();filter.sharedMesh=mesh;
                var profile=Bar.Unity.Editor.VolumeProfileBaker.Bake(filter,go.transform,17);
                Assert.That(profile.CapacityMl/d.volumesMl.Last(),Is.InRange(.998,1.002));
                Assert.That(profile.CapacityMl,Is.GreaterThan(d.capacityMl),id+" must physically contain the authored capacity, including the user-approved Gin repair.");
                Object.DestroyImmediate(profile);Object.DestroyImmediate(go);Object.DestroyImmediate(mesh);
            }
        }
    }
}
