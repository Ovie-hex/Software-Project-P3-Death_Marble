using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
namespace Software_Project_P3_Death_Marble.Views;
internal static class FateDialog
{
    static Brush Ink(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
    static TextBlock Text(string value,double size,Brush color) => new TextBlock { Text=value, FontSize=size, Foreground=color, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(6) };
    static FrameworkElement Devil()
    {
        string[] pixels={"R......R","RR....RR",".RRRRRR.","RRRRRRRR","RYRRRRYR","RRRRRRRR",".RWWWWR.","..RRRR..",".RRRRRR.","RR.RR.RR","...RR...","..R..R.."};
        var canvas=new Canvas { Width=96, Height=144, Margin=new Thickness(12) };
        for(int y=0;y<pixels.Length;y++) for(int x=0;x<pixels[y].Length;x++) {
            char c=pixels[y][x]; if(c=='.') continue;
            var r=new Rectangle { Width=12,Height=12,Fill=c=='Y'?Brushes.Gold:c=='W'?Brushes.Bisque:Ink("#C52E49")};
            Canvas.SetLeft(r,x*12); Canvas.SetTop(r,y*12); canvas.Children.Add(r);
        }
        return canvas;
    }
    public static int? Choose(Window? owner,string title,IEnumerable<(int Id,string Label)> options,int? recommended=null,string? dialogue=null)
    {
        var w=new Window { Title=title, Width=650, Height=520, Background=Ink("#140D19"), WindowStartupLocation=owner==null?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner, ResizeMode=ResizeMode.NoResize };
        if(owner!=null) w.Owner=owner;
        var root=new DockPanel { Margin=new Thickness(20) }; w.Content=root;
        var heading=Text(title,28,Brushes.Gold); heading.FontWeight=FontWeights.Bold; DockPanel.SetDock(heading,Dock.Top); root.Children.Add(heading);
        var speech=new DockPanel(); DockPanel.SetDock(speech,Dock.Top); root.Children.Add(speech);
        if(recommended.HasValue) { var devil=Devil(); DockPanel.SetDock(devil,Dock.Left); speech.Children.Add(devil); }
        var line=Text("",18,Brushes.Bisque); line.VerticalAlignment=VerticalAlignment.Center; speech.Children.Add(line);
        string message=dialogue??"원하는 대상을 선택하세요."; int cursor=0;
        var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(18) }; timer.Tick+=(_,_)=> { if(cursor<message.Length) line.Text=message.Substring(0,++cursor); else timer.Stop(); }; w.Loaded+=(_,_)=>timer.Start(); w.Closed+=(_,_)=>timer.Stop();
        var cancel=new Button { Content=recommended.HasValue?"추천 경로 선택":"취소", Margin=new Thickness(6),Padding=new Thickness(10),Background=Ink("#392536"),Foreground=Brushes.Bisque }; DockPanel.SetDock(cancel,Dock.Bottom); root.Children.Add(cancel);
        int? selected=null; cancel.Click+=(_,_)=> { selected=recommended; w.DialogResult=selected.HasValue; };
        var panel=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Center }; root.Children.Add(new ScrollViewer { Content=panel, VerticalScrollBarVisibility=ScrollBarVisibility.Auto });
        foreach(var option in options) {
            bool best=option.Id==recommended;
            var label=Text(option.Label+(best?"\n★ 악마의 추천":""),17,best?Brushes.Gold:Brushes.Bisque); label.TextAlignment=TextAlignment.Center;
            var button=new Button { Content=label, Width=250, MinHeight=70, Margin=new Thickness(7), Padding=new Thickness(8), Background=Ink(best?"#50351E":"#30203D"), BorderBrush=best?Brushes.Gold:Ink("#8961A8"), BorderThickness=new Thickness(best?3:1), Cursor=System.Windows.Input.Cursors.Hand };
            button.Click+=(_,_)=> { selected=option.Id; w.DialogResult=true; }; panel.Children.Add(button);
        }
        w.ShowDialog(); return selected;
    }
}
