using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>已确认丢弃的工具冰：经水槽口上方落到排水处后消失；不建立或修改核心容器。</summary>
    public sealed class IceWasteFlight:MonoBehaviour
    {
        private readonly List<(Transform display,Vector3 landing,float low)> cubes=new List<(Transform,Vector3,float)>();
        private Transform frame;private Vector3 from;private float rim,elapsed;private VisualSettings settings;
        public int DisplayCount=>cubes.Count;
        public static void Play(ItemView tool,ItemView sink,int count)
        {
            var go=new GameObject("Tool ice to sink");go.transform.SetParent(sink.transform,false);var flight=go.AddComponent<IceWasteFlight>();
            flight.frame=sink.transform;flight.from=tool.LoadedIceOrigin;
            flight.settings=JsonUtility.FromJson<CueSettings>(Resources.Load<CueSettingsAsset>("BarCueSettings").Json.text).visual;
            PourPose.Mouth(sink,null,out var mouth,out _,out float floor);flight.rim=flight.frame.InverseTransformPoint(mouth).y;
            var drain=sink.GetComponentsInChildren<MeshRenderer>().FirstOrDefault(r=>r.name=="Drain");var target=drain?drain.bounds.center:mouth;target.y=floor;
            for(int i=0;i<count;i++)
            {
                var display=IceAppearance.Create(i,go.transform).transform;
                display.localScale=Vector3.one*IceAppearance.ConfiguredSide;display.localRotation=Quaternion.Euler(0,i*(360f/count),0);
                var root=flight.frame.InverseTransformPoint(display.position);float low=0;
                foreach(var mesh in display.GetComponentsInChildren<MeshFilter>())for(int corner=0;corner<8;corner++)
                {
                    var b=mesh.sharedMesh.bounds;var p=flight.frame.InverseTransformPoint(mesh.transform.TransformPoint(b.center+Vector3.Scale(b.extents,new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1))))-root;low=Mathf.Min(low,p.y);
                }
                var landing=flight.frame.InverseTransformPoint(target);landing.y-=low;landing.x+=(i-(count-1)*.5f)*IceAppearance.ConfiguredSide;
                flight.cubes.Add((display,landing,low));display.position=flight.from;
            }
        }
        private void Update()
        {
            elapsed+=Time.unscaledDeltaTime;float progress=elapsed/Mathf.Max(.001f,settings.iceEntrySeconds);
            foreach(var cube in cubes)cube.display.position=frame.TransformPoint(IceEntryPath.Position(frame.InverseTransformPoint(from),cube.landing,rim,cube.low,settings.iceEntryArc,progress));
            if(progress>=1)Destroy(gameObject);
        }
    }
}
