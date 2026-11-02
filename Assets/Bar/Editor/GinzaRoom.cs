using System;
using System.Linq;
using UnityEngine;
namespace Bar.Unity.Editor
{
    public static partial class GinzaLoungeBuilder
    {
        private static void WallDress(Transform wall,float length,bool art=false)
        {
            Box("Forest charcoal plaster upper wall",wall,new Vector3(length,2.0f,.12f),new Vector3(0,2.1f,-.06f),.005f,materials["ForestPlaster"]);
            Box("Solid walnut wainscot backing",wall,new Vector3(length,1.1f,.12f),new Vector3(0,.55f,-.06f),.006f,walnutDark);
            Box("100mm walnut baseboard",wall,new Vector3(length,.10f,.035f),new Vector3(0,.05f,.012f),.006f,walnut);
            Box("Brass baseboard inlay",wall,new Vector3(length-.03f,.005f,.003f),new Vector3(0,.085f,.031f),.001f,brassDark);
            Box("Chair rail lower profile",wall,new Vector3(length,.026f,.036f),new Vector3(0,1.061f,.015f),.006f,walnutDark);
            Box("Stepped 60mm chair rail cap",wall,new Vector3(length,.034f,.052f),new Vector3(0,1.091f,.021f),.008f,walnut);
            int n=Mathf.FloorToInt(length/.65f);float pitch=.65f;
            for(int i=0;i<n;i++){float x=(i-(n-1)*.5f)*pitch;Frame("True recessed wainscot surround "+i,wall,pitch-.055f,.83f,.036f,.026f,new Vector3(x,.56f,.019f),walnut);Box("Recessed walnut wall panel "+i,wall,new Vector3(pitch-.12f,.75f,.012f),new Vector3(x,.56f,.010f),.004f,walnutDark);}
            Collider(wall,new Vector3(0,1.55f,-.06f),new Vector3(length,3.1f,.12f));
        }
        private static void Shoji(Transform wall,Vector3 center)
        {
            var g=Group("Backlit Kumiko Shoji Window",wall,center);Frame("Deep walnut outer frame",g,1.4f,1.1f,.065f,.09f,Vector3.zero,walnut);
            Box("Low amber frosted diffuser",g,new Vector3(1.27f,.97f,.009f),new Vector3(0,0,-.025f),.003f,materials["ShojiDiffuser"]);
            for(int i=0;i<9;i++)Box("12 by 18mm vertical kumiko "+i,g,new Vector3(.012f,.975f,.018f),new Vector3((i-4)*.14f,0,.021f),.0018f,walnut);
            for(int i=0;i<7;i++)Box("12 by 18mm horizontal kumiko "+i,g,new Vector3(1.275f,.012f,.018f),new Vector3(0,(i-3)*.14f,.021f),.0018f,walnut);
            Lamp("Shoji warm courtyard spill",g,new Vector3(0,0,.13f),Hex("FF9E44"),.8f,3f);
        }
        private static void Art(Transform wall,float x,string image,string name)
        {
            var g=Group(name,wall,new Vector3(x,1.98f,.055f));Frame("Beveled aged brass picture frame",g,.62f,.83f,.020f,.025f,Vector3.zero,brass);
            Box("Ivory archival print mat",g,new Vector3(.578f,.788f,.008f),new Vector3(0,0,.001f),.003f,materials["PaperIvory"]);
            var mesh=new Mesh();mesh.vertices=new[]{new Vector3(-.25f,-.35f,.008f),new Vector3(.25f,-.35f,.008f),new Vector3(.25f,.35f,.008f),new Vector3(-.25f,.35f,.008f)};mesh.uv=new[]{Vector2.right,Vector2.zero,Vector2.up,Vector2.one};mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateNormals();Geometry("Vintage fine art print",g,mesh,materials[image],Vector3.zero,Quaternion.identity);
            Tube("Curved brass gallery light arm",g,Bezier(new Vector3(0,.48f,0),new Vector3(0,.57f,.04f),new Vector3(0,.57f,.17f),new Vector3(0,.50f,.20f)),.010f,brass);
            var lightBody=Turn("Cylindrical turned brass picture light",g,Disc(.018f,.27f,.005f),brass,new Vector3(-.135f,.50f,.20f),48);lightBody.transform.localRotation=Quaternion.Euler(0,0,-90);
            var l=Lamp("Warm gallery print wash",g,new Vector3(0,.475f,.18f),Hex("FFE4B8"),.8f,1.5f,true,85,40);l.transform.localRotation=Quaternion.LookRotation(new Vector3(0,-.70f,-.20f),Vector3.forward);
        }
        private static void Room()
        {
            var g=Group("ArchitecturalEnvelope",lounge,Vector3.zero);float roomLength=wallFront-wallBack,center=(wallBack+wallFront)*.5f;
            var ground=Group("Dark walnut herringbone floor",g,new Vector3(0,-.05f,center));
            var slab=Box("Beveled parquet floor slab",ground,new Vector3(6.5f,.10f,roomLength),Vector3.zero,.008f,materials["HerringboneFloor"]);
            var mesh=slab.GetComponent<MeshFilter>().sharedMesh;var uv=mesh.vertices.Select(p=>new Vector2((p.x+3.25f)/1.44f,(p.z+roomLength*.5f)/1.44f)).ToArray();mesh.uv=uv;
            Collider(ground,Vector3.zero,new Vector3(6.5f,.1f,roomLength));
            var mat=Group("Ribbed rubber anti fatigue mat",ground,new Vector3(0,.054f,-.90f-center));Box("Contoured rubber mat base",mat,new Vector3(1.95f,.008f,.60f),Vector3.zero,.003f,rubber);
            for(int i=0;i<65;i++)Box("Soft rounded rubber rib "+i,mat,new Vector3(.014f,.003f,.56f),new Vector3((i-32)*.029f,.0045f,0),.0013f,rubber);
            var rear=Group("Rear wainscot wall",g,new Vector3(0,0,wallBack),Quaternion.identity);WallDress(rear,6.5f);
            var front=Group("Courtyard lounge wall",g,new Vector3(0,0,wallFront),Quaternion.Euler(0,180,0));WallDress(front,6.5f);Shoji(front,new Vector3(0,2.05f,.065f));Art(front,-1.78f,"VintageGinza","Framed vintage Ginza print");Art(front,1.78f,"VintageJazz","Framed vintage jazz print");
            var left=Group("Left wainscot wall",g,new Vector3(-3.25f,0,center),Quaternion.Euler(0,90,0));WallDress(left,roomLength);
            var right=Group("Right wainscot wall",g,new Vector3(3.25f,0,center),Quaternion.Euler(0,-90,0));WallDress(right,roomLength);
            var ceiling=Group("Timber ceiling and four exposed beams",g,new Vector3(0,3.1f,center));Box("Dark warm timber ceiling",ceiling,new Vector3(6.5f,.12f,roomLength),new Vector3(0,.06f,0),.009f,materials["CeilingWalnut"]);Collider(ceiling,new Vector3(0,.06f,0),new Vector3(6.5f,.12f,roomLength));
            var beamZ=new[]{wallBack+.75f,depth*.5f+.12f,2.0f,wallFront-.55f};for(int i=0;i<4;i++)Box("160 by 200mm walnut ceiling beam "+(i+1),ceiling,new Vector3(6.5f,.20f,.16f),new Vector3(0,-.10f,beamZ[i]-center),.009f,walnutDark);
        }
    }
}
