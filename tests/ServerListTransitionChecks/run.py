"""Production slot planner/timeline; WPF value types are inert Linux doubles."""
from pathlib import Path
import subprocess,sys,tempfile
root=Path(__file__).resolve().parents[2]
fixture=r'''
using System;
using System.Collections.Generic;
using System.Linq;
using IRSpeedyVPN.Components.ServerListControl;
using IRSpeedyVPN.Interfaces;
namespace System.Windows.Input {
 public class Cursor {} public static class Cursors { public static Cursor Hand=new Cursor(),Arrow=new Cursor(); }
}
namespace System.Windows.Media {
 public class ImageSource {} public class Brush {}
 public static class VisualTreeHelper {
  public static int GetChildrenCount(System.Windows.DependencyObject element)=>element.Children.Count;
  public static System.Windows.DependencyObject GetChild(System.Windows.DependencyObject element,int index)=>element.Children[index];
 }
}
namespace System.Windows {
 public class PropertyMetadata { public object DefaultValue; public PropertyMetadata(object value){DefaultValue=value;} }
 public class DependencyProperty {
  public Type PropertyType; public PropertyMetadata DefaultMetadata;
  public static DependencyProperty RegisterAttached(string name,Type type,Type owner,PropertyMetadata metadata){
   if(!type.IsInstanceOfType(metadata.DefaultValue))throw new Exception("Wrong metadata type");
   return new DependencyProperty{PropertyType=type,DefaultMetadata=metadata};
  }
 }
 public class DependencyObject {
  public readonly List<DependencyObject> Children=new List<DependencyObject>();
  readonly Dictionary<DependencyProperty,object> values=new Dictionary<DependencyProperty,object>();
  public object GetValue(DependencyProperty property){object value;return values.TryGetValue(property,out value)?value:property.DefaultMetadata.DefaultValue;}
  public void SetValue(DependencyProperty property,object value){
   if(!property.PropertyType.IsInstanceOfType(value))throw new Exception("Wrong attached value type");values[property]=value;
  }
 }
 public class FrameworkElement:DependencyObject {}
}
namespace IRSpeedyVPN.Interfaces { public interface IVPNService { int ID {get;} } }
namespace IRSpeedyVPN.Common {
 static class LastSuccessfulServer { internal static string Key(IVPNService s)=>s==null?"":s.GetType().FullName+":"+s.ID; }
}
namespace IRSpeedyVPN.Components.ServerListControl {
 class GroupItem {
  public IVPNService Service; public string CountryName,Initials,SignalText,SignalToolTip="";
  public System.Windows.Media.ImageSource FlagSource;
  public System.Windows.Media.Brush SignalBrush;
  public bool IsSelectable=true,IsChecking,IsSelectedCountry,IsLastSuccessfulConnection;
  public long SortKey;
 }
}
// Legacy planner cases explicitly publish completed identities. Strict-cycle
// cases below call the production Publish API without this convenience adapter.
static class CompletedFixture {
 public static void PublishForTest(this ServerListViewModel vm,IEnumerable<ServerItem> source,bool animate,bool allowReorder=true){
  var values=source.ToArray();bool initial=vm.Servers.Count!=values.Length||!vm.Servers.Select(item=>item.Identity).OrderBy(id=>id).SequenceEqual(values.Select(item=>item.Identity).OrderBy(id=>id));
  foreach(var item in values)vm.Publish(values,true,false,item.Identity);
  if(!initial||values.Length==0)vm.Publish(values,animate,allowReorder);
 }
}
class Service:IVPNService { public int ID {get;set;} }
class Program {
 static GroupItem G(int id,bool selected=false)=>new GroupItem{Service=new Service{ID=id},CountryName="کشور "+id,Initials="ک",SignalText=id+" ms",SortKey=id,IsSelectedCountry=selected};
 static ServerItem S(GroupItem g)=>new ServerItem(g);
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static void Unique(ServerListViewModel vm){
  var expected=vm.Servers.Select(item=>item.Identity).OrderBy(id=>id).ToArray();
  for(int p=0;p<5;p++) {
   var visible=vm.Slots.Select(slot=>slot.GetPart((ServerRowPart)p)).ToArray();
   if(visible.Any(item=>item==null)||vm.Slots.Any(slot=>slot.IsVacant))throw new Exception("empty source slot during exchange");
   if(!visible.Select(item=>item.Identity).OrderBy(id=>id).SequenceEqual(expected))throw new Exception("duplicate/lost identity within a displayed group");
  }
 }
 static int Drain(ServerListViewModel vm){
  int n=0;ServerSortPass pass;
  while((pass=vm.TryBeginPass())!=null){if(n++>vm.Servers.Count*4+20)throw new Exception("queue failed to converge");for(int i=0;i<5;i++){vm.Reveal(pass,(ServerRowPart)i);Unique(vm);}vm.Finish(pass);Unique(vm);}
  if(!vm.Slots.Select(s=>s.DisplayedServer?.Identity).SequenceEqual(vm.Servers.Select(s=>s.Identity)))throw new Exception("lost/stale identity after drain");return n;
 }
 static void CheckDestination(int index,int id,int[] order){if(order[index]!=id)throw new Exception("non-final destination");}
 static IEnumerable<int[]> Permutations(int[] order,int start){
  if(start==order.Length){yield return (int[])order.Clone();yield break;}
  for(int i=start;i<order.Length;i++) {
   int value=order[start];order[start]=order[i];order[i]=value;
   foreach(var item in Permutations(order,start+1))yield return item;
   value=order[start];order[start]=order[i];order[i]=value;
  }
 }
 static void Main(){
  // BAML defers enum conversion until the country-row template is loaded.
  // Reproduce SmartAssembly field renaming and exercise the same converter
  // against the actual XAML values, independently of WPF/desktop availability.
  var tokens=System.Xml.Linq.XElement.Load(@"__PICKER_XAML__").DescendantsAndSelf().SelectMany(rowElement=>rowElement.Attributes())
   .Where(rowAttribute=>rowAttribute.Name.LocalName=="ServerCometPart.Part").Select(rowAttribute=>rowAttribute.Value).ToArray();
  var renamedAssembly=System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
   new System.Reflection.AssemblyName("RenamedRowParts"),System.Reflection.Emit.AssemblyBuilderAccess.Run);
  var renamedEnum=renamedAssembly.DefineDynamicModule("Parts").DefineEnum("HiddenRowPart",System.Reflection.TypeAttributes.Public,typeof(int));
  foreach(ServerRowPart part in Enum.GetValues(typeof(ServerRowPart)))renamedEnum.DefineLiteral("renamed_"+((int)part+1),(int)part);
  var converter=new System.ComponentModel.EnumConverter(renamedEnum.CreateTypeInfo().AsType());
  bool originalNameFailed=false;
  try{converter.ConvertFromInvariantString("Selection");}catch(FormatException){originalNameFailed=true;}
  Check(originalNameFailed,"reproduced Selection EnumConverter failure after enum member renaming");
  Check(ServerCometPart.PartProperty.PropertyType==typeof(int),"XAML numeric row-part values target an integer DP; numeric enum markup would fail MC3032");
  Check(typeof(ServerCometPart).GetMethod("SetPart").GetParameters()[1].ParameterType==typeof(int)
   &&typeof(ServerCometPart).GetMethod("GetPart").ReturnType==typeof(int),"attached setter/getter and DP metadata consistently expose int to the markup compiler");
  var partConverter=System.ComponentModel.TypeDescriptor.GetConverter(ServerCometPart.PartProperty.PropertyType);
  Check(!(partConverter is System.ComponentModel.EnumConverter),"row XAML uses integer conversion without enum name lookup");
  var dependency=new System.Windows.DependencyObject();
  Check(Convert.ToInt32(ServerCometPart.GetPart(dependency))==-1,"unmarked visual elements retain the no-part default");
  var converted=new List<int>();
  foreach(var token in tokens){
   var value=partConverter.ConvertFromInvariantString(token);
   typeof(ServerCometPart).GetMethod("SetPart").Invoke(null,new object[]{dependency,value});
   Check(Convert.ToInt32(ServerCometPart.GetPart(dependency))==Convert.ToInt32(value),"actual attached property round-trips XAML part ID "+token);
   converted.Add(Convert.ToInt32(value));
  }
  Check(converted.OrderBy(p=>p).SequenceEqual(Enumerable.Range(0,5)),"all five actual XAML part values use stable integer IDs independent of enum names");
  var visualRoot=new System.Windows.DependencyObject();
  foreach(var id in converted){var visual=new System.Windows.FrameworkElement();ServerCometPart.SetPart(visual,id);visualRoot.Children.Add(visual);}
  var invalidVisual=new System.Windows.FrameworkElement();ServerCometPart.SetPart(invalidVisual,99);visualRoot.Children.Add(invalidVisual);
  var foundGroups=ParsedGroupFinder.Find(visualRoot);
  Check(converted.Select((id,index)=>ReferenceEquals(foundGroups[id],visualRoot.Children[index])).All(valid=>valid),
   "production FindGroups discovers all five integer-marked groups and skips unmarked/out-of-range nodes");
  Check((int)ServerRowPart.None==-1&&(int)ServerRowPart.Signal==0&&(int)ServerRowPart.Badge==1
   &&(int)ServerRowPart.Name==2&&(int)ServerRowPart.Flag==3&&(int)ServerRowPart.Selection==4,"stable row-part IDs match the slot array and XAML contract");
  // Exercise the production displayed-slot planner, including in-flight work.
  var pa=G(71,true);var pb=G(72);var pc=G(73);var pinned=new ServerListViewModel();
  pinned.Publish(new[]{S(pa),S(pb),S(pc)},true);pinned.BeginCheckCycle();
  int pinNotices=0;foreach(var slot in pinned.Slots)slot.PropertyChanged+=(sender,e)=>{Unique(pinned);pinNotices++;};
  pb.IsLastSuccessfulConnection=true;
  pinned.Publish(new[]{S(pb),S(pa),S(pc)},true);
  Check(pinned.Slots[0].DisplayedServer.Service.ID==72&&pinned.Slots[0].BadgeServer.IsLastSuccessfulConnection,
    "star pins immediately above unfinished countries without completion eligibility");
  Check(pinNotices>0&&pinned.Slots[1].SelectionServer.IsSelectedCountry,
    "pin promotion is atomic and preserves selected identity");
  pb.IsChecking=true;pinned.MarkChecking(S(pb).Identity);pinned.BeginCheckCycle();
  pinned.Publish(new[]{S(pb),S(pc),S(pa)},true,false);
  Check(pinned.Slots[0].DisplayedServer.Service.ID==72&&pinned.TryBeginPass()==null,
    "cycle reset and an in-progress star keep first place without a sweep");
  pinned.Publish(new[]{S(pb),S(pc),S(pa)},true,true,S(pa).Identity);
  pinned.Publish(new[]{S(pb),S(pc),S(pa)},true,true,S(pc).Identity);
  var belowPin=pinned.TryBeginPass();
  Check(belowPin!=null&&belowPin.Rows.All(row=>row.Index>0),
    "completed ordinary countries can reorder without displacing a checking star");
  pinned.Reveal(belowPin,ServerRowPart.Signal);Unique(pinned);
  pb.IsLastSuccessfulConnection=false;pa.IsLastSuccessfulConnection=true;
  pinned.Publish(new[]{S(pa),S(pc),S(pb)},true);
  Check(pinned.Slots[0].DisplayedServer.Service.ID==71&&pinned.Slots[0].SelectionServer.IsSelectedCountry,
    "changing the pin during a partial reveal immediately puts the new star first");
  pinned.Finish(belowPin);pinned.Reveal(belowPin,ServerRowPart.Name);Unique(pinned);
  Check(pinned.Slots[0].DisplayedServer.Service.ID==71&&!pinned.IsTransitioning,
    "canceled old animation cannot restore the previous pinned country");
  pa.IsChecking=true;pinned.MarkChecking(S(pa).Identity);
  pinned.Publish(new[]{S(pa),S(pc),S(pb)},false);pinned.CompleteImmediately();
  Check(pinned.Slots[0].DisplayedServer.Service.ID==71,
    "reduced motion and lifecycle cleanup retain the star independently of testing");
  var repair=new ServerListViewModel();repair.Publish(new[]{S(pa),S(pc),S(pb)},true);
  repair.Slots[0].SetInstant(S(pc));repair.Slots[1].SetInstant(S(pa));
  repair.Publish(new[]{S(pa),S(pc),S(pb)},true);var fixPin=repair.TryBeginPass();
  Check(fixPin!=null&&fixPin.Rows[0].Target.Service.ID==71,
    "already-misordered displayed slots repair even when the pinned identity has not changed");
  repair.Finish(fixPin);Unique(repair);
  Check(repair.Slots[0].DisplayedServer.IsLastSuccessfulConnection,
    "repaired star is first even while checking and every other country is pending");
  repair.Publish(new[]{S(pc),S(pb)},true);Unique(repair);
  Check(repair.Slots.Count==2&&repair.Slots.All(slot=>!slot.BadgeServer.IsLastSuccessfulConnection),
    "removing the pinned country leaves no phantom star or missing identity");
  var strict=new ServerListViewModel();var sg=Enumerable.Range(1,4).Select(id=>G(id,id==1)).ToArray();
  var original=sg.Select(S).ToArray();strict.Publish(original,true);strict.BeginCheckCycle();
  var reversed=original.Reverse().ToArray();strict.Publish(reversed,true);
  Check(strict.TryBeginPass()==null,"zero completed countries cannot move even if canonical order changes");
  strict.Publish(reversed,true,true,original[0].Identity);
  Check(strict.TryBeginPass()==null,"one completed country cannot swap with a pending partner");
  strict.CompleteImmediately();
  Check(strict.Slots.Select(slot=>slot.DisplayedServer.Service.ID).SequenceEqual(new[]{1,2,3,4}),
    "instant/reduced-motion cleanup also pins every unfinished country");
  strict.Publish(reversed,true,true,original[3].Identity);var eligible=strict.TryBeginPass();
  Check(eligible!=null&&eligible.Rows.Length==2&&eligible.Rows.All(row=>row.From.Service.ID==1||row.From.Service.ID==4),
    "only two explicitly completed countries form the active pair");
  for(int part=0;part<5;part++){strict.Reveal(eligible,(ServerRowPart)part);Unique(strict);
   Check(strict.Slots[1].GetPart((ServerRowPart)part).Service.ID==2&&strict.Slots[2].GetPart((ServerRowPart)part).Service.ID==3,
     "not-started countries stay fixed during paired reveal part "+part);}
  strict.Finish(eligible);Check(strict.TryBeginPass()==null,"pending middle countries receive no follow-up swap");
  strict.BeginCheckCycle();strict.Publish(original,true);
  Check(strict.TryBeginPass()==null,"new cycle forgets all previous completion eligibility");
  sg[0].IsChecking=true;strict.Publish(sg.Select(S).Reverse(),true,true,original[0].Identity);
  strict.Publish(sg.Select(S).Reverse(),true,true,original[3].Identity);
  Check(strict.TryBeginPass()==null,"a still-checking country cannot become a partner from completion metadata alone");
  sg[0].IsChecking=false;strict.MarkChecking(original[0].Identity);
  strict.Publish(sg.Select(S).Reverse(),true,false);
  Check(strict.TryBeginPass()==null,"stopping/canceling checking cannot restore earlier completion eligibility");
  var incremental=new ServerListViewModel();var six=Enumerable.Range(1,6).Select(id=>S(G(id))).ToArray();
  incremental.Publish(six,true);incremental.BeginCheckCycle();
  var subset=new[]{six[2],six[4],six[0],six[1],six[3],six[5]};
  foreach(int id in new[]{1,3,5})incremental.Publish(subset,true,true,six[id-1].Identity);
  var triple=incremental.TryBeginPass();
  Check(triple.Rows.Length==3&&triple.Rows.Select(row=>row.Index).SequenceEqual(new[]{0,2,4}),
    "three completed countries update immediately while three unfinished countries keep their slots");
  var latest=new[]{six[5],six[2],six[4],six[0],six[1],six[3]};
  incremental.Publish(latest,true,true,six[5].Identity);
  Check(incremental.TryBeginPass()==null&&triple.Rows[0].Target.Service.ID==3,
    "new completion cannot restart or overwrite the active multi-row target");
  for(int part=0;part<5;part++){
   incremental.Reveal(triple,(ServerRowPart)part);Unique(incremental);
   Check(incremental.Slots[1].GetPart((ServerRowPart)part).Service.ID==2&&incremental.Slots[3].GetPart((ServerRowPart)part).Service.ID==4,
    "unfinished slots remain unchanged during unlimited reveal part "+part);
  }
  incremental.Finish(triple);var newest=incremental.TryBeginPass();
  Check(newest.Rows.Length==4&&newest.Rows.Select(row=>row.Target.Service.ID).SequenceEqual(new[]{6,3,5,1}),
    "next boundary consumes newest completed order with four lights instead of pair swaps");
  incremental.Finish(newest);Unique(incremental);
  Check(incremental.TryBeginPass()==null&&incremental.Slots.Select(slot=>slot.DisplayedServer.Service.ID).SequenceEqual(new[]{6,2,3,4,5,1}),
    "completed subset settles directly without waiting for pending countries");
  var a=G(1,true);var b=G(2);var c=G(3);var vm=new ServerListViewModel();int work=0;vm.WorkAvailable+=()=>work++;
  vm.PublishForTest(new[]{S(a),S(b),S(c)},true);var stable=vm.Slots.ToArray();
  Check(!vm.IsTransitioning,"initial load has no light");var before=vm.Slots.Select(s=>s.DisplayedServer.Identity).ToArray();vm.CompleteImmediately();work=0;
  a.IsChecking=b.IsChecking=true;a.SignalText=b.SignalText="—";vm.PublishForTest(new[]{S(c),S(b),S(a)},true,false);
  Check(work==0&&!vm.IsTransitioning&&vm.Slots.Select(s=>s.DisplayedServer.Identity).SequenceEqual(before),"check again/reset preserves visible order with no light");
  vm.PublishForTest(new[]{S(c),S(b),S(a)},false,false);
  Check(vm.Slots.Select(s=>s.DisplayedServer.Identity).SequenceEqual(before),"reduced-motion reset also preserves visible order");
  Check(vm.Slots[0].DisplayedServer.IsChecking&&vm.Slots[0].SignalServer.SignalText=="—","pending indicators/data update normally");
  a.IsChecking=false;b.IsChecking=false;b.SignalText="80 ms";vm.PublishForTest(new[]{S(b),S(a),S(c)},true);var pass=vm.TryBeginPass();var frozen=pass.Target;
  Check(pass.Index==0&&pass.Target.Service.ID==2&&!a.IsChecking,"completed countries sort immediately without waiting for the full list");
  Check(vm.Servers[0].Service.ID==2&&vm.Slots[0].NameServer.Service.ID==1,"sorted truth is immediate but displayed country remains old until reveal");
  Check(vm.TryBeginPass()==null&&pass.Rows.Length==2&&pass.Rows[1].Index==1,"one active exchange freezes both affected rows");
  int notifications=0;foreach(var slot in vm.Slots)slot.PropertyChanged+=(sender,e)=>{Unique(vm);notifications++;};
  vm.Reveal(pass,ServerRowPart.Signal);Unique(vm);
  Check(!vm.Slots[1].IsVacant&&vm.Slots[0].NameServer.Service.ID==1&&vm.Slots[1].NameServer.Service.ID==2
    &&vm.Slots[0].SignalServer.Service.ID==2&&vm.Slots[1].SignalServer.Service.ID==1,"paired group exchanges leave both cards populated while names retain old data");
  Check(notifications>0,"every binding notification observes an already atomic paired group");
  vm.PublishForTest(new[]{S(c),S(b),S(a)},true,false);Unique(vm);Check(vm.Slots[1].SignalServer.Service.ID==1,"progress cannot overwrite either half of the active pair");
  for(int i=0;i<200;i++){b.SignalText=i+" ms";vm.PublishForTest(new[]{S(c),S(b),S(a)},true);}
  Check(ReferenceEquals(pass.Target,frozen)&&vm.Slots[0].SignalServer.SignalText=="80 ms"&&pass.Rows[1].Target.Service.ID==1,"200 completions coalesce while both active targets stay frozen");vm.Finish(pass);Unique(vm);
  var next=vm.TryBeginPass();Check(next.Index==0&&next.Target.Service.ID==3,"next boundary replans first current mismatch against newest order");vm.Finish(next);Unique(vm);Drain(vm);
  Check(vm.Slots[1].SignalServer.SignalText=="199 ms"&&vm.Slots[2].SelectionServer.IsSelectedCountry&&a.IsSelectedCountry,"latest ping and selection follow stable identity");
  Check(vm.Slots.SequenceEqual(stable),"visual slots are never reordered/replaced");
  vm.PublishForTest(vm.Servers.ToArray(),true);Check(vm.TryBeginPass()==null,"unchanged positions consume no pass time");
  a.SignalText="—";a.IsSelectable=false;vm.PublishForTest(new[]{S(c),S(b),S(a)},true,false);
  Check(!vm.IsTransitioning&&vm.Slots[2].SignalServer.SignalText=="—"&&!vm.Slots[2].IsSelectable,"ping-only/failed data updates without any reveal pass");
  var cycle=new ServerListViewModel();var all=new[]{S(G(1)),S(G(2)),S(G(3))};cycle.PublishForTest(all,true);cycle.PublishForTest(new[]{all[1],all[2],all[0]},true);
  int n=Drain(cycle);Check(n==1&&n*ServerCometTiming.SORT_PASS_MS==800,"three-country cycle reaches all final positions in one 800ms pass");
  var direct=new ServerListViewModel();direct.PublishForTest(all,true);direct.PublishForTest(new[]{all[2],all[1],all[0]},true);
  Check(Drain(direct)==1,"two distant servers exchange in one 800ms pass, skipping the unchanged middle row");
  var four=Enumerable.Range(1,4).Select(id=>S(G(id,id==1))).ToArray();
  var descent=new ServerListViewModel();descent.PublishForTest(four,true);
  descent.PublishForTest(new[]{four[1],four[2],four[3],four[0]},true);
  var drop=descent.TryBeginPass();
  Check(drop.Rows.Length==4&&drop.Rows.Single(row=>row.Target.Service.ID==1).Index==3,
    "all four changed rows get lights and direct final destinations");
  descent.Reveal(drop,ServerRowPart.Name);Unique(descent);
  Check(descent.Slots[3].DisplayedServer.Service.ID==1,
    "country moves directly from first to fourth without visiting second or third");
  descent.Finish(drop);Unique(descent);
  Check(descent.Slots[3].SelectionServer.IsSelectedCountry,
    "direct move preserves identity selection");
  int remainder=Drain(descent);Check(remainder==0&&descent.Slots[3].DisplayedServer.Service.ID==1,
    "no follow-up passes or intermediate country assignments after the four-row reveal");
  cycle.PublishForTest(all,true);var old=cycle.TryBeginPass();cycle.Reveal(old,ServerRowPart.Signal);cycle.CompleteImmediately();var baseline=cycle.Slots.Select(s=>s.DisplayedServer.Identity).ToArray();
  cycle.PublishForTest(all.Reverse(),true,false);cycle.Reveal(old,ServerRowPart.Name);cycle.Finish(old);
  Check(!cycle.IsTransitioning&&cycle.Slots.Select(s=>s.DisplayedServer.Identity).SequenceEqual(baseline),"new cycle cancels partial presentation and stale callbacks without light");Unique(cycle);
  cycle.PublishForTest(new[]{all[2],S(G(4))},true);Drain(cycle);Check(cycle.Slots.Count==2&&cycle.Slots[1].DisplayedServer.Service.ID==4,"added/removed countries reconcile without duplicates or loss");
  cycle.PublishForTest(new ServerItem[0],true);Check(cycle.Slots.Count==0&&!cycle.IsTransitioning,"empty source cancels immediately");
  cycle.PublishForTest(new[]{S(G(9))},true);Drain(cycle);cycle.PublishForTest(new[]{S(G(10))},false);Check(!cycle.IsTransitioning&&cycle.Slots[0].DisplayedServer.Service.ID==10,"single row and reduced-motion fallback apply latest state");
  // Every fixed permutation resolves in one unlimited pass,
  // and each intermediate notification retains all countries.
  foreach(var order in Permutations(Enumerable.Range(1,6).ToArray(),0)) {
   var exact=new ServerListViewModel();var items=Enumerable.Range(1,6).Select(id=>S(G(id))).ToArray();
   exact.PublishForTest(items,true);exact.PublishForTest(order.Select(id=>items[id-1]),true);
   int passes=0;ServerSortPass step;
   while((step=exact.TryBeginPass())!=null) {
    if(++passes>1)throw new Exception("obsolete intermediate reorder pass");
    foreach(var row in step.Rows)CheckDestination(row.Index,row.Target.Service.ID,order);
    exact.Finish(step);Unique(exact);
   }
   if(!exact.Slots.Select(slot=>slot.DisplayedServer.Service.ID).SequenceEqual(order))throw new Exception("incorrect final order");
  }
  Check(true,"all 720 six-country orders reveal final assignments once per position");
  var random=new Random(17);var churn=new ServerListViewModel();var many=Enumerable.Range(1,37).Select(id=>S(G(id,id==17))).ToArray();churn.PublishForTest(many,true);
  for(int i=0;i<150;i++){churn.PublishForTest(many.OrderBy(x=>random.Next()).ToArray(),true);var active=churn.TryBeginPass();if(active!=null){churn.Reveal(active,ServerRowPart.Signal);Unique(churn);for(int j=0;j<3;j++)churn.PublishForTest(many.OrderBy(x=>random.Next()).ToArray(),true);churn.Finish(active);Unique(churn);}}
  Drain(churn);Check(churn.Slots.Select(s=>s.DisplayedServer.Service.ID).OrderBy(x=>x).SequenceEqual(Enumerable.Range(1,37)),"rapid replanning preserves every country in every group without blank rows or duplicate names/flags");
  var large=new ServerListViewModel();var lots=Enumerable.Range(1,4096).Select(id=>S(G(id))).ToArray();large.PublishForTest(lots,true);large.PublishForTest(lots.Reverse(),true);int instant=0;ServerSortPass offscreen;while((offscreen=large.TryBeginPass())!=null){large.Finish(offscreen);instant++;}
  Check(instant==1&&!large.IsTransitioning,"4096 changed rows resolve in one pass without a band-count limit");
  Check(ServerCometTiming.SORT_PASS_MS==800&&ServerCometTiming.FLASH_MS==400&&ServerCometTiming.BAND_WIDTH_DIP==170&&ServerCometTiming.SKEW_DEG==-18,"exact named timing and geometry constants");
  Check(Math.Abs(ServerCometTiming.Ease(.5)-.5)<1e-10&&ServerCometTiming.Ease(-1)==0&&ServerCometTiming.Ease(2)==1,"clamped accelerate-decelerate cosine easing");
  var timeline=new ServerCometTimeline(360,new[]{350d,160d,300d,40d,200d});var reveals=new List<int>();timeline.Advance(0,(i,t)=>reveals.Add(i));timeline.Advance(800,(i,t)=>reveals.Add(i));timeline.Advance(1200,(i,t)=>reveals.Add(i));
  Check(reveals.SequenceEqual(new[]{3,1,4,2,0}),"dropped frames reveal groups once in measured physical order, including RTL coordinates");
  var paired=new ServerCometTimeline(360,new[]{40d,100d,180d,280d,350d},400,new[]{30d,200d,160d,320d,390d});
  var times=new Dictionary<int,double>();paired.Advance(800,(i,t)=>times.Add(i,t));
  Check(times.Count==5&&Math.Abs(times[1]-Math.Max(ServerCometTiming.CrossingTime(100,360),ServerCometTiming.CrossingTime(200,400)))<1e-8,
    "paired reveal waits until both physical bands reach the group, including different row widths");
  var rotatedTimeline=new ServerCometTimeline(new[]{360d,400d,420d},
   new[]{new[]{40d,100d,180d,280d,350d},new[]{30d,200d,160d,320d,390d},new[]{410d,210d,310d,110d,10d}});
  var cycleTimes=new Dictionary<int,double>();rotatedTimeline.Advance(800,(i,t)=>cycleTimes.Add(i,t));
  Check(cycleTimes.Count==5&&Math.Abs(cycleTimes[0]-ServerCometTiming.CrossingTime(410,420))<1e-8,
    "cycle timeline waits for every affected surface before atomic reveal");
  var live=new ServerListViewModel();live.PublishForTest(all,true);live.PublishForTest(new[]{all[2],all[1],all[0]},true);var retired=live.TryBeginPass();live.Reveal(retired,ServerRowPart.Name);
  live.PublishForTest(new[]{all[0],S(G(4))},true);live.Finish(retired);Unique(live);
  Check(!live.IsTransitioning&&live.Slots.Count==2&&live.Slots[1].DisplayedServer.Service.ID==4,"membership change cancels a partial pair and applies new countries without holes or stale callbacks");
  Check(Math.Abs(ServerCometTiming.BandLeft(400,360)-(.5*(360+.85*170)-.85*170))<1e-8,"exact band/leading-edge coordinate formula");
 }
}
'''
with tempfile.TemporaryDirectory(prefix='server-comet-') as directory:
    path=Path(directory)
    (path/'Program.cs').write_text(fixture.replace('__PICKER_XAML__',(root/'IRSpeedyVPN/Components/ServerListControl/ServerCountryPicker.xaml').as_posix()))
    for name in ['ServerListViewModel.cs','ServerCometTimeline.cs']:
        (path/name).write_text((root/'IRSpeedyVPN/Components/ServerListControl'/name).read_text())
    overlay=(root/'IRSpeedyVPN/Components/ServerListControl/ServerListCometOverlay.cs').read_text()
    attached=overlay[overlay.index('    public static class ServerCometPart'):overlay.index('    public sealed class ServerListCometOverlay')]
    (path/'ServerCometPart.cs').write_text('using System.Windows;namespace IRSpeedyVPN.Components.ServerListControl{\n'+attached+'\n}')
    animator=(root/'IRSpeedyVPN/Components/ServerListControl/ServerListSweepAnimator.cs').read_text()
    find_groups=animator[animator.index('        private static void FindGroups('):animator.index('        private static bool PartChanged(')]
    (path/'ParsedGroupFinder.cs').write_text('using System.Windows;using System.Windows.Media;using IRSpeedyVPN.Components.ServerListControl;class ParsedGroupFinder{\n'
        +'public static FrameworkElement[] Find(DependencyObject root){var groups=new FrameworkElement[5];FindGroups(root,groups);return groups;}\n'+find_groups+'\n}')
    (path/'Checks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion></PropertyGroup></Project>')
    subprocess.run([sys.argv[1] if len(sys.argv)>1 else 'dotnet','run','--project',directory,'-v:q'],check=True)
