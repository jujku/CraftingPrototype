using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
namespace Bar.Unity.Editor
{
    public static partial class GinzaLoungeBuilder
    {
        private static Material walnut,walnutDark,brass,brassDark,leather,burgundy,iron,stone,amberGlow,filament,glassAmber,rubber;
        private static float width,depth,top,floor,backFront,wallBack,wallFront;
        private static void CounterWrap()
        {
            var g=Group("BarCounter_CustomerWrap",lounge,Vector3.zero);float hw=width*.5f,hd=depth*.5f;
            Box("Recessed 100mm toe kick front",g,new Vector3(width,.10f,.045f),new Vector3(0,.05f,hd-.006f),.006f,walnutDark);
            Box("Upper 40mm walnut moulding front",g,new Vector3(width+.022f,.04f,.045f),new Vector3(0,top-.025f,hd+.004f),.006f,walnut);
            int count=Mathf.FloorToInt(width/.03f);for(int i=0;i<count;i++)HalfSlat("Front tambour half-round "+i,g,top-.12f,walnut,new Vector3((i-(count-1)*.5f)*.03f,.10f,hd+.012f),Quaternion.identity);
            foreach(int side in new[]{-1,1})
            {
                int n=Mathf.FloorToInt(depth/.03f);var q=Quaternion.Euler(0,90*side,0);
                for(int i=0;i<n;i++)HalfSlat("Side "+side+" tambour "+i,g,top-.12f,walnut,new Vector3(side*(hw+.012f),.10f,(i-(n-1)*.5f)*.03f),q);
                Box("Side recessed plinth "+side,g,new Vector3(.045f,.10f,depth),new Vector3(side*hw,.05f,0),.005f,walnutDark);
                Box("Side crown moulding "+side,g,new Vector3(.045f,.04f,depth),new Vector3(side*hw,top-.025f,0),.006f,walnut);
            }
            float ledge=top+.12f;
            Box("Raised drinking ledge front 4 segment 10mm bevel",g,new Vector3(width+.10f,.045f,.28f),new Vector3(0,ledge-.0225f,hd+.14f),.010f,walnut);
            Box("L return drinking ledge",g,new Vector3(.28f,.045f,depth+.02f),new Vector3(-hw-.14f,ledge-.0225f,-.006f),.010f,walnut);
            Box("Concealed ledge amber wash",g,new Vector3(width-.12f,.008f,.008f),new Vector3(0,ledge-.048f,hd+.026f),.0015f,amberGlow);
            Lamp("Under ledge warm pool L",g,new Vector3(-.62f,.55f,hd+.18f),Hex("FF9A3C"),.18f,2.6f);
            Lamp("Under ledge warm pool R",g,new Vector3(.62f,.55f,hd+.18f),Hex("FF9A3C"),.18f,2.6f);
            Tube("38mm continuous brushed brass foot rail",g,new[]{new Vector3(-hw+.08f,.18f,hd+.14f),new Vector3(hw-.08f,.18f,hd+.14f)},.038f,brass);
            foreach(float x in new[]{-.88f,0,.88f})
            {
                var flange=Turn("Bracket wall flange",g,Disc(.033f,.009f,.003f),brass,new Vector3(x,.27f,hd+.014f),48);flange.transform.localRotation=Quaternion.Euler(90,0,0);
                Tube("Sculpted swept brass bracket",g,Bezier(new Vector3(x,.27f,hd+.018f),new Vector3(x,.22f,hd+.045f),new Vector3(x,.15f,hd+.075f),new Vector3(x,.18f,hd+.14f)),.018f,brass);
                Torus("Bracket rail ring collar",g,.021f,.004f,new Vector3(x,.18f,hd+.14f),brass,Quaternion.Euler(0,0,90));
            }
            var mat=Group("ServeCoasterMat",g,new Vector3(0,ledge+.004f,hd+.14f),Quaternion.Euler(-90,0,0));
            Frame("Chamfered 10mm brass frame",mat,.32f,.22f,.010f,.008f,Vector3.zero,brass);
            Box("Inset cognac leather pad",mat,new Vector3(.300f,.200f,.006f),new Vector3(0,0,-.0005f),.003f,leather);
            foreach(float x in new[]{-.145f,.145f})foreach(float y in new[]{-.095f,.095f}){var rivet=Turn("Tiny brass corner rivet",mat,Disc(.0025f,.0018f,.0006f),brass,new Vector3(x,y,.0025f),24);rivet.transform.localRotation=Quaternion.Euler(90,0,0);}
            Collider(g,new Vector3(0,(top+.12f)*.5f,hd+.12f),new Vector3(width+.10f,top+.12f,.30f));
            var sideBlock=Group("L return clean collider",g,Vector3.zero);Collider(sideBlock,new Vector3(-hw-.14f,(top+.12f)*.5f,0),new Vector3(.28f,top+.12f,depth));
        }
        private static void Stool(float x,float yaw,int id)
        {
            var g=Group("Speakeasy_BarStool_"+id,lounge,new Vector3(x,0,depth*.5f+.41f),Quaternion.Euler(0,yaw,0));
            var cushion=Group("01 Domed plump cushion",g,Vector3.zero);
            Turn("Barrel curved leather with 12mm dome",cushion,new[]{P(0,.605f),P(.159f,.605f),P(.172f,.609f),P(.178f,.620f),P(.18f,.639f),P(.18f,.655f),P(.174f,.673f),P(.16f,.680f),P(.12f,.685f),P(.06f,.691f),P(0,.692f)},leather,Vector3.zero,64);
            Torus("Leather welt top stitching",cushion,.174f,.0011f,new Vector3(0,.673f,0),brassDark);
            var pan=Group("02 Collar and walnut seat pan",g,Vector3.zero);
            Turn("Brass under seat collar 15mm",pan,new[]{P(.155f,.590f),P(.175f,.590f),P(.178f,.593f),P(.178f,.602f),P(.175f,.605f),P(.155f,.605f),P(.155f,.590f)},brass,Vector3.zero);
            Turn("Bullnose walnut seat pan 25mm",pan,Disc(.173f,.025f,.006f),walnut,new Vector3(0,.565f,0));
            var legs=Group("03 Four splayed tapered legs and ferrules",g,Vector3.zero);
            for(int j=0;j<4;j++)
            {
                float a=(45+j*90)*Mathf.Deg2Rad;var direction=new Vector3(Mathf.Sin(a),0,Mathf.Cos(a));var bottom=direction*.205f+Vector3.up*.010f;var upper=direction*.131f+Vector3.up*.572f;float len=Vector3.Distance(bottom,upper);
                var leg=Turn("7.5 degree tapered walnut leg "+j,legs,new[]{P(0,0),P(.011f,0),P(.012f,.003f),P(.018f,len-.005f),P(.019f,len),P(0,len)},walnut,bottom,32);leg.transform.localRotation=Quaternion.FromToRotation(Vector3.up,(upper-bottom).normalized);
                var ferrule=Turn("Polished 45mm brass ferrule "+j,legs,Disc(.0128f,.045f,.002f),brass,bottom,32);ferrule.transform.localRotation=leg.transform.localRotation;
                Turn("Rubber floor glide "+j,legs,Disc(.012f,.009f,.002f),rubber,direction*.205f,32);
                var ringPoint=direction*.191f+Vector3.up*.24f;Torus("Footrest mounting collar "+j,legs,.014f,.003f,ringPoint,brass,Quaternion.FromToRotation(Vector3.up,(upper-bottom).normalized));
            }
            var rest=Group("04 Circular brass footrest",g,Vector3.zero);Torus("195mm torus footrest",rest,.195f,.01f,new Vector3(0,.24f,0),brass);
            var back=Group("05 Channel tufted curved bucket",g,Vector3.zero);
            ArcPad("110 degree curved outer leather shell",back,.202f,0,.15f,.027f,-55,55,new Vector3(0,.759f,0),burgundy);
            for(int j=0;j<5;j++)ArcPad("Padded vertical channel "+(j+1),back,.189f,0,.145f,.031f,-54+j*22,-34+j*22,new Vector3(0,.759f,0),leather);
            var struts=Group("06 Twin bent brass backrest struts",g,Vector3.zero);
            foreach(float sx in new[]{-.105f,.105f}){Tube("95 degree swept brass flat strut",struts,Bezier(new Vector3(sx,.562f,.02f),new Vector3(sx,.54f,.22f),new Vector3(sx,.64f,.225f),new Vector3(sx,.78f,.18f)),.018f,brass,.006f);var bolt=Turn("Backrest bolt",struts,Disc(.004f,.003f,.001f),brassDark,new Vector3(sx,.78f,.190f),24);bolt.transform.localRotation=Quaternion.Euler(90,0,0);}
            Collider(g,new Vector3(0,.42f,0),new Vector3(.49f,.84f,.49f));
        }
        private static void Pilaster(string name,Transform parent,Vector3 position)
        {
            var section=new List<Vector2>();for(int i=0;i<=60;i++){float x=-.04f+.08f*i/60f,z=.02f;foreach(float center in new[]{-.023f,0,.023f}){float f=(x-center)/.008f;if(Mathf.Abs(f)<1)z-=.0055f*Mathf.Sqrt(1-f*f);}section.Add(new Vector2(x,z));}section.Add(new Vector2(.04f,-.02f));section.Add(new Vector2(-.04f,-.02f));
            int n=section.Count;var v=new List<Vector3>();var uv=new List<Vector2>();var tri=new List<int>();for(int j=0;j<2;j++)foreach(var p in section){v.Add(new Vector3(p.x,j*1.43f,p.y));uv.Add(new Vector2(p.x*10,j*4.2f));}
            for(int i=0;i<n;i++)Quad(tri,i,(i+1)%n,n+(i+1)%n,n+i);
            for(int i=1;i<n-1;i++){tri.Add(0);tri.Add(i+1);tri.Add(i);tri.Add(n);tri.Add(n+i);tri.Add(n+i+1);}Geometry(name,parent,Mesh(v,tri,uv),walnut,position,Quaternion.identity);
            Box(name+" capital",parent,new Vector3(.105f,.045f,.064f),position+new Vector3(0,1.405f,0),.006f,walnut);
            Box(name+" base",parent,new Vector3(.10f,.05f,.06f),position+new Vector3(0,.025f,0),.006f,walnut);
        }
        private static void ArchValance(Transform parent,float center,float y)
        {
            var pts=Enumerable.Range(0,33).Select(i=>{float x=(i/32f-.5f)*.76f;return new Vector3(x+center,y+.075f*(1-(x/.38f)*(x/.38f)),.176f);}).ToArray();Tube("Curved arched walnut valance",parent,pts,.052f,walnut,.036f);
        }
        private static Vector2[] BottleProfile(int kind)
        {
            if(kind==0)return new[]{P(0,0),P(.031f,0),P(.035f,.006f),P(.035f,.178f),P(.032f,.190f),P(.018f,.203f),P(.012f,.210f),P(.012f,.268f),P(.014f,.273f),P(.014f,.284f),P(.009f,.284f),P(.009f,.211f),P(.031f,.177f),P(.031f,.008f),P(0,.008f)};
            if(kind==1)return new[]{P(0,0),P(.035f,0),P(.041f,.008f),P(.041f,.122f),P(.038f,.150f),P(.031f,.177f),P(.020f,.206f),P(.012f,.218f),P(.012f,.257f),P(.009f,.257f),P(.009f,.218f),P(.035f,.145f),P(.037f,.008f),P(0,.008f)};
            if(kind==2)return new[]{P(0,0),P(.040f,0),P(.049f,.009f),P(.049f,.145f),P(.046f,.166f),P(.019f,.179f),P(.014f,.186f),P(.014f,.225f),P(.010f,.225f),P(.010f,.188f),P(.045f,.162f),P(.045f,.009f),P(0,.009f)};
            return new[]{P(0,0),P(.025f,0),P(.036f,.018f),P(.043f,.051f),P(.037f,.080f),P(.023f,.099f),P(.022f,.111f),P(.030f,.126f),P(.032f,.149f),P(.024f,.172f),P(.010f,.183f),P(.010f,.217f),P(.007f,.217f),P(.007f,.183f),P(.028f,.145f),P(.018f,.103f),P(.038f,.051f),P(.025f,.008f),P(0,.008f)};
        }
        private static void DecorativeBottle(Transform parent,int index,Vector3 position)
        {
            int kind=index%4;var g=Group("Decorative bottle "+index+" silhouette "+kind,parent,position);var glass=materials[new[]{"AmberGlass","EmeraldGlass","CobaltGlass","ClearGlass"}[index%4]];
            var bottle=Turn("Multi segment spun glass body",g,BottleProfile(kind),glass,Vector3.zero,kind==2?48:64);if(kind==2){var mesh=bottle.GetComponent<MeshFilter>().sharedMesh;var vv=mesh.vertices;for(int i=0;i<vv.Length;i++){float radius=new Vector2(vv[i].x,vv[i].z).magnitude;if(radius>.025f){float c=vv[i].x/radius,s=vv[i].z/radius,k=Mathf.Pow(Mathf.Pow(Mathf.Abs(c),6)+Mathf.Pow(Mathf.Abs(s),6),-1f/6);vv[i].x*=k;vv[i].z*=k;}}mesh.vertices=vv;mesh.RecalculateNormals();mesh.RecalculateBounds();bottle.transform.localScale=new Vector3(1,.94f,.78f);}
            float h=BottleProfile(kind).Max(p=>p.y);Turn("Gold foil neck and closure",g,Disc(.015f,.026f,.002f),brass,new Vector3(0,h-.006f,0),32);
            var liquid=materials[kind%2==0?"WhiskyLiquid":"BotanicalLiquid"];Turn("Visible liquor fill",g,Disc(kind==2?.040f:kind==3?.026f:.029f,.122f,.004f),liquid,new Vector3(0,.008f,0));
            Box("Paper front label",g,new Vector3(.041f,.056f,.0012f),new Vector3(0,.094f,kind==2?.039f:kind==3?.031f:.035f),.0005f,materials["BottleLabel"]);
            Box("Foil label cartouche",g,new Vector3(.026f,.0022f,.001f),new Vector3(0,.103f,kind==2?.040f:kind==3?.032f:.036f),.0004f,brassDark);
        }
        private static void Backbar()
        {
            var g=Group("Backbar_Cabinet",lounge,new Vector3(0,0,backFront-.19f));
            Box("Recessed 80mm toe kick",g,new Vector3(2.50f,.08f,.30f),new Vector3(0,.04f,-.015f),.006f,walnutDark);
            Box("Credenza contoured case",g,new Vector3(2.60f,.735f,.37f),new Vector3(0,.4475f,-.002f),.008f,walnut);
            for(int i=0;i<4;i++)
            {
                var door=Group("Inset framed cabinet door "+(i+1),g,new Vector3((i-1.5f)*.634f,.446f,.190f));Frame("45mm inset surround with 4mm inner bevel",door,.616f,.700f,.045f,.030f,Vector3.zero,walnut);
                Box("Panel recessed by 12mm",door,new Vector3(.526f,.610f,.018f),new Vector3(0,0,-.006f),.004f,walnutDark);
                var knob=Turn("Lathe turned mushroom brass knob",door,new[]{P(0,0),P(.009f,0),P(.010f,.003f),P(.005f,.006f),P(.005f,.013f),P(.013f,.017f),P(.014f,.021f),P(.010f,.025f),P(0,.026f)},brass,new Vector3(.19f,.19f,.020f),48);knob.transform.localRotation=Quaternion.Euler(90,0,0);
            }
            Box("35mm bullnose dark stone countertop",g,new Vector3(2.65f,.035f,.40f),new Vector3(0,.8325f,.005f),.014f,stone);
            Box("Display case dark backing",g,new Vector3(2.6f,1.51f,.023f),new Vector3(0,1.595f,-.178f),.006f,walnutDark);
            for(int i=0;i<4;i++)Pilaster("Three concave flute pilaster "+(i+1),g,new Vector3(-1.26f+i*.84f,.85f,.174f));
            Box("Stepped cornice lower",g,new Vector3(2.65f,.038f,.40f),new Vector3(0,2.313f,0),.005f,walnut);
            Box("Stepped cornice middle",g,new Vector3(2.69f,.030f,.42f),new Vector3(0,2.347f,0),.007f,walnutDark);
            Box("Stepped crown cornice cap",g,new Vector3(2.73f,.038f,.44f),new Vector3(0,2.381f,0),.008f,walnut);
            int bottle=0;
            for(int bay=0;bay<3;bay++)
            {
                float bx=(bay-1)*.84f;ArchValance(g,bx,2.220f);
                for(int tier=0;tier<3;tier++)
                {
                    float y=1.00f+tier*.43f;
                    Box("28mm walnut shelf bay "+bay+" tier "+tier,g,new Vector3(.76f,.028f,.31f),new Vector3(bx,y,0),.005f,walnut);
                    Tube("4mm brass shelf gallery rail",g,new[]{new Vector3(bx-.35f,y+.045f,.154f),new Vector3(bx+.35f,y+.045f,.154f)},.004f,brass);
                    foreach(float offset in new[]{-.33f,.33f})Turn("Gallery rail stanchion",g,Disc(.003f,.035f,.001f),brass,new Vector3(bx+offset,y+.014f,.154f),24);
                    Box("Recessed FF AA 44 LED strip",g,new Vector3(.69f,.005f,.012f),new Vector3(bx,y-.018f,.12f),.0015f,amberGlow);
                    int number=tier<2?3:2;for(int b=0;b<number;b++)DecorativeBottle(g,bottle++,new Vector3(bx+(b-(number-1)*.5f)*.19f,y+.014f,-.016f));
                }
                foreach(float y in new[]{1.410f,1.840f,2.250f})Lamp("Backbar concealed shelf wash "+bay+"_"+y,g,new Vector3(bx,y,.130f),Hex("FFAA44"),.090f,.85f);
                StemwareRack(g,bx);
                for(int j=0;j<2;j++)
                {
                    var wine=Group("Inverted stemware "+bay+"_"+j,g,new Vector3(bx+(j-.5f)*.15f,2.22f,.055f),Quaternion.Euler(180,0,0));
                    Turn("Inverted coupe foot stem bowl",wine,new[]{P(0,0),P(.025f,0),P(.027f,.003f),P(.023f,.005f),P(.0035f,.007f),P(.003f,.074f),P(.011f,.078f),P(.027f,.089f),P(.037f,.111f),P(.038f,.122f),P(.036f,.123f),P(.035f,.112f),P(.025f,.092f),P(.010f,.083f),P(0,.081f)},materials["ClearGlass"],Vector3.zero,48);
                }
            }
            Collider(g,new Vector3(0,1.20f,0),new Vector3(2.73f,2.40f,.44f));
        }
        private static void StemwareRack(Transform parent,float x)
        {
            var rack=Group("Inverted stemware brass rack "+x,parent,new Vector3(x,0,.055f));foreach(float z in new[]{-.015f,.015f}){Tube("Brass glass base support rail",rack,new[]{new Vector3(-.30f,2.216f,z),new Vector3(.30f,2.216f,z)},.004f,brass);foreach(float sx in new[]{-.22f,.22f})Tube("Rack hanger to arched valance",rack,new[]{new Vector3(sx,2.216f,z),new Vector3(sx,2.256f,z)},.004f,brass);}
        }
        private static void Pendants()
        {
            var g=Group("Bar_PendantLight_Trio",lounge,Vector3.zero);float z=depth*.5f+.12f;
            Box("Rounded brass ceiling canopy",g,new Vector3(1.35f,.035f,.08f),new Vector3(0,2.88f,z),.008f,brassDark);
            foreach(float x in new[]{-.52f,0,.52f})
            {
                var lamp=Group("24 rib amber pendant "+x,g,new Vector3(x,2.02f-floor,z));
                Tube("5mm dark braided cord core",lamp,new[]{new Vector3(0,.219f,0),new Vector3(0,.85f,0)},.0042f,rubber);PendantCraftDetails(lamp);
                Turn("Tiered brass lamp socket",lamp,new[]{P(0,.15f),P(.022f,.15f),P(.025f,.156f),P(.025f,.169f),P(.018f,.174f),P(.018f,.199f),P(.014f,.215f),P(0,.215f)},brass,Vector3.zero,48);
                for(int i=0;i<3;i++)Torus("Socket knurled collar "+i,lamp,.024f,.0014f,new Vector3(0,.157f+i*.004f,0),brassDark);
                Turn("3mm wall fluted amber cloche shade",lamp,new[]{P(.030f,.155f),P(.033f,.148f),P(.042f,.133f),P(.056f,.108f),P(.075f,.076f),P(.096f,.034f),P(.110f,0),P(.108f,-.003f),P(.105f,.002f),P(.093f,.035f),P(.072f,.077f),P(.053f,.109f),P(.039f,.134f),P(.030f,.149f),P(.027f,.155f),P(.030f,.155f)}.Reverse().ToArray(),glassAmber,Vector3.zero,48,.0025f);
                Turn("Pear shaped Edison bulb",lamp,new[]{P(0,.029f),P(.014f,.031f),P(.028f,.044f),P(.032f,.065f),P(.028f,.085f),P(.016f,.104f),P(.011f,.131f),P(0,.135f)},materials["ClearGlass"],Vector3.zero,48);
                Tube("Glowing warm gold filament loop",lamp,Bezier(new Vector3(-.012f,.099f,0),new Vector3(-.018f,.044f,0),new Vector3(.018f,.044f,0),new Vector3(.012f,.099f,0),32),.0020f,filament);
                Lamp("Warm task pendant spot",lamp,new Vector3(0,.07f,0),Hex("FFE4B8"),4.1f,3.5f,true,75,35,true);
            }
        }
        private static void PendantCraftDetails(Transform lamp)
        {
            Turn("Knurled brass socket band",lamp,new[]{P(.023f,.155f),P(.024f,.156f),P(.024f,.170f),P(.023f,.172f),P(.021f,.172f),P(.021f,.155f),P(.023f,.155f)},brass,Vector3.zero,96,.00045f);
            foreach(int direction in new[]{-1,1}){var path=Enumerable.Range(0,961).Select(i=>{float f=i/960f,a=f*80*2*Mathf.PI*direction;return new Vector3(Mathf.Cos(a)*.00205f,.219f+.631f*f,Mathf.Sin(a)*.00205f);}).ToArray();Geometry("Interwoven dark cord strand "+direction,lamp,Sweep(path,.0009f,0,6),rubber,Vector3.zero,Quaternion.identity);}
        }
        private static void BoothAndTable()
        {
            float boothZ=wallFront-.37f;var g=Group("Lounge_Booth_Set",lounge,Vector3.zero);
            var booth=Group("Nine channel leather booth",g,new Vector3(0,0,boothZ));
            Box("Walnut plinth 180mm",booth,new Vector3(1.35f,.18f,.68f),new Vector3(0,.09f,0),.018f,walnutDark);
            Box("Walnut seat frame",booth,new Vector3(1.35f,.11f,.67f),new Vector3(0,.235f,0),.013f,walnut);
            Box("140mm plump leather seat",booth,new Vector3(1.35f,.14f,.64f),new Vector3(0,.36f,-.018f),.047f,burgundy);
            var reclining=Group("102 degree reclined back",booth,new Vector3(0,.405f,.185f),Quaternion.Euler(12,0,0));
            for(int i=0;i<9;i++)Box("Vertical plump bolster "+(i+1),reclining,new Vector3(.146f,.48f,.13f),new Vector3((i-4)*.15f,.24f,0),.056f,burgundy);
            Box("Walnut top rail",reclining,new Vector3(1.37f,.045f,.145f),new Vector3(0,.470f,0),.016f,walnut);
            Collider(booth,new Vector3(0,.45f,0),new Vector3(1.38f,.90f,.68f));
            var table=Group("Pedestal bistro table",g,new Vector3(0,0,2.79f));
            Turn("Bullnose circular walnut top 620mm",table,Disc(.31f,.028f,.009f),walnut,new Vector3(0,.692f,0),64);
            Torus("Thin brass edge band",table,.309f,.003f,new Vector3(0,.705f,0),brass);
            Turn("Stepped cast iron base and spherical node",table,new[]{P(0,0),P(.177f,0),P(.19f,.006f),P(.19f,.018f),P(.18f,.028f),P(.12f,.038f),P(.06f,.058f),P(.032f,.09f),P(.024f,.24f),P(.045f,.263f),P(.057f,.29f),P(.045f,.32f),P(.025f,.34f),P(.023f,.64f),P(.05f,.67f),P(0,.68f)},iron,Vector3.zero,64);
            for(int i=0;i<4;i++){float a=i*Mathf.PI*.5f;var v=new Vector3(Mathf.Sin(a),0,Mathf.Cos(a));Tube("Four prong spider mount "+i,table,Bezier(Vector3.up*.63f,Vector3.up*.67f+v*.06f,Vector3.up*.68f+v*.16f,Vector3.up*.687f+v*.245f),.026f,iron,.012f);}
            var lamp=Group("Brass mushroom banker lamp",table,new Vector3(0,.72f,0));
            Turn("Stepped brass lamp base",lamp,Disc(.073f,.026f,.006f),brass,Vector3.zero,64);
            Turn("Tapered stem and decorative neck",lamp,new[]{P(0,.026f),P(.027f,.026f),P(.022f,.04f),P(.012f,.061f),P(.009f,.231f),P(.018f,.246f),P(.018f,.256f),P(0,.256f)},brass,Vector3.zero,48);
            var profile=new List<Vector2>{P(0,.34f)};for(int i=1;i<=16;i++){float a=i*Mathf.PI*.5f/16;profile.Add(P(.11f*Mathf.Sin(a),.23f+.11f*Mathf.Cos(a)));}profile.Add(P(.108f,.226f));for(int i=16;i>=0;i--){float a=i*Mathf.PI*.5f/16;profile.Add(P(.106f*Mathf.Sin(a),.23f+.106f*Mathf.Cos(a)));}profile.Reverse();Turn("Double hemisphere spun brass dome",lamp,profile.ToArray(),brass,Vector3.zero,64);
            Turn("Warm reflective shade underside",lamp,new[]{P(.101f,.230f),P(.101f,.232f),P(.02f,.232f),P(.02f,.230f),P(.101f,.230f)},materials["LampLining"],Vector3.zero,64);
            Lamp("Bistro lamp amber mood",lamp,new Vector3(0,.22f,0),Hex("FFB35B"),.65f,2.5f);
            Lamp("Bistro lamp downward pool",lamp,new Vector3(0,.21f,0),Hex("FFD28A"),.7f,1.6f,true,100,55);
            Collider(table,new Vector3(0,.36f,0),new Vector3(.62f,.72f,.62f));
        }
    }
}
