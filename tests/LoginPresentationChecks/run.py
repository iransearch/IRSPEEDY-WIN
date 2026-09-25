"""Exercise the production stage queue with a dispatcher synchronization context."""
from pathlib import Path
import subprocess, sys, tempfile
root = Path(__file__).resolve().parents[2]
source = (root/'IRSpeedyVPN/UserControls/UCLoginLoading.xaml.cs').read_text()
methods = source[source.index('        private Task stageQueue'):source.index('        private void VisibilityChanged')]
harness = r'''
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
enum DispatcherPriority { Background }
class TestDispatcher { public async Task InvokeAsync(Action action, DispatcherPriority priority) { await Task.Yield(); action(); } }
class Step { public int State; public void SetState(int state) { State=state; } }
class Loader {
 public TestDispatcher Dispatcher = new TestDispatcher();
 public Step[] Steps = {new Step(),new Step(),new Step()};
'''
tests = r'''
}
class UiContext : SynchronizationContext {
 readonly BlockingCollection<Action> queue=new BlockingCollection<Action>();
 public override void Post(SendOrPostCallback cb,object state){queue.Add(()=>cb(state));}
 public void Run(Func<Task> action){SetSynchronizationContext(this);var task=action();while(!task.IsCompleted){if(queue.TryTake(out var next,20))next();}task.GetAwaiter().GetResult();}
}
class Program {
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static void Main(){new UiContext().Run(Run);}
 static async Task Run(){
 var l=new Loader();l.SetStage(0);l.SetStage(1);l.SetStage(2);
 Check(l.Steps[0].State==1,"fast results do not skip account step");
 var finish=l.FinishStagesAsync();var seen=new HashSet<int>();int heartbeats=0;
 while(!finish.IsCompleted){for(int i=0;i<3;i++)if(l.Steps[i].State==1)seen.Add(i);heartbeats++;await Task.Delay(10);}
 await finish;
 Check(seen.Count==3,"all three real stages are visible before dismissal");
 Check(heartbeats>20,"dispatcher keeps processing while stages wait");
 l.SetStage(1);Check(l.Steps[2].State==1,"late progress cannot move backwards");
 l.SetStage(0);l.SetStage(2);await Task.Delay(30);l.SetStage(0);await Task.Delay(800);
 Check(l.Steps[0].State==1&&l.Steps[1].State==0&&l.Steps[2].State==0,"retry invalidates pending previous stages");
 await l.FinishStagesAsync();Check(l.Steps[0].State==1,"failed authentication does not invent successful steps");
 }
}
'''
with tempfile.TemporaryDirectory(prefix='login-presentation-') as d:
 p=Path(d);(p/'Program.cs').write_text(harness+methods+tests)
 (p/'checks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion></PropertyGroup></Project>')
 subprocess.run([sys.argv[1] if len(sys.argv)>1 else 'dotnet','run','--project',d,'-v:q'],check=True)
