using System;
namespace System.Windows.Threading {
 internal sealed class DispatcherTimer {
  internal static DispatcherTimer Last;
  public DispatcherTimer(){Last=this;}
  public TimeSpan Interval{get;set;}public event EventHandler Tick;
  public void Start(){}public void Stop(){}public void Fire()=>Tick?.Invoke(this,EventArgs.Empty);
 }
}
namespace System.Windows.Input {internal class Cursor{}internal static class Cursors{internal static Cursor Hand=new Cursor(),Arrow=new Cursor();}}
namespace System.Windows.Media {internal class Brush{}internal class BrushConverter {internal object ConvertFromString(string value)=>new Brush();}internal class ImageSource{}internal static class Brushes{internal static Brush Gray=new Brush();}}
namespace IRSpeedyVPN.Common {
 internal class ThemeManager {internal static ThemeManager Instance=new ThemeManager();internal System.Windows.Media.Brush GetLiveBrush(string key)=>new System.Windows.Media.Brush();}
 internal static class Extensions {internal static System.Collections.Generic.IEnumerable<IRSpeedyVPN.Models.NewService.Url> OrderByHysteriaFirst(this System.Collections.Generic.IEnumerable<IRSpeedyVPN.Models.NewService.Url> urls)=>urls;}
}
namespace IRSpeedyVPN.Components.ServerListControl {
 internal abstract class PickerItem {protected void On(string name=null){} }
 internal static class FlagCatalog {internal static System.Windows.Media.ImageSource TryGetListFlag(string code)=>null;internal static string Initials(string name)=>"";}
}

