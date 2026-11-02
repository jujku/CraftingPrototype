using System.Collections;
using System.Linq;
using Bar.Domain;
using Bar.Unity;
using NUnit.Framework;
using UnityEngine;

namespace Bar.Tests
{
    /// <summary>PlayMode 流程共用的瓶盖步骤：用真实射线和鼠标入口取下瓶盖，再把瓶盖放到吧台后排空位。</summary>
    internal static class CapSteps
    {
        private static double Now(BarSession s) => s.EventTime(Time.realtimeSinceStartupAsDouble);
        /// <summary>把台面局部坐标转换为世界位置，测试不依赖调酒师位于吧台哪一面。</summary>
        internal static Vector3 TablePoint(BarSession s,float x,float z)
        {
            var counter=s.Views.Single(v=>v.DefinitionId=="fixture.counter");
            float y=counter.transform.InverseTransformPoint(counter.Anchor("Surface").position).y;
            return counter.transform.TransformPoint(new Vector3(x,y,z));
        }
        /// <summary>酒瓶自带瓶盖的场景视图。</summary>
        internal static ItemView CapOf(BarSession s, ItemView bottle) => s.Views.Single(v => v.Item == ((Bottle)bottle.Item).Cap);
        /// <summary>吧台后排一个准星能命中、没有被占用、不在开口上的位置。</summary>
        internal static Vector3 FreeSpot(BarSession s, ItemView placing)
        {
            var counter = s.Views.First(v => v.DefinitionId == "fixture.counter");
            var surface = counter.GetComponent<PlacementSurface>();
            for (float x = -1.12f; x <= 1.12f; x += .06f)
            {
                var point = TablePoint(s,x,.345f - placing.Size.z * .5f);
                bool occupied = s.Views.Any(v => v != placing && v.Item is not Fixture && v.Item.Parent == null &&
                    v.Item.Location.Kind == LocationKind.Surface &&
                    Vector2.Distance(new Vector2(point.x, point.z), new Vector2(v.transform.position.x, v.transform.position.z)) < (placing.Size.x + v.Size.x) * .5f + .012f);
                if (occupied || !surface.CanPlace(point, placing.Size)) continue;
                s.Player.AimAt(point); Physics.SyncTransforms(); s.Targeting();
                if (s.Target && s.Target.Item is BarCounter) return point;
            }
            Assert.Fail("吧台后排没有空位放 " + placing.DefinitionId);
            return default;
        }
        /// <summary>把手里的物品放到 FreeSpot，必要时先切换主手。</summary>
        internal static IEnumerator PutDown(BarSession s, ItemView view)
        {
            var side = s.Hands.SideOf(view.Item);
            Assert.That(side.HasValue, Is.True, "要放下的物品不在手里 " + view.DefinitionId);
            if (s.Hands.MainHand != side) { s.ToggleMain(Now(s)); yield return new WaitForSecondsRealtime(.14f); }
            var point = FreeSpot(s, view);
            s.Player.AimAt(point); Physics.SyncTransforms(); s.Targeting();
            s.RightButton(Now(s));
            yield return new WaitForSecondsRealtime(.28f);
            Assert.That(s.Hands.SideOf(view.Item), Is.Null, "放下 " + view.DefinitionId + "；" + s.Clock.Error);
        }
        /// <summary>取下瓶盖并放到吧台后排。瓶子可以在台面上，也可以在一只手里；都需要另一只手空着接住瓶盖。</summary>
        internal static IEnumerator Uncap(BarSession s, ItemView bottle)
        {
            var item = (Bottle)bottle.Item;
            if (item.IsOpen) yield break;
            if (s.Hands.SideOf(item).HasValue) s.Targeting();
            else Aim(s,bottle);
            int index = s.MenuOptions.ToList().FindIndex(o => o.Action.Id == "bottle.open" && o.Target == item);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "取下瓶盖需要一只空手 " + bottle.DefinitionId);
            s.SelectMenu(index, true); s.LeftButton(true, Now(s)); s.LeftButton(false, Now(s) + .001);
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(item.IsOpen, Is.True, bottle.DefinitionId + "；" + s.Clock.Error);
            yield return PutDown(s, CapOf(s, bottle));
        }
        /// <summary>使用真实射线寻找可见碰撞面；新瓶身的中心可能被水槽挡住，不能假定包围盒中心永远可点。</summary>
        internal static void Aim(BarSession session,ItemView view)
        {
            var bounds=view.GetComponent<Collider>().bounds;
            foreach(float y in new[]{0f,.6f,-.6f})foreach(float x in new[]{0f,-.8f,.8f})foreach(float z in new[]{0f,-.6f,.6f})
            {session.Player.AimAt(bounds.center+Vector3.Scale(bounds.extents,new Vector3(x,y,z)));Physics.SyncTransforms();session.Targeting();if(session.Target==view)return;}
            Assert.That(session.Target,Is.SameAs(view),"目标没有可见碰撞面："+view.DefinitionId);
        }
    }
}
