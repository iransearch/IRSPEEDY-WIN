"""Guard against the 1.4.15 cold starred-row XAML crash and shared effects.

The portable checks exercise the actual mutable-effect/stop helpers. Run the
Windows companion against a built app to instantiate its real BAML row template.
"""
from pathlib import Path
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[2]
folder = root / 'IRSpeedyVPN/Components/ServerListControl'
w = '{http://schemas.microsoft.com/winfx/2006/xaml/presentation}'
x = '{http://schemas.microsoft.com/winfx/2006/xaml}'
xml = ET.parse(folder / 'ServerCountryPicker.xaml').getroot()
row = next(e for e in xml.iter(w + 'DataTemplate') if e.get(x + 'Key') == 'ServerRowContent')
star = next(e for e in row.iter(w + 'Path') if e.find(w + 'Path.Effect/' + w + 'DropShadowEffect') is not None)
assert not any('Effect' in e.get('Storyboard.TargetProperty', '') for e in star.iter()), 'Star style starts an Effect property-path animation before the template sets Effect'
for event, handler in [('Loaded', 'StarGlow_Loaded'), ('Unloaded', 'StarGlow_Unloaded'), ('IsVisibleChanged', 'StarGlow_VisibilityChanged')]:
    assert star.get(event) == handler, (event, 'missing safe lifecycle hook')
effect = star.find(w + 'Path.Effect/' + w + 'DropShadowEffect')
assert effect.attrib == {'Color': '#FFFFCC55', 'BlurRadius': '6', 'ShadowDepth': '0', 'Opacity': '0.65'}
print('PASS starred-row template retains its artwork and starts no early Effect storyboard')

source = (folder / 'ServerCountryPicker.StarGlow.cs').read_text()
start = source.index('        private static DropShadowEffect MutableStarGlow(')
end = source.index('        private void UpdateStarGlow(', start)
helpers = source[start:end]
code = r'''
using System;
class Effect { public bool IsFrozen; }
class DropShadowEffect:Effect {
 public double Opacity=.65,BlurRadius=6;public bool OpacityAnimated,BlurAnimated;
 public static object OpacityProperty=new object(),BlurRadiusProperty=new object();
 public DropShadowEffect Clone()=>new DropShadowEffect{Opacity=Opacity,BlurRadius=BlurRadius};
 public void BeginAnimation(object property,object animation){
  if(IsFrozen)throw new Exception("mutation of shared/frozen effect");
  if(property==OpacityProperty)OpacityAnimated=animation!=null;else BlurAnimated=animation!=null;
 }
}
class FrameworkElement { public Effect Effect; }
class Program {
''' + helpers + r'''
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static void Main(){
  var shared=new DropShadowEffect{IsFrozen=true};
  var a=new FrameworkElement{Effect=shared};var b=new FrameworkElement{Effect=shared};
  StopStarGlow(a);Check(ReferenceEquals(a.Effect,shared),"early stop does not mutate a frozen template effect");
  var x=MutableStarGlow(a);var y=MutableStarGlow(b);
  Check(x!=null&&y!=null&&!x.IsFrozen&&!y.IsFrozen&&!ReferenceEquals(x,y),"old/new row presenters own independent mutable glow effects");
  Check(ReferenceEquals(x,MutableStarGlow(a)),"repeated visibility updates reuse the owned effect");
  x.BlurRadius=11;Check(y.BlurRadius==6&&shared.BlurRadius==6,"pulsing one row leaves the template and other row unchanged");
  x.BeginAnimation(DropShadowEffect.OpacityProperty,new object());x.BeginAnimation(DropShadowEffect.BlurRadiusProperty,new object());
  StopStarGlow(a);Check(!x.OpacityAnimated&&!x.BlurAnimated,"hide/unload removes both animation clocks");
  Check(x.Opacity==.65&&y.Opacity==.65,"idle gold glow values are retained");
  var empty=new FrameworkElement();StopStarGlow(empty);
  Check(MutableStarGlow(empty)==null,"visibility events before Effect assignment are harmless");
  var other=new FrameworkElement{Effect=new Effect()};StopStarGlow(other);
  Check(MutableStarGlow(other)==null,"a replaced effect is not overwritten or animated");
 }
}
'''
with tempfile.TemporaryDirectory(prefix='server-star-glow-') as directory:
    path = Path(directory)
    (path / 'Program.cs').write_text(code)
    (path / 'Checks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion></PropertyGroup></Project>')
    subprocess.run([sys.argv[1] if len(sys.argv) > 1 else 'dotnet', 'run', '--project', directory, '-v:q'], check=True)
