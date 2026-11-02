using System.Linq;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>冰与承接面接近时投射柔和透光斑，方向来自主光；纯视觉，不参与热量、融冰或其他核心运算。</summary>
    public sealed class IceTransmissionPatch:MonoBehaviour
    {
        private GameObject patch;private Material material;private Mesh mesh;
        private Light mainLight;
        public bool HasContact=>patch&&patch.activeInHierarchy;
        public void SyncLayer(){if(patch)patch.layer=gameObject.layer;}
        private readonly System.Collections.Generic.List<(Transform frame,Vector3[] vertices,int[] triangles)> toolMeshes=new System.Collections.Generic.List<(Transform,Vector3[],int[])>();
        private void Start()
        {
            patch=new GameObject("Ice transmitted light");mesh=new Mesh();
            mesh.vertices=new[]{new Vector3(-1,0,-1),new Vector3(-1,0,1),new Vector3(1,0,1),new Vector3(1,0,-1)};
            mesh.uv=new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right};mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateNormals();
            patch.AddComponent<MeshFilter>().sharedMesh=mesh;material=new Material(Shader.Find("Bar/IceTransmission"));patch.AddComponent<MeshRenderer>().sharedMaterial=material;
            mainLight=RenderSettings.sun?RenderSettings.sun:FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l=>l.enabled).OrderByDescending(l=>l.type==LightType.Directional).ThenByDescending(l=>l.intensity).FirstOrDefault();
            var owner=GetComponentInParent<ItemView>();
            if(owner&&owner.Item is Bar.Domain.IceScoop)
            {
                var visual=owner.transform.Find("Visual");
                if(visual)foreach(var filter in visual.GetComponentsInChildren<MeshFilter>())if(filter.sharedMesh&&filter.sharedMesh.isReadable)toolMeshes.Add((filter.transform,filter.sharedMesh.vertices,filter.sharedMesh.triangles));
            }
        }
        private bool ToolContact(Vector3 origin,float range,out Vector3 point,out Vector3 normal)
        {
            point=Vector3.zero;normal=Vector3.up;float nearest=range;bool found=false;
            foreach(var entry in toolMeshes)
            {
                var o=entry.frame.InverseTransformPoint(origin);var direction=entry.frame.InverseTransformVector(Vector3.down);
                for(int i=0;i<entry.triangles.Length;i+=3)
                {
                    var a=entry.vertices[entry.triangles[i]];var e=entry.vertices[entry.triangles[i+1]]-a;var f=entry.vertices[entry.triangles[i+2]]-a;
                    var h=Vector3.Cross(direction,f);float determinant=Vector3.Dot(e,h);if(Mathf.Abs(determinant)<1e-7f*e.magnitude*f.magnitude*direction.magnitude)continue;
                    var s=o-a;float u=Vector3.Dot(s,h)/determinant;if(u<0||u>1)continue;
                    var q=Vector3.Cross(s,e);float v=Vector3.Dot(direction,q)/determinant;if(v<0||u+v>1)continue;
                    float distance=Vector3.Dot(f,q)/determinant;if(distance<0||distance>nearest)continue;
                    nearest=distance;point=origin+Vector3.down*distance;normal=entry.frame.TransformDirection(Vector3.Cross(e,f)).normalized;if(normal.y<0)normal=-normal;found=true;
                }
            }
            return found;
        }
        private void LateUpdate()
        {
            if(!patch)return;var p=PourSettings.Load();var center=transform.position;
            var renderer=GetComponentsInChildren<MeshRenderer>().FirstOrDefault(r=>r.enabled);if(!renderer){patch.SetActive(false);return;}
            float bottom=renderer.bounds.min.y;Vector3 position=Vector3.zero,normal=Vector3.up;bool hit=false;
            var binding=GetComponentInParent<ContainerVisual>();
            if(binding){position=binding.transform.TransformPoint(new Vector3(binding.Shape.Axis.x,binding.Shape.Bottom,binding.Shape.Axis.y));position.x=center.x;position.z=center.z;hit=bottom-position.y<p.iceTransmissionContactDistance&&bottom>=position.y-p.iceTransmissionPenetrationTolerance;}
            else if(toolMeshes.Count>0)hit=ToolContact(new Vector3(center.x,bottom+p.iceTransmissionContactDistance,center.z),p.iceTransmissionRayRange,out position,out normal)&&Mathf.Abs(bottom-position.y)<p.iceTransmissionContactDistance;
            else
            {
                foreach(var candidate in Physics.RaycastAll(new Vector3(center.x,bottom+p.iceTransmissionPenetrationTolerance,center.z),Vector3.down,p.iceTransmissionRayRange).OrderBy(h=>h.distance))
                    if(!candidate.transform.IsChildOf(transform)&&!transform.IsChildOf(candidate.transform)){position=candidate.point;normal=candidate.normal;hit=true;break;}
            }
            patch.SetActive(hit);if(!hit)return;
            var direction=mainLight?(mainLight.type==LightType.Directional?-mainLight.transform.forward:(mainLight.transform.position-center).normalized):Vector3.up;
            position-=Vector3.ProjectOnPlane(direction,normal)*Mathf.Max(p.iceTransmissionPenetrationTolerance,bottom-position.y)/Mathf.Max(.2f,Vector3.Dot(direction,normal));
            patch.transform.SetPositionAndRotation(position+normal*.0004f,Quaternion.FromToRotation(Vector3.up,normal));patch.transform.localScale=Vector3.one*p.iceTransmissionRadius;
            material.SetFloat("_Intensity",p.iceTransmissionIntensity);
        }
        private void OnDisable(){if(patch)patch.SetActive(false);}
        private void OnDestroy(){if(patch)Destroy(patch);if(mesh)Destroy(mesh);if(material)Destroy(material);}
    }
}
