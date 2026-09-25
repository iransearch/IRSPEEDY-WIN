"""Portable ownership regression; real WPF lifecycle check is in run-wpf.ps1."""
from pathlib import Path
import subprocess, tempfile, sys
root=Path(__file__).resolve().parents[2]
s=(root/'IRSpeedyVPN/Components/ServerListControl/ServerCountryPicker.Motion.cs').read_text()
method=s[s.index('        private static TranslateTransform ProbeShift'):s.index('        private void UpdateProbeShine')]
code=r'''
using System; using System.Collections.Generic;
class Transform { public bool IsFrozen; }
class TranslateTransform:Transform { public double X=-20; }
class TransformGroup:Transform {
 public List<Transform> Children=new List<Transform>();
 public TransformGroup Clone(){var c=new TransformGroup();foreach(var t in Children)c.Children.Add(t is TranslateTransform x ? new TranslateTransform{X=x.X} : new Transform());return c;}
}
class FrameworkElement { public Transform RenderTransform; }
class Program {
'''+method+r'''
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
 static void Main(){
 var template=new TransformGroup{IsFrozen=true};template.Children.Add(new Transform{IsFrozen=true});template.Children.Add(new TranslateTransform{IsFrozen=true});
 var a=new FrameworkElement{RenderTransform=template};var b=new FrameworkElement{RenderTransform=template};
 var x=ProbeShift(a);var y=ProbeShift(b);
 Check(!x.IsFrozen&&!y.IsFrozen,"frozen template yields mutable animation targets");
 Check(!ReferenceEquals(x,y)&&!ReferenceEquals(a.RenderTransform,template),"rows own independent transform graphs");
 x.X=42;Check(y.X==-20&&((TranslateTransform)template.Children[1]).X==-20,"animating a row leaves template and other rows unchanged");
 Check(ReferenceEquals(x,ProbeShift(a)),"repeated updates reuse the mutable target");
 var parent=new TransformGroup();parent.Children.Add(new Transform());parent.Children.Add(new TranslateTransform{IsFrozen=true});
 Check(!ProbeShift(new FrameworkElement{RenderTransform=parent}).IsFrozen,"frozen child is cloned even with mutable parent");
 Check(ProbeShift(new FrameworkElement())==null&&ProbeShift(new FrameworkElement{RenderTransform=new TransformGroup()})==null,"early template initialization is safe");
 }
}
'''
with tempfile.TemporaryDirectory(prefix='probe-shine-') as d:
 p=Path(d);(p/'Program.cs').write_text(code)
 (p/'checks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion></PropertyGroup></Project>')
 subprocess.run([sys.argv[1] if len(sys.argv)>1 else 'dotnet','run','--project',d,'-v:q'],check=True)
