using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace ScreenPilot
{
    public class RememberedScreen
    {
        public string Key;
        public Mode SourceMode, TargetMode;
        public DisplayPath Path;
        public bool HasTarget;
    }
    public class LayoutMemory
    {
        public List<RememberedScreen> Screens = new List<RememberedScreen>();
        public string PreferredPrimary;
        public List<RememberedScreen> LastApplied = new List<RememberedScreen>();
        public List<string> DeviceOrder = new List<string>();
        public List<string> ConnectedKeys = new List<string>();
        public void RememberDevices(IEnumerable<MonitorInfo> monitors)
        {
            foreach(var m in monitors.OrderByDescending(m=>m.Active).ThenBy(m=>m.GdiName).ThenBy(m=>m.Key))
                if(!DeviceOrder.Contains(m.Key)) DeviceOrder.Add(m.Key);
        }
        static string FileName { get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenPilot", "layout.xml"); } }
        public static LayoutMemory Load()
        {
            try { using(var file=File.OpenRead(FileName)) return (LayoutMemory)new XmlSerializer(typeof(LayoutMemory)).Deserialize(file); }
            catch { return new LayoutMemory(); }
        }
        public void Save()
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FileName));
            string temp=FileName+".tmp";
            try
            {
                using(var file=File.Create(temp)) new XmlSerializer(typeof(LayoutMemory)).Serialize(file,this);
                if(File.Exists(FileName)) File.Replace(temp,FileName,null); else File.Move(temp,FileName);
            }
            catch
            {
                try { if(File.Exists(temp)) File.Delete(temp); } catch { }
                throw;
            }
        }
        // Remembering the layout is a convenience, not a precondition for listing or switching
        // screens. A read-only, sandboxed or otherwise denied data folder must be reported to the
        // caller instead of aborting the whole screen refresh.
        public static string LastSaveError;
        public bool TrySave()
        {
            try { Save(); LastSaveError=null; return true; }
            catch(Exception e) { LastSaveError=Describe(e); return false; }
        }
        static string Describe(Exception e)
        {
            // Report the innermost reason: for a denied file it carries the exact path.
            Exception last=e;
            while(last.InnerException!=null) last=last.InnerException;
            string text=last.Message;
            return text.EndsWith("。") ? text.Substring(0,text.Length-1) : text;
        }
        public static string DataFolder { get { return System.IO.Path.GetDirectoryName(FileName); } }
        public static Mode Position(Mode m,int x,int y)
        {
            m.D1=(m.D1 & 0xffffffffUL) | ((ulong)unchecked((uint)x)<<32);
            m.D2=(m.D2 & 0xffffffff00000000UL) | unchecked((uint)y); return m;
        }
        public static Rectangle Bounds(Mode m) { return new Rectangle(m.X,m.Y,(int)m.Width,(int)m.Height); }
        public void Observe(Snapshot data,List<MonitorInfo> monitors,bool preservePositions=false)
        {
            var active=monitors.Where(m=>m.Active).ToList();
            RememberDevices(monitors);
            bool hardwareChanged=ConnectedKeys.Count>0 && !new HashSet<string>(ConnectedKeys).SetEquals(monitors.Select(m=>m.Key));
            ConnectedKeys=monitors.Select(m=>m.Key).ToList();
            if(hardwareChanged || preservePositions)
            {
                // Windows may compact/rebase automatically after unplugging. Preserve the full layout.
                LastApplied.Clear();
                foreach(var monitor in active)
                {
                    var entry=ReadScreen(data,monitor);
                    if(entry==null) continue;
                    LastApplied.Add(entry);
                    var previous=Screens.FirstOrDefault(s=>s.Key==monitor.Key);
                    var remembered=ReadScreen(data,monitor);
                    if(previous!=null)
                    {
                        remembered.SourceMode=Position(remembered.SourceMode,previous.SourceMode.X,previous.SourceMode.Y);
                        Screens[Screens.IndexOf(previous)]=remembered;
                    }
                    else
                    {
                        PlaceNew(remembered); Screens.Add(remembered);
                    }
                }
                return;
            }
            // A compacted desktop is temporary. Do not overwrite the remembered full layout.
            if(LastApplied.Count==active.Count && active.All(m=>LastApplied.Any(s=>s.Key==m.Key)))
            {
                bool same=true;
                foreach(var monitor in active)
                {
                    var path=data.Paths.First(p=>NativeDisplay.TargetKey(p)==monitor.TargetKey && (p.Flags&1)!=0);
                    var previous=LastApplied.First(s=>s.Key==monitor.Key);
                    if(path.Source.Mode>=data.Modes.Length || Bounds(data.Modes[path.Source.Mode])!=Bounds(previous.SourceMode) || path.Target.Rotation!=previous.Path.Target.Rotation ||
                        (ulong)path.Target.Refresh.Numerator*previous.Path.Target.Refresh.Denominator!=(ulong)previous.Path.Target.Refresh.Numerator*path.Target.Refresh.Denominator) same=false;
                }
                if(same) return;
            }
            LastApplied.Clear();
            var anchor=active.FirstOrDefault(m=>Screens.Any(s=>s.Key==m.Key));
            if(anchor!=null)
            {
                var path=data.Paths.First(p=>NativeDisplay.TargetKey(p)==anchor.TargetKey && (p.Flags&1)!=0);
                var old=Screens.First(s=>s.Key==anchor.Key);
                if(path.Source.Mode<data.Modes.Length)
                {
                    var current=data.Modes[path.Source.Mode]; int dx=current.X-old.SourceMode.X,dy=current.Y-old.SourceMode.Y;
                    foreach(var item in Screens) item.SourceMode=Position(item.SourceMode,item.SourceMode.X+dx,item.SourceMode.Y+dy);
                }
            }
            foreach(var monitor in active)
            {
                var path=data.Paths.First(p=>NativeDisplay.TargetKey(p)==monitor.TargetKey && (p.Flags&1)!=0);
                if(path.Source.Mode>=data.Modes.Length || data.Modes[path.Source.Mode].Type!=1) continue;
                var record=Screens.FirstOrDefault(s=>s.Key==monitor.Key);
                if(record==null) { record=new RememberedScreen { Key=monitor.Key }; Screens.Add(record); }
                record.Path=path; record.SourceMode=data.Modes[path.Source.Mode];
                record.HasTarget=path.Target.Mode<data.Modes.Length && data.Modes[path.Target.Mode].Type==2;
                if(record.HasTarget) record.TargetMode=data.Modes[path.Target.Mode];
                if(PreferredPrimary==null && record.SourceMode.X==0 && record.SourceMode.Y==0) PreferredPrimary=monitor.Key;
            }
            // Respect an explicit primary-monitor change while the preferred monitor is still active.
            if(active.Any(m=>m.Key==PreferredPrimary))
            {
                var primary=Screens.FirstOrDefault(s=>active.Any(m=>m.Key==s.Key) && s.SourceMode.X==0 && s.SourceMode.Y==0);
                if(primary!=null) PreferredPrimary=primary.Key;
            }
        }
        public static RememberedScreen ReadScreen(Snapshot data,MonitorInfo monitor)
        {
            var paths=data.Paths.Where(item=>NativeDisplay.TargetKey(item)==monitor.TargetKey && (item.Flags&1)!=0).ToArray();
            if(paths.Length==0 || paths[0].Source.Mode>=data.Modes.Length) return null;
            var p=paths[0];
            if(data.Modes[p.Source.Mode].Type!=1) return null;
            var entry=new RememberedScreen { Key=monitor.Key,Path=p,SourceMode=data.Modes[p.Source.Mode],HasTarget=p.Target.Mode<data.Modes.Length && data.Modes[p.Target.Mode].Type==2 };
            if(entry.HasTarget) entry.TargetMode=data.Modes[p.Target.Mode];
            return entry;
        }
        public void PlaceNew(RememberedScreen screen)
        {
            if(Screens.Any(s=>Bounds(s.SourceMode).IntersectsWith(Bounds(screen.SourceMode))))
                screen.SourceMode=Position(screen.SourceMode,Screens.Max(s=>Bounds(s.SourceMode).Right),0);
        }
        public void Accept(Snapshot plan,List<MonitorInfo> monitors,string primaryKey)
        {
            LastApplied.Clear();
            foreach(var p in plan.Paths)
            {
                string key=monitors.First(m=>m.TargetKey==NativeDisplay.TargetKey(p)).Key;
                var entry=new RememberedScreen { Key=key,Path=p,SourceMode=plan.Modes[p.Source.Mode],HasTarget=p.Target.Mode<plan.Modes.Length };
                if(entry.HasTarget) entry.TargetMode=plan.Modes[p.Target.Mode];
                LastApplied.Add(entry);
                if(!Screens.Any(s=>s.Key==key)) Screens.Add(entry);
            }
            if(primaryKey!=null) PreferredPrimary=primaryKey;
        }
        public Snapshot Plan(Snapshot all,List<MonitorInfo> monitors,IEnumerable<string> keys,Func<DisplayPath,RememberedScreen> preferred,string primaryKey=null)
        {
            var wanted=new HashSet<string>(keys);
            var selected=monitors.Where(m=>wanted.Contains(m.Key)).ToList();
            if(selected.Count!=wanted.Count) throw new InvalidOperationException("屏幕已断开，请刷新。");
            var paths=NativeDisplay.BuildPaths(all,selected.Select(m=>m.TargetKey));
            var records=new List<RememberedScreen>();
            int edge=Screens.Count==0?0:Screens.Max(s=>Bounds(s.SourceMode).Right);
            for(int i=0;i<selected.Count;i++)
            {
                var record=Screens.FirstOrDefault(s=>s.Key==selected[i].Key);
                if(record==null)
                {
                    record=preferred(paths[i]); record.Key=selected[i].Key;
                    record.SourceMode=Position(record.SourceMode,edge,0); edge+=(int)record.SourceMode.Width;
                }
                records.Add(record);
            }
            int primary=selected.FindIndex(m=>m.Key==(primaryKey??PreferredPrimary));
            if(primary<0) primary=records.FindIndex(s=>s.SourceMode.X==0 && s.SourceMode.Y==0);
            if(primary<0) primary=0;
            int ox=records[primary].SourceMode.X,oy=records[primary].SourceMode.Y;
            var modes=new List<Mode>();
            for(int i=0;i<paths.Length;i++)
            {
                var p=paths[i]; var saved=records[i];
                var source=Position(saved.SourceMode,saved.SourceMode.X-ox,saved.SourceMode.Y-oy);
                source.Adapter=p.Source.Adapter; source.Id=p.Source.Id; source.Type=1;
                p.Source.Mode=(uint)modes.Count; modes.Add(source);
                // Rebind saved timings to current adapter/target IDs after re-enumeration or reboot.
                p.Target.Rotation=saved.Path.Target.Rotation; p.Target.Scaling=saved.Path.Target.Scaling;
                p.Target.Refresh=saved.Path.Target.Refresh; p.Target.Scan=saved.Path.Target.Scan;
                if(saved.HasTarget)
                {
                    var target=saved.TargetMode; target.Adapter=p.Target.Adapter;target.Id=p.Target.Id;target.Type=2;
                    p.Target.Mode=(uint)modes.Count; modes.Add(target);
                }
                paths[i]=p;
            }
            var result=new Snapshot { Paths=paths,Modes=modes.ToArray() };
            Compact(result,primary);
            EnsureContiguous(result); return result;
        }
        static bool Adjacent(Rectangle a,Rectangle b)
        {
            return ((a.Right==b.Left || b.Right==a.Left) && Math.Min(a.Bottom,b.Bottom)>Math.Max(a.Top,b.Top)) ||
                ((a.Bottom==b.Top || b.Bottom==a.Top) && Math.Min(a.Right,b.Right)>Math.Max(a.Left,b.Left));
        }
        static HashSet<int> Connected(Rectangle[] rectangles,int start)
        {
            var set=new HashSet<int> { start }; bool added=true;
            while(added) { added=false; for(int i=0;i<rectangles.Length;i++) if(!set.Contains(i) && set.Any(j=>Adjacent(rectangles[i],rectangles[j]))) { set.Add(i);added=true; } }
            return set;
        }
        public static void Compact(Snapshot plan,int primary)
        {
            var rectangles=plan.Paths.Select(p=>Bounds(plan.Modes[p.Source.Mode])).ToArray();
            for(int step=0;step<rectangles.Length;step++)
            {
                var placed=Connected(rectangles,primary); if(placed.Count==rectangles.Length) break;
                HashSet<int> bestGroup=null;Point bestDelta=Point.Empty;long bestCost=long.MaxValue;
                var visited=new HashSet<int>(placed);
                for(int i=0;i<rectangles.Length;i++)
                {
                    if(visited.Contains(i)) continue;
                    var group=Connected(rectangles,i);visited.UnionWith(group);
                    foreach(int member in group) foreach(int fixedIndex in placed)
                    {
                        var a=rectangles[member];var b=rectangles[fixedIndex];
                        int[] vertical={0,b.Top-a.Top,b.Bottom-a.Bottom};int[] horizontal={0,b.Left-a.Left,b.Right-a.Right};
                        var candidates=new List<Point>();
                        foreach(int dy in vertical) { candidates.Add(new Point(b.Right-a.Left,dy));candidates.Add(new Point(b.Left-a.Right,dy)); }
                        foreach(int dx in horizontal) { candidates.Add(new Point(dx,b.Bottom-a.Top));candidates.Add(new Point(dx,b.Top-a.Bottom)); }
                        foreach(var delta in candidates)
                        {
                            bool valid=true,touches=false;
                            foreach(int moving in group)
                            {
                                var moved=rectangles[moving];moved.Offset(delta);
                                for(int other=0;other<rectangles.Length;other++) if(!group.Contains(other))
                                {
                                    if(moved.IntersectsWith(rectangles[other])) valid=false;
                                    if(placed.Contains(other) && Adjacent(moved,rectangles[other])) touches=true;
                                }
                            }
                            long cost=(Math.Abs((long)delta.X)+Math.Abs((long)delta.Y))*group.Count;
                            if(valid && touches && cost<bestCost) { bestCost=cost;bestGroup=group;bestDelta=delta; }
                        }
                    }
                }
                if(bestGroup==null) throw new InvalidOperationException("无法安全拼接当前屏幕排列，请先在 Windows 显示设置中调整。");
                foreach(int index in bestGroup) rectangles[index].Offset(bestDelta);
            }
            for(int i=0;i<rectangles.Length;i++)
            {
                uint index=plan.Paths[i].Source.Mode;plan.Modes[index]=Position(plan.Modes[index],rectangles[i].X,rectangles[i].Y);
            }
        }
        public static void EnsureContiguous(Snapshot plan)
        {
            var rectangles=plan.Paths.Select(p=>Bounds(plan.Modes[p.Source.Mode])).ToArray();
            for(int i=0;i<rectangles.Length;i++) for(int j=i+1;j<rectangles.Length;j++)
                if(rectangles[i].IntersectsWith(rectangles[j])) throw new InvalidOperationException("原位置会造成屏幕重叠，已取消切换。请先在 Windows 中调整排列。");
            var found=new HashSet<int>(); if(rectangles.Length>0) found.Add(0);
            bool changed=true;
            while(changed)
            {
                changed=false;
                for(int i=0;i<rectangles.Length;i++) if(!found.Contains(i))
                    foreach(int j in found.ToArray())
                    {
                        Rectangle a=rectangles[i],b=rectangles[j];
                        bool adjacent=((a.Right==b.Left || b.Right==a.Left) && Math.Min(a.Bottom,b.Bottom)>Math.Max(a.Top,b.Top)) ||
                            ((a.Bottom==b.Top || b.Bottom==a.Top) && Math.Min(a.Right,b.Right)>Math.Max(a.Left,b.Left));
                        if(adjacent) { found.Add(i);changed=true;break; }
                    }
            }
            if(found.Count!=rectangles.Length) throw new InvalidOperationException("关闭这块屏幕后，剩余屏幕原位置之间会有空隙，Windows 无法保持该排列。已取消切换；可只开一块屏幕，或先调整排列。");
        }
        public static bool Matches(Snapshot actual,Snapshot expected)
        {
            if(!NativeDisplay.TargetsMatch(actual,expected.Paths.Select(NativeDisplay.TargetKey))) return false;
            foreach(var wanted in expected.Paths)
            {
                var p=actual.Paths.First(x=>NativeDisplay.TargetKey(x)==NativeDisplay.TargetKey(wanted) && (x.Flags&1)!=0);
                if(p.Source.Mode>=actual.Modes.Length) return false;
                Mode a=actual.Modes[p.Source.Mode],b=expected.Modes[wanted.Source.Mode];
                if(a.X!=b.X || a.Y!=b.Y || a.Width!=b.Width || a.Height!=b.Height || p.Target.Rotation!=wanted.Target.Rotation) return false;
                if(wanted.Target.Refresh.Denominator!=0 && (p.Target.Refresh.Denominator==0 || (ulong)p.Target.Refresh.Numerator*wanted.Target.Refresh.Denominator!=(ulong)wanted.Target.Refresh.Numerator*p.Target.Refresh.Denominator)) return false;
            }
            return true;
        }
    }
}
