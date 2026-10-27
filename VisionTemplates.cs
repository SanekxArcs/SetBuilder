using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace TunerFest;
public record NamePattern(string Name,double Aspect,byte[] Bits);
public sealed class VisionTemplates
{
    public List<NamePattern> Patterns {get;set;}=[];
    public int SchemaVersion {get;set;}=1;
    public static readonly Lazy<VisionTemplates> Shared=new(()=>Load(Path.Combine(AppContext.BaseDirectory,"Assets","recognition-templates.json")));
    public static VisionTemplates Load(string path){try{return JsonSerializer.Deserialize<VisionTemplates>(File.ReadAllText(path))??new();}catch{return new();}}
    public void Save(string path){Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,JsonSerializer.Serialize(this));}
    public static NamePattern? Extract(Bitmap frame,Rectangle card,string name="")
    {
        var area=Rectangle.Intersect(new Rectangle(card.X+card.Width/30,card.Y+card.Height*18/100,card.Width*14/15,card.Height*34/100),new Rectangle(Point.Empty,frame.Size));
        if(area.Width<16||area.Height<6)return null;
        using var crop=frame.Clone(area,PixelFormat.Format32bppArgb);
        var locked=crop.LockBits(new Rectangle(Point.Empty,crop.Size),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        byte[] bytes=new byte[Math.Abs(locked.Stride)*crop.Height];try{Marshal.Copy(locked.Scan0,bytes,0,bytes.Length);}finally{crop.UnlockBits(locked);}
        int stride=bytes.Length/crop.Height,left=crop.Width,right=-1,top=crop.Height,bottom=-1,dark=0,white=0;
        var mask=new bool[crop.Width*crop.Height];
        for(int y=0;y<crop.Height;y++)for(int x=0;x<crop.Width;x++)
        {
            int i=y*stride+x*4,b=bytes[i],g=bytes[i+1],r=bytes[i+2];if(r<65&&g<65&&b<65)dark++;
            if(r>=160&&g>=160&&b>=160&&Math.Max(r,Math.Max(g,b))-Math.Min(r,Math.Min(g,b))<45)
            {mask[y*crop.Width+x]=true;white++;left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);}
        }
        if(dark<crop.Width*crop.Height*.70||white<25||right-left<10||bottom-top<5)return null;
        int width=right-left+1,height=bottom-top+1;if(height>crop.Height*.85)return null;
        byte[] bits=new byte[256*32/8];
        for(int y=0;y<32;y++)for(int x=0;x<256;x++)
        {
            int sx=left+Math.Min(width-1,(int)((x+.5)*width/256)),sy=top+Math.Min(height-1,(int)((y+.5)*height/32));
            if(mask[sy*crop.Width+sx]){int bit=y*256+x;bits[bit>>3]|=(byte)(1<<(bit&7));}
        }
        return new(name,(double)width/height,bits);
    }
    static double Similarity(NamePattern a,NamePattern b)
    {
        if(Math.Abs(a.Aspect/b.Aspect-1)>.075)return 0;
        int intersection=0,union=0;
        for(int i=0;i<a.Bits.Length;i++){intersection+=System.Numerics.BitOperations.PopCount((uint)(a.Bits[i]&b.Bits[i]));union+=System.Numerics.BitOperations.PopCount((uint)(a.Bits[i]|b.Bits[i]));}
        return union>0?(double)intersection/union:0;
    }
    public bool Learn(Bitmap frame,Rectangle card,string name)
    {
        var sample=Extract(frame,card,name);if(sample==null)return false;
        var same=Patterns.Where(p=>p.Name==name).ToList();if(same.Count>=8||same.Any(p=>Similarity(sample,p)>.97))return false;
        Patterns.Add(sample);return true;
    }
    public bool Match(Bitmap frame,Rectangle card,out string name,out double confidence)
    {
        name="";confidence=0;var sample=Extract(frame,card);if(sample==null)return false;
        var scores=Patterns.Select(p=>(p.Name,Score:Similarity(sample,p))).GroupBy(p=>p.Name).Select(g=>(Name:g.Key,Score:g.Max(p=>p.Score))).OrderByDescending(p=>p.Score).Take(2).ToArray();
        if(scores.Length==0)return false;confidence=scores[0].Score;
        if(confidence<.86||(scores.Length>1&&confidence-scores[1].Score<.10))return false;
        name=scores[0].Name;return true;
    }
}
