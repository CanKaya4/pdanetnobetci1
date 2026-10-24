"""Rebuild the original TetherMax vector/raster brand assets (requires Pillow)."""
from pathlib import Path
from PIL import Image, ImageDraw
root=Path(__file__).resolve().parent.parent
assets=root/'windows/TetherMax.Desktop/Assets';assets.mkdir(exist_ok=True)
paths='M64,76H192M128,76V184L94,150M128,184L164,150M177,113L195,131L222,95'
svg='<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256"><rect width="256" height="256" rx="58" fill="#0c1424"/><path d="'+paths+'" fill="none" stroke="#55dce7" stroke-width="18" stroke-linecap="round" stroke-linejoin="round"/></svg>'
(assets/'tethermax.svg').write_text(svg,encoding='utf-8')
scale=4
im=Image.new('RGBA',(256*scale,256*scale),'#0c1424');draw=ImageDraw.Draw(im)
for points in [[(64,76),(192,76)],[(128,76),(128,184),(94,150)],[(128,184),(164,150)],[(177,113),(195,131),(222,95)]]:
 xy=[(x*scale,y*scale) for x,y in points];draw.line(xy,fill='#55dce7',width=18*scale,joint='curve')
 for x,y in xy:draw.ellipse((x-9*scale,y-9*scale,x+9*scale,y+9*scale),fill='#55dce7')
im=im.resize((256,256),Image.Resampling.LANCZOS)
im.save(assets/'tethermax.png');im.save(assets/'tethermax.ico',sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
for name,color,mark in [('connected','#51e3ac','+'),('waiting','#55dce7','-'),('error','#ff7b89','!')]:
 variant=im.copy();v=ImageDraw.Draw(variant);v.ellipse((178,178,248,248),fill=color,outline='#0c1424',width=8);v.text((204,194),mark,fill='#0c1424',stroke_width=2);variant.save(assets/(name+'.ico'),sizes=[(16,16),(32,32),(48,48)])
res=root/'android/res'
for density,size in [('mdpi',48),('hdpi',72),('xhdpi',96),('xxhdpi',144),('xxxhdpi',192)]:
 target=res/('mipmap-'+density);target.mkdir(exist_ok=True);im.resize((size,size),Image.Resampling.LANCZOS).save(target/'ic_launcher.png')
head='<vector xmlns:android="http://schemas.android.com/apk/res/android" android:width="108dp" android:height="108dp" android:viewportWidth="256" android:viewportHeight="256">'
def stroke(color):return '<path android:strokeColor="'+color+'" android:strokeWidth="18" android:strokeLineCap="round" android:strokeLineJoin="round" android:fillColor="#00000000" android:pathData="'+paths+'"/>'
(res/'drawable/tethermax_foreground.xml').write_text(head+'<group android:pivotX="128" android:pivotY="128" android:scaleX="0.72" android:scaleY="0.72">'+stroke('#55DCE7')+'</group></vector>')
(res/'drawable/tethermax_notification.xml').write_text(head+stroke('#FFFFFF')+'</vector>')
(res/'values/colors.xml').write_text('<resources><color name="tethermax_background">#0C1424</color></resources>')
(res/'mipmap-anydpi-v26/ic_launcher.xml').write_text('<adaptive-icon xmlns:android="http://schemas.android.com/apk/res/android"><background android:drawable="@color/tethermax_background"/><foreground android:drawable="@drawable/tethermax_foreground"/></adaptive-icon>')
