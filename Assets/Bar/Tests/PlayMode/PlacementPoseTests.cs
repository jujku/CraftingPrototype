using System.Collections;
using System.Linq;
using Bar.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
namespace Bar.Tests
{
    internal sealed class PlacementPoseTests
    {
        [UnityTest] public IEnumerator IdleSceneEditsSurviveAndBecomeTheNextPlacementHeading()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/Bar.unity");yield return null;
            var s=Object.FindFirstObjectByType<BarSession>();
            var counter=s.Views.Single(v=>v.DefinitionId=="fixture.counter");
            var counterPosition=counter.transform.position;var counterRotation=counter.transform.rotation;
            var playerPosition=s.Player.transform.position;
            Assert.That(playerPosition.z,Is.GreaterThan(counterPosition.z),"调酒师应在移动后的柜门一侧");
            Assert.That(Vector3.Dot(s.Player.ViewCamera.transform.forward,(counterPosition+Vector3.up*.95f-s.Player.ViewCamera.transform.position).normalized),Is.GreaterThan(.7f),"启动不能把已保存的 180 度视角转回零度");
            float distance=Vector3.ProjectOnPlane(playerPosition-counterPosition,Vector3.up).magnitude;
            s.Player.Move(Vector2.up,.1f);
            Assert.That(Vector3.ProjectOnPlane(s.Player.transform.position-counterPosition,Vector3.up).magnitude,Is.LessThan(distance),"W 应向工作台移动");
            s.Player.transform.position=playerPosition;
            var editedCounterPosition=counterPosition+Vector3.right*.01f;var editedCounterRotation=counterRotation*Quaternion.Euler(0,17,0);
            counter.transform.SetPositionAndRotation(editedCounterPosition,editedCounterRotation);
            yield return new WaitForSecondsRealtime(.45f);
            Assert.That(Vector3.Distance(counter.transform.position,editedCounterPosition),Is.LessThan(1e-5f));
            Assert.That(Quaternion.Angle(counter.transform.rotation,editedCounterRotation),Is.LessThan(.01f),"静置设施不应每帧被转回启动角度");
            counter.transform.SetPositionAndRotation(counterPosition,counterRotation);
            var bottle=s.Views.First(v=>v.DefinitionId=="bottle.soda");
            var position=bottle.transform.position+Vector3.right*.005f;var rotation=Quaternion.Euler(0,43,0);
            bottle.transform.SetPositionAndRotation(position,rotation);yield return new WaitForSecondsRealtime(.45f);
            Assert.That(Vector3.Distance(bottle.transform.position,position),Is.LessThan(1e-5f));
            Assert.That(Quaternion.Angle(bottle.transform.rotation,rotation),Is.LessThan(.01f));
            s.Player.AimAt(bottle.GetComponent<Collider>().bounds.center);Physics.SyncTransforms();s.Targeting();
            Assert.That(s.Target,Is.SameAs(bottle));s.RightButton(s.EventTime(Time.realtimeSinceStartupAsDouble));
            yield return new WaitForSecondsRealtime(.25f);Assert.That(s.Hands.SideOf(bottle.Item).HasValue,Is.True);
            yield return CapSteps.PutDown(s,bottle);yield return new WaitForSecondsRealtime(.8f);
            Assert.That(Quaternion.Angle(bottle.transform.rotation,rotation),Is.LessThan(.05f),"放下应回到用户调整过的朝向");
            position=bottle.transform.position+Vector3.right*.003f;rotation=Quaternion.Euler(0,107,0);
            bottle.transform.SetPositionAndRotation(position,rotation);yield return new WaitForSecondsRealtime(.5f);
            Assert.That(Vector3.Distance(bottle.transform.position,position),Is.LessThan(1e-5f));
            Assert.That(Quaternion.Angle(bottle.transform.rotation,rotation),Is.LessThan(.01f),"放下动画结束后应释放根节点姿态");
            s.Cancel(s.EventTime(Time.realtimeSinceStartupAsDouble));
        }
    }
}
