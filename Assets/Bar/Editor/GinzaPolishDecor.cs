using System;
using System.Linq;
using UnityEngine;
namespace Bar.Unity.Editor
{
    public static partial class GinzaLoungeBuilder
    {
        private static void PopulateBooths()
        {
            var left=lounge.Find("Lounge_Booth_Set");left.name="Lounge_Booth_Set_Left";left.localPosition=new Vector3(-.95f,0,.8f);
            var right=UnityEngine.Object.Instantiate(left.gameObject,lounge).transform;right.name="Lounge_Booth_Set_Right";right.localPosition=new Vector3(.95f,0,.8f);
            foreach(var set in new[]{left,right})TableDetails(set.Find("Pedestal bistro table"));
            foreach(float x in new[]{-.95f,.95f})TubChair(x);
            var screen=Group("Walnut brass privacy partition 1450mm",polish,new Vector3(0,0,4.0f));
            Box("Screen walnut base",screen,new Vector3(.085f,.12f,1.25f),new Vector3(0,.06f,0),.008f,walnutDark);
            foreach(float z in new[]{-.60f,.60f})Box("Screen stepped end post",screen,new Vector3(.065f,1.45f,.065f),new Vector3(0,.725f,z),.009f,walnut);
            Box("Screen lower rail",screen,new Vector3(.06f,.055f,1.20f),new Vector3(0,.18f,0),.006f,walnut);
            Box("Screen top brass cap",screen,new Vector3(.074f,.024f,1.26f),new Vector3(0,1.438f,0),.006f,brass);
            Box("Screen glass sill",screen,new Vector3(.065f,.04f,1.22f),new Vector3(0,1.02f,0),.006f,walnut);
            for(int i=0;i<13;i++)Box("Vertical walnut privacy slat "+i,screen,new Vector3(.045f,.80f,.032f),new Vector3(0,.60f,(i-6)*.088f),.007f,walnut);
            Box("Frosted glass privacy topper",screen,new Vector3(.014f,.375f,1.14f),new Vector3(0,1.23f,0),.003f,frost);
            for(int i=0;i<9;i++)Turn("Brass upper screen rod "+i,screen,Disc(.004f,.38f,.001f),brass,new Vector3(.016f,1.04f,(i-4)*.13f),24);
            Collider(screen,new Vector3(0,.725f,0),new Vector3(.085f,1.45f,1.25f));
        }
        private static void TableDetails(Transform table)
        {
            var g=Group("Cocktail menu coasters and candle",table,new Vector3(0,.72f,0));
            var menu=Group("Leather bound cocktail menu stand",g,new Vector3(-.145f,0,.10f),Quaternion.Euler(-9,-12,0));
            Box("Menu stand brass weighted base",menu,new Vector3(.145f,.012f,.075f),new Vector3(0,.006f,0),.004f,brass);
            Box("Burgundy leather menu cover",menu,new Vector3(.128f,.19f,.012f),new Vector3(0,.103f,0),.004f,burgundy);
            Box("Ivory menu inset",menu,new Vector3(.105f,.157f,.002f),new Vector3(0,.105f,-.007f),.0007f,materials["PaperIvory"]);
            for(int i=0;i<6;i++)Box("Printed cocktail menu line "+i,menu,new Vector3(i==0?.060f:.075f,.0015f,.001f),new Vector3(0,.155f-i*.019f,-.0085f),.0003f,brassDark);
            foreach(float x in new[]{-.175f,.175f}){Turn("Brass coaster with inset leather",g,Disc(.046f,.004f,.0015f),brass,new Vector3(x,0,-.105f),48);Turn("Leather coaster centre",g,Disc(.041f,.0015f,.0006f),leather,new Vector3(x,.004f,-.105f),48);}
            var candle=Group("Small amber candle votive",g,new Vector3(.17f,0,.10f));Turn("Brass votive holder",candle,new[]{P(0,0),P(.025f,0),P(.028f,.004f),P(.028f,.052f),P(.026f,.055f),P(.024f,.052f),P(.024f,.006f),P(0,.006f)},brass,Vector3.zero,48);Turn("Warm ivory candle",candle,Disc(.023f,.042f,.002f),materials["PaperIvory"],new Vector3(0,.006f,0),40);
            Turn("Candle flame",candle,new[]{P(0,0),P(.003f,.003f),P(.004f,.009f),P(.0015f,.017f),P(0,.022f)},ivoryFill,new Vector3(0,.048f,0),24);Lamp("Candle intimate warm pool",candle,new Vector3(0,.067f,0),Hex("FFC282"),.08f,.6f);
        }
        private static void TubChair(float x)
        {
            var g=Group("Leather walnut tub chair "+x,polish,new Vector3(x,0,2.90f),Quaternion.Euler(0,180,0));
            foreach(float sx in new[]{-.22f,.22f})foreach(float z in new[]{-.22f,.22f})
            {
                var leg=Group("Splayed turned walnut chair leg",g,new Vector3(sx,0,z),Quaternion.Euler(z*10,0,-sx*10));
                Turn("Tapered walnut leg",leg,new[]{P(0,0),P(.017f,0),P(.019f,.025f),P(.027f,.31f),P(.024f,.34f),P(0,.34f)},walnut,Vector3.zero,32);Turn("Brass chair foot ferrule",leg,Disc(.0195f,.042f,.003f),brass,Vector3.zero,32);
            }
            Turn("Walnut tub seat frame",g,Disc(.31f,.075f,.018f),walnut,new Vector3(0,.30f,0),64);
            Turn("Plump cognac seat cushion",g,Disc(.299f,.11f,.038f),leather,new Vector3(0,.375f,-.012f),64);
            ArcPad("Curved walnut tub outer shell",g,.306f,.65f,.35f,.032f,-110,110,new Vector3(0,.603f,0),walnut);
            for(int i=0;i<8;i++)ArcPad("Individual leather back channel "+i,g,.279f,.65f,.31f,.041f,-108+i*27, -83+i*27,new Vector3(0,.607f,0),leather);
            var rim=Enumerable.Range(0,65).Select(i=>{float a=(-110+i*220f/64)*Mathf.Deg2Rad;return new Vector3(Mathf.Sin(a)*.307f,.777f,Mathf.Cos(a)*.307f);}).ToArray();Tube("Curved walnut tub arm cap",g,rim,.034f,walnut);
            Collider(g,new Vector3(0,.39f,0),new Vector3(.70f,.78f,.70f));
        }
        private static void Entrance()
        {
            var wall=lounge.Find("ArchitecturalEnvelope/Right wainscot wall");float doorZ=2.40f;
            var g=Group("Speakeasy_EntryDoor",wall,new Vector3(doorZ-wall.localPosition.z,0,.025f));
            Box("1050 by 2250 solid walnut door leaf",g,new Vector3(1.05f,2.25f,.060f),new Vector3(0,1.125f,.032f),.007f,walnutDark);
            Frame("Deep stepped outer walnut casing",g,1.25f,2.37f,.070f,.10f,new Vector3(0,1.185f,.058f),walnutDark);
            Frame("Fine inner walnut casing bead",g,1.13f,2.29f,.024f,.040f,new Vector3(0,1.145f,.106f),walnut);
            Box("120mm crown door header",g,new Vector3(1.33f,.12f,.15f),new Vector3(0,2.36f,.067f),.010f,walnut);
            Box("Stepped crown header cap",g,new Vector3(1.37f,.026f,.17f),new Vector3(0,2.426f,.073f),.006f,walnutDark);
            Box("Brass crown header inlay",g,new Vector3(1.30f,.006f,.007f),new Vector3(0,2.386f,.146f),.001f,brass);
            Frame("Lower recessed walnut panel surround",g,.86f,.81f,.055f,.044f,new Vector3(0,.65f,.075f),walnut);
            Box("Deep inset lower door panel",g,new Vector3(.745f,.695f,.012f),new Vector3(0,.65f,.067f),.004f,walnutDark);
            Frame("Upper glazed door surround",g,.86f,1.03f,.045f,.040f,new Vector3(0,1.60f,.075f),walnut);
            Box("Frosted amber hallway glass",g,new Vector3(.768f,.936f,.012f),new Vector3(0,1.60f,.083f),.003f,frost);
            for(int i=0;i<5;i++)Box("Upper door brass grille upright "+i,g,new Vector3(.009f,.93f,.014f),new Vector3((i-2)*.151f,1.60f,.099f),.002f,brass);
            foreach(float y in new[]{1.32f,1.60f,1.88f})Box("Geometric grille crossbar",g,new Vector3(.765f,.009f,.014f),new Vector3(0,y,.099f),.002f,brass);
            foreach(float x in new[]{-.15f,.15f}){var diamond=Group("Art deco grille diamond",g,new Vector3(x,1.60f,.107f),Quaternion.Euler(0,0,45));Frame("Open diamond brass tracery",diamond,.21f,.21f,.010f,.009f,Vector3.zero,brass);}
            Box("150mm brass kickplate",g,new Vector3(.93f,.15f,.005f),new Vector3(0,.105f,.065f),.002f,brass);
            foreach(float y in new[]{.96f,1.28f}){Turn("Pull handle mounting rosette",g,Disc(.025f,.010f,.003f),brass,new Vector3(.33f,y,.077f),32).transform.localRotation=Quaternion.Euler(90,0,0);Tube("Pull handle standoff",g,new[]{new Vector3(.33f,y,.08f),new Vector3(.33f,y,.135f)},.016f,brass);}
            Tube("Heavy vertical brass pull",g,new[]{new Vector3(.33f,.96f,.135f),new Vector3(.33f,1.28f,.135f)},.026f,brass);
            foreach(float x in new[]{-.34f,.34f})Tube("Push bar standoff",g,new[]{new Vector3(x,.96f,.08f),new Vector3(x,.96f,.145f)},.020f,brass);
            Tube("Horizontal brass push bar",g,new[]{new Vector3(-.35f,.96f,.145f),new Vector3(.35f,.96f,.145f)},.030f,brass);
            foreach(float y in new[]{.38f,1.12f,1.98f})Box("Brass door hinge",g,new Vector3(.025f,.075f,.018f),new Vector3(-.515f,y,.070f),.004f,brass);
            Lamp("Subtle hall light through frosted amber",g,new Vector3(0,1.62f,.18f),Hex("FFCE98"),.22f,1.65f);
            Sconce("Brass Entry Wall Sconce",wall,new Vector3(3.22f-wall.localPosition.z,2.06f,.05f));CoatStand();
        }
        private static void CoatStand()
        {
            var g=Group("Entry_CoatStand",polish,new Vector3(2.88f,0,3.48f));
            Turn("Lathe turned walnut coat tree",g,new[]{P(0,0),P(.175f,0),P(.20f,.015f),P(.20f,.035f),P(.17f,.053f),P(.08f,.068f),P(.038f,.10f),P(.025f,.26f),P(.025f,1.39f),P(.038f,1.43f),P(.041f,1.47f),P(.026f,1.53f),P(.018f,1.69f),P(.035f,1.71f),P(.029f,1.74f),P(0,1.75f)},walnut,Vector3.zero,64);
            foreach(float h in new[]{.12f,1.39f,1.49f})Torus("Brass turned collar",g,.028f,.005f,new Vector3(0,h,0),brass);
            for(int i=0;i<6;i++){float a=i*60*Mathf.Deg2Rad;var axis=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));Tube("Curled brass coat hook "+i,g,Bezier(Vector3.up*1.48f+axis*.025f,Vector3.up*1.60f+axis*.23f,Vector3.up*1.75f+axis*.23f,Vector3.up*1.70f+axis*.16f),.016f,brass);Turn("Rounded hook finial "+i,g,Disc(.017f,.022f,.009f),walnut,axis*.16f+Vector3.up*1.69f,24);}
            Torus("Cylindrical brass umbrella retaining ring",g,.165f,.010f,new Vector3(0,.49f,0),brass);Turn("Umbrella drip tray",g,Disc(.174f,.018f,.005f),brassDark,new Vector3(0,.075f,0),64);
            for(int i=0;i<3;i++){float a=i*Mathf.PI*2/3;Tube("Umbrella ring support",g,new[]{new Vector3(Mathf.Cos(a)*.16f,.092f,Mathf.Sin(a)*.16f),new Vector3(Mathf.Cos(a)*.16f,.49f,Mathf.Sin(a)*.16f)},.008f,brass);}
            Collider(g,new Vector3(0,.875f,0),new Vector3(.48f,1.75f,.48f));
        }
        private static void Sconce(string name,Transform wall,Vector3 position)
        {
            var g=Group(name,wall,position);Frame("Stepped brass sconce wall plate",g,.095f,.255f,.018f,.025f,Vector3.zero,brassDark);Box("Sconce backing",g,new Vector3(.061f,.22f,.012f),Vector3.zero,.005f,brass);
            Tube("Curved brass sconce arm",g,Bezier(new Vector3(0,-.08f,.015f),new Vector3(0,-.12f,.13f),new Vector3(0,-.03f,.17f),new Vector3(0,-.035f,.17f)),.014f,brass);
            Turn("Frosted cylindrical sconce diffuser",g,Disc(.047f,.23f,.010f),frost,new Vector3(0,-.09f,.17f),48);
            foreach(float y in new[]{-.105f,.142f})Turn("Beaded brass sconce cap",g,Disc(.052f,.018f,.005f),brass,new Vector3(0,y,.17f),48);
            foreach(float side in new[]{-1f,1f}){var l=Lamp("Soft sconce "+(side>0?"uplight":"downlight"),g,new Vector3(0,side>0?.18f:-.14f,.19f),Hex("FFD7AA"),.48f,1.85f,true,95,55);l.transform.localRotation=Quaternion.LookRotation(new Vector3(0,side,-.12f));}
            Lamp("Sconce reflected warm fill",g,new Vector3(0,.02f,.23f),Hex("FFD7AA"),.14f,1.4f);
        }
        private static void RoomSconces()
        {
            var front=lounge.Find("ArchitecturalEnvelope/Courtyard lounge wall");foreach(float x in new[]{-2.40f,2.40f})Sconce("Upper wall brass sconce "+x,front,new Vector3(x,1.85f-floor,.065f));
            var left=lounge.Find("ArchitecturalEnvelope/Left wainscot wall");Sconce("Upper left wall brass sconce",left,new Vector3(left.localPosition.z-.65f,1.85f-floor,.065f));
            var right=lounge.Find("ArchitecturalEnvelope/Right wainscot wall");Sconce("Upper right wall brass sconce",right,new Vector3(.50f-right.localPosition.z,1.85f-floor,.065f));
        }
        private static void VinylStation()
        {
            var g=Group("Corner_VinylStation",polish,new Vector3(-2.54f,0,4.30f));
            foreach(float x in new[]{-.46f,.46f})foreach(float z in new[]{-.125f,.125f})Turn("Credenza tapered brass foot",g,new[]{P(0,0),P(.013f,0),P(.020f,.10f),P(.022f,.15f),P(0,.15f)},brass,new Vector3(x,0,z),32);
            Box("1100mm walnut credenza cabinet",g,new Vector3(1.1f,.61f,.38f),new Vector3(0,.435f,0),.010f,walnutDark);Box("Bullnose credenza crown",g,new Vector3(1.12f,.04f,.40f),new Vector3(0,.76f,0),.012f,walnut);
            foreach(float x in new[]{-.266f,.266f}){Frame("Framed credenza recessed door",g,.51f,.51f,.045f,.034f,new Vector3(x,.435f,-.198f),walnut).transform.localRotation=Quaternion.Euler(0,180,0);Box("Inset credenza walnut panel",g,new Vector3(.414f,.413f,.009f),new Vector3(x,.435f,-.195f),.004f,walnutDark);Turn("Turned brass cabinet knob",g,Disc(.012f,.025f,.004f),brass,new Vector3(x*.25f,.46f,-.205f),32).transform.localRotation=Quaternion.Euler(-90,0,0);}
            var gram=Group("Brass horn gramophone",g,new Vector3(-.18f,.78f,-.015f));Box("Beveled gramophone walnut plinth",gram,new Vector3(.39f,.10f,.29f),new Vector3(0,.05f,0),.010f,walnut);Box("Gramophone brass upper deck",gram,new Vector3(.365f,.008f,.266f),new Vector3(0,.103f,0),.003f,brassDark);
            Turn("Machined record platter",gram,Disc(.12f,.012f,.004f),brass,new Vector3(-.025f,.108f,-.02f),64);Turn("Black shellac record",gram,Disc(.117f,.003f,.001f),rubber,new Vector3(-.025f,.121f,-.02f),64);
            for(int i=0;i<9;i++)Torus("Shellac concentric record groove "+i,gram,.040f+i*.008f,.00035f,new Vector3(-.025f,.1245f,-.02f),iron);
            Turn("Cream record centre label",gram,Disc(.031f,.001f,.0003f),materials["PaperIvory"],new Vector3(-.025f,.125f,-.02f),48);Turn("Record spindle",gram,Disc(.003f,.012f,.001f),brass,new Vector3(-.025f,.123f,-.02f),24);
            Tube("Curved gramophone tonearm",gram,Bezier(new Vector3(.14f,.11f,.08f),new Vector3(.17f,.18f,.06f),new Vector3(.12f,.16f,-.07f),new Vector3(.04f,.14f,-.075f)),.012f,brass);Turn("Circular soundbox",gram,Disc(.023f,.012f,.003f),brass,new Vector3(.04f,.132f,-.075f),40).transform.localRotation=Quaternion.Euler(65,0,0);
            Tube("Swept horn gooseneck",gram,Bezier(new Vector3(.11f,.12f,.11f),new Vector3(.14f,.35f,.18f),new Vector3(.03f,.44f,.16f),new Vector3(-.035f,.46f,.07f)),.043f,brass);
            var horn=Group("Flared brass acoustic horn",gram,new Vector3(-.035f,.46f,.07f),Quaternion.Euler(-68,0,-12));
            Turn("Double wall trumpet horn",horn,new[]{P(.022f,0),P(.030f,.055f),P(.047f,.12f),P(.085f,.19f),P(.145f,.25f),P(.199f,.28f),P(.201f,.286f),P(.195f,.29f),P(.140f,.255f),P(.080f,.195f),P(.042f,.125f),P(.024f,.057f),P(.017f,0),P(.022f,0)},brass,Vector3.zero,96);Torus("Rolled gramophone horn lip",horn,.198f,.005f,new Vector3(0,.285f,0),brassDark);
            Tube("Gramophone winding crank",gram,new[]{new Vector3(.19f,.052f,0),new Vector3(.25f,.052f,0),new Vector3(.25f,.025f,0),new Vector3(.29f,.025f,0)},.009f,brass);Turn("Walnut crank grip",gram,Disc(.013f,.048f,.004f),walnut,new Vector3(.29f,.001f,0),32);
            foreach(int i in Enumerable.Range(0,4)){var sleeve=Group("Stacked vintage record sleeve "+i,g,new Vector3(.31f,.791f+i*.008f,-.015f),Quaternion.Euler(0,i*6-8,0));Box("Thick paper vinyl sleeve",sleeve,new Vector3(.205f,.006f,.205f),Vector3.zero,.0015f,i%2==0?burgundy:materials["PaperIvory"]);Torus("Embossed circular sleeve motif",sleeve,.057f,.001f,new Vector3(0,.0035f,0),brassDark);}
            var lamp=Group("Warm brass vinyl accent lamp",g,new Vector3(.34f,.824f,.035f));Turn("Accent lamp base and stem",lamp,new[]{P(0,0),P(.055f,0),P(.061f,.006f),P(.055f,.019f),P(.012f,.029f),P(.008f,.23f),P(.020f,.245f),P(0,.245f)},brass,Vector3.zero,48);Turn("Ivory pleated accent shade",lamp,new[]{P(.085f,.18f),P(.043f,.31f),P(.040f,.31f),P(.081f,.18f),P(.085f,.18f)},frost,Vector3.zero,48,.002f);Lamp("Vinyl warm lamp pool",lamp,new Vector3(0,.19f,0),Hex("FFD29D"),.42f,1.8f);
            Collider(g,new Vector3(0,.70f,-.02f),new Vector3(1.12f,1.4f,.48f));
        }
    }
}
