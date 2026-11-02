using System.Collections;
using System.IO;
using System.Linq;
using Bar.Domain;
using Bar.Unity;
using CraftingResearch.CoreV2;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
namespace Bar.Tests
{
    /// <summary>敞口杯里安顿好的冰在被平稳端着走时，支撑接触不能当成撞击发声。</summary>
    public sealed class OpenIceQuietTests
    {
        private static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../QReports"));
        [UnityTest] public IEnumerator SettledIceInACarriedGlassDoesNotChatter()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;
            var session=Object.FindFirstObjectByType<BarSession>();session.enabled=false;
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter")b.enabled=false;
            var view=session.Views.First(v=>v.DefinitionId=="glass.highball");var glass=(Glass)view.Item;var rig=view.GetComponentInChildren<LocalIceWorld>();rig.enabled=false;
            session.Adapter.World.Apply(new Operation{type="AddIce",to=glass.CoreVesselId,grams=120});session.Adapter.Advance(0);
            for(int i=0;i<240;i++)rig.Advance(1f/60);
            int settled=rig.Impacts.Count;var origin=view.transform.position;
            // A gentle carry: 4 cm sway at 3 rad/s is about 0.36 m/s², above the wake threshold, so the cubes stay awake on their supports.
            for(int frame=0;frame<180;frame++){view.transform.position=origin+new Vector3(Mathf.Sin(frame/60f*3)*.04f,0,0);rig.Advance(1f/60);}
            int carried=rig.Impacts.Count-settled;Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output,"open-ice-carry.txt"),"impactsWhileCarried="+carried+"\n"+string.Join("\n",rig.Impacts.Recent.Select(e=>e.Kind+" "+e.Impulse+" submerged="+e.Submerged)));
            Assert.That(rig.Bodies.Count(),Is.GreaterThan(1));
            Assert.That(carried,Is.LessThanOrEqualTo(3),"端杯平移时，靠在杯底和杯壁上的冰不应每 0.04 秒响一次");
        }
        /// <summary>放进杯里的冰每块至多一次"落定"，之后端着走不再有落定事件。</summary>
        [UnityTest] public IEnumerator IceDroppedIntoAGlassLandsOncePerCube()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;
            var session=Object.FindFirstObjectByType<BarSession>();session.enabled=false;
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter")b.enabled=false;
            var view=session.Views.First(v=>v.DefinitionId=="glass.highball");var glass=(Glass)view.Item;var rig=view.GetComponentInChildren<LocalIceWorld>();rig.enabled=false;
            int landings=0,others=0;rig.Impacts.Emitted+=hit=>{if(hit.Landing)landings++;else others++;};
            session.IceOrigins[glass.CoreVesselId]=view.transform.position+Vector3.up*.2f;int cubes=4;
            session.Adapter.World.Apply(new Operation{type="AddIce",to=glass.CoreVesselId,grams=cubes*session.Adapter.World.Data.icePortionGrams});session.Adapter.Advance(0);
            for(int i=0;i<300;i++)rig.Advance(1f/60);int landed=landings;var origin=view.transform.position;
            for(int frame=0;frame<180;frame++){view.transform.position=origin+new Vector3(Mathf.Sin(frame/60f*3)*.04f,0,0);rig.Advance(1f/60);}
            Directory.CreateDirectory(Output);File.WriteAllText(Path.Combine(Output,"ice-landing.txt"),"cubes="+cubes+" landings="+landed+" landingsWhileCarried="+(landings-landed)+" otherHits="+others);
            Assert.That(landed,Is.InRange(1,cubes),"每块冰至多一次落定声");Assert.That(landings,Is.EqualTo(landed),"端着走时不再有落定");
        }
    }
}
