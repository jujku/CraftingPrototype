using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bar.Domain;
using Bar.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
namespace Bar.Tests
{
    /// <summary>榨汁器零倾角对口、手持独立深度与还原、人物真实环境碰撞和扩展范围。</summary>
    public sealed class InteractionEnvironmentTests
    {
        private BarSession session;
        private static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../InteractionReports"));
        private ItemView View(string id)=>session.Views.First(v=>v.DefinitionId==id);
        private IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;
            session=Object.FindFirstObjectByType<BarSession>();session.enabled=false;
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter")b.enabled=false;
            Directory.CreateDirectory(Output);
        }
        private static Texture2D Capture(Camera camera,string name)
        {
            var image=PourCaptureTests.Capture(camera,"interaction-"+name);File.Copy(Path.Combine(PourCaptureTests.Output,"interaction-"+name+".png"),Path.Combine(Output,name+".png"),true);return image;
        }
        [UnityTest]public IEnumerator PressOutletStaysOverEveryReceiverAcrossFlowAndPressureChanges()
        {
            yield return Load();var source=View("tool.squeezer");var rig=source.GetComponent<SqueezerViewRig>();
            var camera=session.Player.ViewCamera;var sourceRotation=source.transform.rotation;
            var rows=new List<string>{"target,held,frame,flow,pressure,horizontalErrorMetres"};
            foreach(string id in new[]{"jigger.dual","glass.highball","shaker.cobbler"})foreach(bool held in new[]{false,true})
            {
                var target=View(id);var targetHome=target.transform.position;var targetRotation=target.transform.rotation;
                var sourceHome=camera.transform.TransformPoint(new Vector3(.2f,-.27f,.55f));
                Container container=target.Item is Jigger j?j.ActiveCup:target.Item is CobblerShaker s?s.Body:(Container)target.Item;
                source.transform.SetPositionAndRotation(sourceHome,sourceRotation);var pose=new PourPose();pose.Begin(source,target,camera.transform,held);
                for(int frame=0;frame<30;frame++)
                {
                    source.transform.SetPositionAndRotation(sourceHome,sourceRotation);target.transform.SetPositionAndRotation(targetHome,targetRotation);
                    float pressure=(frame%4)/3f,flow=frame%3==0?0:20;rig.SetPressAmount(pressure);
                    var input=pose.Step(frame==0?.25f:1f/60,0,flow,container);
                    var offset=source.PourPoint-input.MouthCenter;float error=new Vector2(offset.x,offset.z).magnitude;
                    Assert.That(error,Is.LessThan(.0001f),id+" pressing tools cannot use upright bottle side offset");
                    Assert.That(source.PourPoint.y-input.MouthCenter.y,Is.EqualTo(PourSettings.Load().mouthClearance).Within(.0001f));
                    rows.Add(id+","+held+","+frame+","+flow+","+pressure+","+error);
                    rig.SetPressAmount(1-pressure);pose.Step(0,0,flow,container);
                    offset=source.PourPoint-input.MouthCenter;Assert.That(new Vector2(offset.x,offset.z).magnitude,Is.LessThan(.0001f));
                    if(frame==15&&id=="jigger.dual"&&held){yield return null;pose.Step(0,0,flow,container);Object.Destroy(Capture(camera,"squeezer-held"));}
                }
                target.transform.SetPositionAndRotation(targetHome,targetRotation);
            }
            File.WriteAllLines(Path.Combine(Output,"squeezer-frames.csv"),rows);
        }
        [UnityTest]public IEnumerator HeldObjectsRemainVisibleThroughWorldDepthAndRestoreTheirLayers()
        {
            yield return Load();var source=View("bottle.gin");var layers=PlayerEnvironmentSettings.Load();var foreground=session.GetComponent<HeldItemRendering>();
            Assert.That(foreground,Is.Not.Null);var original=source.gameObject.layer;((Bottle)source.Item).SetOpen(true);
            Assert.That(session.Hands.TryPickUp(source.Item),Is.True);var camera=session.Player.ViewCamera;camera.fieldOfView=45;camera.aspect=640f/480;
            source.transform.rotation=Quaternion.identity;source.transform.position=camera.transform.TransformPoint(new Vector3(0,-source.Size.y*.45f,.55f));
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="World depth occluder fixture";wall.transform.SetPositionAndRotation(camera.transform.TransformPoint(new Vector3(0,0,.23f)),camera.transform.rotation);wall.transform.localScale=new Vector3(1,1,.02f);
            var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));material.SetColor("_BaseColor",new Color(.75f,.02f,.02f));wall.GetComponent<MeshRenderer>().sharedMaterial=material;
            foreground.Refresh();
            Assert.That(source.gameObject.layer,Is.EqualTo(layers.Held));Assert.That(source.GetComponent<BottleLiquidView>().Cavity.gameObject.layer,Is.EqualTo(layers.Liquid));
            Assert.That(foreground.OverlayCamera.GetUniversalAdditionalCameraData().clearDepth,Is.True);
            Assert.That(camera.cullingMask&layers.Mask,Is.Zero);Assert.That(Physics.GetIgnoreCollision(session.Player.Body,source.GetComponent<Collider>()),Is.True);
            var with=Capture(camera,"held-over-world");source.gameObject.SetActive(false);var without=Capture(camera,"world-only");source.gameObject.SetActive(true);
            int changed=0,pink=0;for(int y=0;y<with.height;y++)for(int x=0;x<with.width;x++)
            {var a=with.GetPixel(x,y);var b=without.GetPixel(x,y);if(Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b)>.15f)changed++;if(a.r>.9f&&a.b>.9f&&a.g<.1f)pink++;}
            Assert.That(changed,Is.GreaterThan(300),"the world wall must not erase the held item");Assert.That(pink,Is.LessThan(10));
            var side=session.Hands.SideOf(source.Item).Value;Assert.That(session.Hands.TryPlace(side,(BarCounter)View("fixture.counter").Item),Is.True);foreground.Refresh();
            Assert.That(source.gameObject.layer,Is.EqualTo(original));Assert.That(source.GetComponent<BottleLiquidView>().Cavity.gameObject.layer,Is.EqualTo(31));
            Assert.That(Physics.GetIgnoreCollision(session.Player.Body,source.GetComponent<Collider>()),Is.False);
            File.WriteAllText(Path.Combine(Output,"foreground-pixels.txt"),"changed="+changed+" pink="+pink+" layersRestored=true");
            Object.Destroy(with);Object.Destroy(without);Object.Destroy(wall);Object.Destroy(material);
        }
        private void Teleport(Vector3 position)
        {var body=session.Player.Body;body.enabled=false;session.Player.transform.position=position;body.enabled=true;session.Player.AimAt(session.Player.ViewCamera.transform.position+Vector3.forward*10);Physics.SyncTransforms();}
        [UnityTest]public IEnumerator CapsuleBlocksCounterWallsAndNewPartitionsWithoutCoordinateLimits()
        {
            yield return Load();var player=session.Player;var body=player.Body;var rows=new List<string>();
            var counter=View("fixture.counter").GetComponent<Collider>().bounds;
            Teleport(new Vector3(counter.center.x,.01f,counter.min.z-.7f));
            for(int i=0;i<180;i++)player.Move(Vector2.up,1f/60);
            Assert.That(player.transform.position.z,Is.LessThan(counter.min.z-body.radius+.04f),"cabinet must block the capsule");rows.Add("counter="+player.transform.position);
            var wall=GameObject.Find("Ginza_Lounge/ArchitecturalEnvelope/Rear wainscot wall");var wallCollider=wall.GetComponent<Collider>();Assert.That(wallCollider,Is.Not.Null,"visible room mesh should have runtime collision");var bounds=wallCollider.bounds;
            float sideX=2.25f;Teleport(new Vector3(sideX,.01f,bounds.min.z-.7f));for(int i=0;i<180;i++)player.Move(Vector2.up,1f/60);
            Assert.That(player.transform.position.z,Is.LessThan(bounds.min.z-body.radius+.04f));rows.Add("wall="+player.transform.position);
            var partition=GameObject.CreatePrimitive(PrimitiveType.Cube);partition.name="Expansion partition";partition.transform.position=new Vector3(sideX,1,.15f);partition.transform.localScale=new Vector3(.55f,2,.08f);Object.Destroy(partition.GetComponent<Collider>());yield return null;
            Assert.That(SceneCollisionSetup.Refresh(player),Is.GreaterThan(0));Assert.That(partition.GetComponent<MeshCollider>(),Is.Not.Null);
            Teleport(new Vector3(sideX,.01f,-.7f));for(int i=0;i<180;i++)player.Move(Vector2.up,1f/60);
            Assert.That(player.transform.position.z,Is.LessThan(partition.GetComponent<Collider>().bounds.min.z-body.radius+.04f));rows.Add("newPartition="+player.transform.position);
            Object.Destroy(partition);yield return null;Teleport(new Vector3(sideX,.01f,-.7f));for(int i=0;i<60;i++)player.Move(Vector2.down,1f/60);
            Assert.That(player.transform.position.x,Is.GreaterThan(2));Assert.That(player.transform.position.z,Is.LessThan(-1.2f),"walk beyond former X/Z bounds when the floor is open");
            Assert.That(player.transform.position.y,Is.GreaterThan(-.1f),"floor must support the capsule");rows.Add("outsideFormerBounds="+player.transform.position);
            File.WriteAllLines(Path.Combine(Output,"collision-checks.txt"),rows);
        }
    }
}
