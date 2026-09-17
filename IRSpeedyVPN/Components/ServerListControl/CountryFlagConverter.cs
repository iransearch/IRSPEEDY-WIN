using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IRSpeedyVPN.Components.ServerListControl
{
    // Bundled flags only. Never download an image while scrolling or testing servers.
    public sealed class CountryFlagConverter : IValueConverter
    {
        private static readonly string[] Codes = "ad,ae,af,ag,ai,al,am,ao,aq,ar,as,at,au,aw,ax,az,ba,bb,bd,be,bf,bg,bh,bi,bj,bl,bm,bn,bo,bq,br,bs,bt,bv,bw,by,bz,ca,cc,cd,cf,cg,ch,ci,ck,cl,cm,cn,co,cp,cr,cu,cv,cw,cx,cy,cz,de,dg,dj,dk,dm,do,dz,ec,ee,eg,eh,er,es,et,eu,fi,fj,fk,fm,fo,fr,ga,gb,gd,ge,gf,gg,gh,gi,gl,gm,gn,gp,gq,gr,gs,gt,gu,gw,gy,hk,hm,hn,hr,ht,hu,ic,id,ie,il,im,in,io,iq,ir,is,it,je,jm,jo,jp,ke,kg,kh,ki,km,kn,kp,kr,kw,ky,kz,la,lb,lc,li,lk,lr,ls,lt,lu,lv,ly,ma,mc,md,me,mf,mg,mh,mk,ml,mm,mn,mo,mp,mq,mr,ms,mt,mu,mv,mw,mx,my,mz,na,nc,ne,nf,ng,ni,nl,no,np,nr,nu,nz,om,pa,pc,pe,pf,pg,ph,pk,pl,pm,pn,pr,ps,pt,pw,py,qa,re,ro,rs,ru,rw,sa,sb,sc,sd,se,sg,sh,si,sj,sk,sl,sm,sn,so,sr,ss,st,sv,sx,sy,sz,tc,td,tf,tg,th,tj,tk,tl,tm,tn,to,tr,tt,tv,tw,tz,ua,ug,um,un,us,uy,uz,va,vc,ve,vg,vi,vn,vu,wf,ws,xk,xx,ye,yt,za,zm,zw".Split(',');
        private static readonly Dictionary<string, ImageSource> Cache = new Dictionary<string, ImageSource>();
        public static ImageSource GetFlag(string code)
        {
            code = (code ?? "").Trim().ToLowerInvariant();
            if (code == "uk") code = "gb";
            if (code.Length != 2 || code[0] < 'a' || code[0] > 'z' || code[1] < 'a' || code[1] > 'z') return null;
            lock (Cache)
            {
                if (Cache.TryGetValue(code, out var cached)) return cached;
                try
                {
                    var index = Array.IndexOf(Codes, code);
                    if (index < 0) return null;
                    var atlas = new BitmapImage(new Uri("pack://application:,,,/IRSpeedyVPN;component/Resources/Flags.png"));
                    var bitmap = new CroppedBitmap(atlas, new System.Windows.Int32Rect(index % 16 * 48, index / 16 * 36, 48, 36));
                    bitmap.Freeze();
                    Cache[code] = bitmap;
                    return bitmap;
                }
                catch { Cache[code] = null; return null; }
            }
        }
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => GetFlag(value as string);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
