using System;
using System.Collections;
using System.Linq;
using Bar.Domain;
using Bar.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Bar.Tests
{
    internal sealed class ToolIceAnchorTests
    {
        [UnityTest] public IEnumerator TongsIceFollowsEditedImportedHoldPoint() => CheckTool("tool.tongs");
        [UnityTest] public IEnumerator ScoopIceClusterFollowsEditedImportedHoldPoint() => CheckTool("tool.scoop");

        private static IEnumerator CheckTool(string id)
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/Bar.unity");
            yield return null;
            var session = UnityEngine.Object.FindFirstObjectByType<BarSession>();
            double Now() => session.EventTime(Time.realtimeSinceStartupAsDouble);
            ItemView View(string definition) => session.Views.First(v => v.DefinitionId == definition);
            void Aim(ItemView view)
            {
                session.Player.AimAt(view.Item is Fixture ? view.Anchor("Surface").position : view.GetComponent<Collider>().bounds.center);
                Physics.SyncTransforms();
                session.Targeting();
                Assert.That(session.Target, Is.SameAs(view));
            }
            void Click(string action, ItemView target)
            {
                Aim(target);
                int index = session.MenuOptions.ToList().FindIndex(o => o.Action.Id == action);
                Assert.That(index, Is.GreaterThanOrEqualTo(0), action);
                session.SelectMenu(index, true);
                session.LeftButton(true, Now());
                session.LeftButton(false, Now() + .001);
            }
            var tool = View(id);
            var hold = tool.Anchor("HoldPoint");
            Assert.That(hold, Is.Not.Null);
            Assert.That(hold.parent, Is.Not.SameAs(tool.transform), "覆盖 FBX 内嵌锚点的完整坐标换算");
            Aim(tool);
            session.RightButton(Now());
            yield return new WaitForSecondsRealtime(.25f);
            Assert.That(session.Hands.SideOf(tool.Item).HasValue, Is.True);
            Click("ice.take", View("fixture.icebin"));
            yield return new WaitForSecondsRealtime(.25f);
            var loaded = ((Tool)tool.Item).LoadedCubes;
            Assert.That(loaded, Is.GreaterThan(0));
            if (id == "tool.tongs") Assert.That(loaded, Is.EqualTo(1));
            else Assert.That(loaded, Is.GreaterThan(1));
            var ice = tool.transform.Find("Held ice (snapshot)");
            Assert.That(ice, Is.Not.Null);
            Transform[] Visible() => ice.GetComponentsInChildren<Transform>().Where(t => t.name == "Ice cube (snapshot)" && t.gameObject.activeInHierarchy).ToArray();
            void Check()
            {
                var cubes = Visible();
                Assert.That(cubes.Length, Is.EqualTo(loaded));
                Vector3 centre = cubes.Aggregate(Vector3.zero, (sum, cube) => sum + cube.position) / cubes.Length;
                if(id=="tool.scoop")
                {
                    Assert.That(Vector3.Distance(ice.position,hold.position),Is.LessThan(.00001f),"载体跟随作者持冰点，冰块在载体内按铲面支撑摆放");
                    Assert.That(Quaternion.Angle(ice.rotation,tool.GetComponent<ScoopIceRig>().HoldingRotation(hold)),Is.LessThan(.001f));
                }
                else{Assert.That(Vector3.Distance(centre, hold.position), Is.LessThan(.00001f));Assert.That(Quaternion.Angle(ice.rotation, hold.rotation), Is.LessThan(.001f));}
                float side = IceAppearance.ConfiguredSide;
                foreach (var cube in cubes)
                {
                    Assert.That(cube.GetComponentsInChildren<MeshRenderer>().Select(r=>r.name), Is.EquivalentTo(new[]{"IceSurface","FrostCore"}));
                    Assert.That(Vector3.Distance(cube.lossyScale, Vector3.one * side), Is.LessThan(.00001f), "不继承 FBX 的 100 倍缩放");
                }
            }
            Check();
            int stock = ((IceBin)View("fixture.icebin").Item).StockCubes;
            hold.position += new Vector3(.043f,.017f,-.026f);
            hold.rotation = Quaternion.Euler(13,29,41);
            yield return null;
            yield return null;
            Check();
            Assert.That(((Tool)tool.Item).LoadedCubes, Is.EqualTo(loaded));
            Assert.That(((IceBin)View("fixture.icebin").Item).StockCubes, Is.EqualTo(stock));
            Click("ice.add", View("glass.highball"));
            yield return new WaitForSecondsRealtime(.25f);
            float deliveryDeadline=Time.realtimeSinceStartup+4;
            while(session.IceDeliveryBusy&&Time.realtimeSinceStartup<deliveryDeadline)yield return null;
            Assert.That(session.IceDeliveryBusy,Is.False,"等待工具抵达、放冰及回手，不能再假设点击立即转移");
            Assert.That(session.Clock.Error, Is.Null);
            var receipt=session.Journal.ReadLines().Select(Bar.Logging.SessionJournal.Decode).Last(r=>r.kind=="core_operation"&&r.operation.type=="AddIce"&&r.actorId==tool.InstanceId);
            int accepted=(int)Math.Round(receipt.operation.grams.Value/session.Adapter.World.Data.icePortionGrams);
            Assert.That(accepted,Is.InRange(1,loaded));loaded-=accepted;
            Assert.That(((Tool)tool.Item).LoadedCubes,Is.EqualTo(loaded),"仅扣此次实际入杯的整块冰");
            Assert.That(Visible().Length,Is.EqualTo(loaded),"剩余冰继续显示；实际清空才全部隐藏");
            if(loaded>0)Check();
        }
    }
}
