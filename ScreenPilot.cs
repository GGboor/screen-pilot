using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ScreenPilot
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [STAThread] static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--guard") return Recovery.Guard(args[1]);
            if (args.Length == 2 && args[0] == "--list")
            {
                try
                {
                    var data = NativeDisplay.Query(true);
                    var rows = NativeDisplay.Monitors(data).Select(m => m.Name + "\t" + m.Active + "\t" + m.GdiName + "\t" + m.Detail + "\t" + m.Key+"\t"+m.TypeLabel+"\t"+m.PositionText+"\t"+m.AdapterName);
                    File.WriteAllLines(args[1], rows); return 0;
                }
                catch (Exception e) { File.WriteAllText(args[1], e.ToString()); return 1; }
            }
            bool created;
            using (var mutex = new Mutex(true, "Local\\ScreenPilot.Main", out created))
            {
                if (!created) { MessageBox.Show("屏幕管家已经在运行，请点击右下角托盘图标，或按 Ctrl + Alt + Shift + 0 打开。", "屏幕管家"); return 0; }
                SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            return 0;
        }
    }
    class ProtectionChoice
    {
        public string Key, Text;
        public override string ToString() { return Text; }
    }
    class MainForm : Form
    {
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        readonly Color Ink = RoundedStyle.Text, Accent = RoundedStyle.Blue;
        FlowLayoutPanel cards;
        MonitorLayoutView layoutView;
        Panel pendingPanel;
        Label status, countdown, summary;
        RoundedPicker protection;
        Button apply, refresh, all, original, recoverWindows;
        NotifyIcon tray;
        System.Windows.Forms.Timer tick;
        List<MonitorInfo> monitors = new List<MonitorInfo>();
        readonly Dictionary<string, CheckBox> toggles = new Dictionary<string, CheckBox>();
        List<string> stableOrder { get { return layoutMemory.DeviceOrder; } }
        readonly List<int> hotkeys = new List<int>();
        Snapshot initial, before;
        Snapshot pendingLayout;
        string pendingPrimary;
        readonly LayoutMemory layoutMemory = LayoutMemory.Load();
        string pendingDir;
        string[] pendingTargets, recoveryTargets;
        int recoveryPasses;
        readonly Stopwatch backgroundClock = Stopwatch.StartNew();
        readonly Stopwatch inventoryClock = Stopwatch.StartNew();
        readonly Stopwatch deviceEventClock = new Stopwatch();
        readonly Stopwatch connectionSettling = new Stopwatch();
        string inventorySignature;
        List<MonitorInfo> pendingMonitors;
        Stopwatch elapsed;
        bool rebuilding, applying, exit;
        string ProtectedKey { get { var p = protection.SelectedItem as ProtectionChoice; return p == null ? null : p.Key; } }
        public MainForm()
        {
            Text = "屏幕管家";
            using (var resource = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("ScreenPilot.AppIcon.ico"))
            using (var icon = new Icon(resource)) Icon = (Icon)icon.Clone();
            Size = new Size(980, 1000); MinimumSize = new Size(900, 800); StartPosition = FormStartPosition.CenterScreen;
            BackColor = RoundedStyle.Background; ForeColor = Ink; Font = new Font("Microsoft YaHei UI", 10F);
            AutoScaleMode = AutoScaleMode.Dpi;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(30, 22, 30, 20) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            Controls.Add(root);
            var heading = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            heading.Controls.Add(new Label { Text = "屏幕管家", Font = new Font(Font.FontFamily, 28, FontStyle.Bold), AutoSize = true, Location = new Point(-3, 9) });
            heading.Controls.Add(new Label { Text = "查看屏幕类型与排列，选择需要开启的屏幕。", AutoSize = true, Location = new Point(1, 68), ForeColor = RoundedStyle.Secondary });
            summary = new Label { Text = "正在连接显示器", TextAlign = ContentAlignment.MiddleRight, ForeColor = RoundedStyle.Secondary, Size = new Size(240, 28) };
            heading.Controls.Add(summary);
            heading.SizeChanged += delegate { summary.Location = new Point(heading.Width - summary.Width - 5, 43); };
            root.Controls.Add(heading, 0, 0);
            var protectRow = new RoundedPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 15) };
            protectRow.Controls.Add(new Label { Text = "串流保护", BackColor = Color.White, Font = new Font(Font, FontStyle.Bold), AutoSize = true, Location = new Point(20, 15) });
            protectRow.Controls.Add(new Label { Text = "避免误关正在串流的屏幕", BackColor = Color.White, Font = new Font(Font.FontFamily, 9), AutoSize = true, Location = new Point(20, 43), ForeColor = RoundedStyle.Secondary });
            protection = new RoundedPicker { Size = new Size(390, 42), Location = new Point(265, 17), AccessibleName = "保护串流屏幕" };
            protectRow.Controls.Add(protection);
            refresh = Button("刷新", delegate { RefreshMonitors(); }); refresh.Size = new Size(72, 42); refresh.MinimumSize = Size.Empty; refresh.BackColor = Color.White; protectRow.Controls.Add(refresh);
            protectRow.SizeChanged += delegate { refresh.Location = new Point(protectRow.Width - 94, 17); protection.Width = Math.Max(220, protectRow.Width - 377); };
            root.Controls.Add(protectRow, 0, 1);
            var screenSection = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0),ColumnCount=1,RowCount=3 };
            screenSection.RowStyles.Add(new RowStyle(SizeType.Absolute,160));
            screenSection.RowStyles.Add(new RowStyle(SizeType.Absolute,28));
            screenSection.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            layoutView=new MonitorLayoutView { Dock=DockStyle.Fill,Margin=new Padding(0,0,0,10) };
            var sectionLabel = new Label { Text = "我的屏幕", Dock = DockStyle.Fill, ForeColor = RoundedStyle.Secondary, Padding = new Padding(10, 2, 0, 0), Font = new Font(Font.FontFamily, 9) };
            cards = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
            screenSection.Controls.Add(layoutView,0,0);screenSection.Controls.Add(sectionLabel,0,1);screenSection.Controls.Add(cards,0,2);root.Controls.Add(screenSection, 0, 2);
            cards.SizeChanged += delegate { foreach (Control c in cards.Controls) c.Width = Math.Max(700, cards.ClientSize.Width - 22); };
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            apply = Button("应用所选屏幕", delegate { ApplySelection(SelectedKeys()); }); apply.BackColor = Accent; apply.ForeColor = Color.White; apply.Width = 220;
            all = Button("全部开启", delegate { ApplySelection(monitors.Select(m => m.Key)); });
            original = Button("恢复启动时配置", delegate { RestoreInitial(); });
            recoverWindows = Button("找回窗口", delegate { RecoverWindows(); }); recoverWindows.Width = 118;
            actions.Controls.AddRange(new Control[] { apply, all, original, recoverWindows }); root.Controls.Add(actions, 0, 3);
            pendingPanel = new RoundedPanel { Dock = DockStyle.Fill, Fill = Color.FromArgb(231, 241, 255), Visible = false, Margin = new Padding(0, 2, 0, 2) };
            countdown = new Label { AutoSize = true, Location = new Point(18, 13), Font = new Font(Font, FontStyle.Bold), BackColor = Color.FromArgb(231, 241, 255) };
            var keep = Button("画面正常，保留", delegate { Keep(); }); keep.Location = new Point(18, 48); keep.BackColor = Accent; keep.ForeColor = Color.White;
            var undo = Button("立即恢复", delegate { Undo(); }); undo.Location = new Point(190, 48);
            pendingPanel.Controls.AddRange(new Control[] { countdown, keep, undo }); root.Controls.Add(pendingPanel, 0, 4);
            status = new Label { Dock = DockStyle.Fill, Text = "正在读取显示器…", ForeColor = RoundedStyle.Secondary, Font = new Font(Font.FontFamily, 9), Padding = new Padding(5, 7, 0, 0), Margin = new Padding(0) }; root.Controls.Add(status, 0, 5);
            var footer = new Label { Dock = DockStyle.Fill, ForeColor = RoundedStyle.Secondary, Font = new Font(Font.FontFamily, 9), Padding = new Padding(5, 0, 0, 0), Margin = new Padding(0), Text = "Ctrl + Alt + Shift + 1～9 设为主屏  ·  Ctrl + Alt + 1～9 开关屏幕\nCtrl + Alt + Shift + 0 打开面板  ·  更改后 30 秒未确认自动恢复" }; root.Controls.Add(footer, 0, 6);
            tray = new NotifyIcon { Icon = Icon, Text = "屏幕管家", Visible = true };
            tray.DoubleClick += delegate { ShowPanel(); }; UpdateTray();
            tick = new System.Windows.Forms.Timer { Interval = 200 }; tick.Tick += delegate { TickPending(); CheckDesktop(); }; tick.Start();
            Shown += delegate
            {
                RefreshMonitors();
                var failed = new List<string>();
                for (int i = 0; i <= 9; i++)
                {
                    if (RegisterHotKey(Handle, 100 + i, 0x4000 | 1 | 2 | 4, (uint)(Keys.D0 + i))) hotkeys.Add(100 + i);
                    else failed.Add(i.ToString());
                }
                for(int i=1;i<=9;i++)
                {
                    if(RegisterHotKey(Handle,200+i,0x4000|1|2,(uint)(Keys.D0+i))) hotkeys.Add(200+i);
                    else failed.Add("Ctrl+Alt+"+i);
                }
                if (failed.Count > 0) status.Text += "\n快捷键 " + String.Join("、", failed) + " 被占用，可使用按钮或托盘。";
            };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (!exit && e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    if (pendingDir == null) Hide(); else status.Text = "请先保留本次更改或立即恢复。";
                }
            };
            FormClosed += delegate { tick.Stop(); foreach (int id in hotkeys) UnregisterHotKey(Handle, id); tray.Dispose(); };
        }
        Button Button(string text, EventHandler click)
        {
            var b = new RoundedButton { Text = text, Size = new Size(158, 44), Margin = new Padding(0, 9, 12, 5), Font = new Font(Font.FontFamily, 10, FontStyle.Bold) };
            b.Click += click; return b;
        }
        IEnumerable<string> SelectedKeys() { return toggles.Where(p => p.Value.Checked).Select(p => p.Key).ToArray(); }
        void RefreshMonitors(Snapshot prefetched=null,List<MonitorInfo> discovered=null)
        {
            if (pendingDir != null || applying) return;
            try
            {
                var data = prefetched??NativeDisplay.Query(true);
                string protect = ProtectedKey;
                monitors = discovered??NativeDisplay.Monitors(data);
                if(initial==null && monitors.Any(m=>m.Active)) initial=NativeDisplay.Capture(data,monitors);
                inventorySignature=NativeDisplay.InventorySignature(data,monitors);
                inventoryClock.Restart();
                if(layoutMemory.ConnectedKeys.Count>0 && !new HashSet<string>(layoutMemory.ConnectedKeys).SetEquals(monitors.Select(m=>m.Key))) connectionSettling.Restart();
                layoutMemory.Observe(data,monitors,connectionSettling.IsRunning && connectionSettling.ElapsedMilliseconds<12000);
                layoutMemory.TrySave();
                monitors = monitors.OrderBy(m => stableOrder.IndexOf(m.Key)).ToList();
                layoutView.UpdateScreens(monitors,Number);
                rebuilding = true;
                protection.Items.Clear(); protection.Items.Add(new ProtectionChoice { Text = "选择需要保护的屏幕" });
                foreach (var m in monitors) protection.Items.Add(new ProtectionChoice { Key = m.Key, Text = "屏幕 " + Number(m) + " · " + m.Name });
                if(protect!=null && !monitors.Any(m=>m.Key==protect)) protection.Items.Add(new ProtectionChoice { Key=protect,Text="屏幕 "+(stableOrder.IndexOf(protect)+1)+" · 已拔出，请重新选择保护对象" });
                protection.SelectedIndex = 0;
                for (int i = 1; i < protection.Items.Count; i++) if (((ProtectionChoice)protection.Items[i]).Key == protect) protection.SelectedIndex = i;
                cards.SuspendLayout();
                foreach (Control c in cards.Controls.Cast<Control>().ToArray()) c.Dispose();
                cards.Controls.Clear(); toggles.Clear();
                foreach (var m in monitors)
                {
                    var card = new RoundedPanel { Tag=m.Key,Height = 116, Width = Math.Max(700, cards.ClientSize.Width - 22), Margin = new Padding(0, 0, 0, 6) };
                    var label = new MonitorGlyph { Number = Number(m), Active = m.Active, Location = new Point(20, 25) };
                    var name = new Label { Text = m.Name, Font = new Font(Font.FontFamily, 12, FontStyle.Bold), BackColor = Color.White, Location = new Point(98, 16), Size = new Size(card.Width - 270, 28), AutoEllipsis = true, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
                    var type=new Label { Name="screenType",Text=m.TypeLabel,Font=new Font(Font.FontFamily,9),BackColor=Color.White,ForeColor=Accent,Location=new Point(98,43),Size=new Size(card.Width-210,22),AutoEllipsis=true,Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right };
                    var detail = new Label { Name="screenDetail",Text = m.Detail, Font = new Font(Font.FontFamily, 9), BackColor = Color.White, Location = new Point(98, 66), Size = new Size(card.Width - 355, 23), ForeColor = RoundedStyle.Secondary, AutoEllipsis = true, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
                    var state = new Label { Name="screenPosition",Text=m.PositionText,BackColor=Color.White,Font=new Font(Font.FontFamily,8.5f),Location=new Point(98,90),Size=new Size(card.Width-355,22),ForeColor=RoundedStyle.Secondary,AutoEllipsis=true,Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right };
                    var toggle = new RoundedSwitch { Checked = m.Active, Location = new Point(card.Width - 82, 18), Anchor = AnchorStyles.Top | AnchorStyles.Right, AccessibleName = m.Name + " 启用显示输出" };
                    string key = m.Key;
                    toggle.CheckedChanged += delegate { if (!rebuilding) status.Text = "选择尚未应用。点击“应用所选屏幕”后生效。"; };
                    toggles.Add(m.Key, toggle);
                    var solo = Button("只开此屏", delegate { ApplySelection(new[] { key }); }); solo.Size = new Size(106, 32); solo.Font = new Font(Font.FontFamily, 9); solo.BackColor = Color.FromArgb(241, 247, 255); solo.Location = new Point(card.Width - 130, 65); solo.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                    var primary = Button("设为主屏",delegate { SetPrimary(key); });primary.Size=new Size(106,32);primary.Font=new Font(Font.FontFamily,9);primary.Location=new Point(card.Width-245,65);primary.Anchor=AnchorStyles.Top|AnchorStyles.Right;
                    card.Controls.AddRange(new Control[] { label, name, type,detail, state, toggle, solo, primary }); cards.Controls.Add(card);
                }
                cards.ResumeLayout(); rebuilding = false;
                status.Text = "检测到 " + monitors.Count + " 块屏幕，" + monitors.Count(m => m.Active) + " 块已开启。自动检测插拔，已知屏幕编号保持不变。";
                if(protect!=null && !monitors.Any(m=>m.Key==protect)) status.Text="受保护的串流屏幕已拔出，请在上方重新选择保护对象。";
                summary.Text = monitors.Count + " 个屏幕   ·   " + monitors.Count(m => m.Active) + " 个已开启";
                status.Text += DataWarning();
                UpdateTray();
            }
            catch (Exception e) { rebuilding = false; Error(e); }
        }
        int Number(MonitorInfo m) { return stableOrder.IndexOf(m.Key) + 1; }
        void UpdateCurrentReadout(List<MonitorInfo> current=null)
        {
            if(current==null) current=NativeDisplay.Monitors(NativeDisplay.Query(true));
            layoutView.UpdateScreens(current,Number);
            summary.Text=current.Count+" 个屏幕   ·   "+current.Count(m=>m.Active)+" 个已开启";
            rebuilding=true;
            try
            {
                foreach(Control card in cards.Controls)
                {
                    var monitor=current.FirstOrDefault(m=>m.Key==(string)card.Tag);
                    card.Controls["screenPosition"].Text=monitor==null?"已拔出 · 不占桌面位置":monitor.PositionText;
                    if(monitor==null) continue;
                    card.Controls["screenDetail"].Text=monitor.Detail;
                    card.Controls["screenType"].Text=monitor.TypeLabel;
                    foreach(var glyph in card.Controls.OfType<MonitorGlyph>()) { glyph.Active=monitor.Active;glyph.Invalidate(); }
                    toggles[monitor.Key].Checked=monitor.Active;
                }
            }
            finally { rebuilding=false; }
        }
        void UpdateTray()
        {
            var menu = new ContextMenuStrip(); menu.Items.Add("打开屏幕管家", null, delegate { ShowPanel(); });
            foreach (var m in monitors)
            {
                string key = m.Key;
                var item = new ToolStripMenuItem("屏幕 " + Number(m) + " · " + m.Name) { Checked = m.Active };
                item.Click += delegate { Toggle(key); }; menu.Items.Add(item);
                menu.Items.Add("将屏幕 "+Number(m)+" 设为主屏",null,delegate { SetPrimary(key); });
            }
            menu.Items.Add("全部开启", null, delegate { ApplySelection(monitors.Select(m => m.Key)); });
            menu.Items.Add("找回离屏窗口", null, delegate { RecoverWindows(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { if (pendingDir != null) Undo(); if (pendingDir == null) { exit = true; Close(); } });
            var old = tray.ContextMenuStrip; tray.ContextMenuStrip = menu; if (old != null) old.Dispose();
        }
        void Toggle(string key)
        {
            if (pendingDir != null || applying) { ShowPanel(); return; }
            // Re-read actual state, since Sunshine or Windows may have changed it since the panel was opened.
            RefreshMonitors();
            var target = monitors.FirstOrDefault(m => m.Key == key); if (target == null) { status.Text = "该屏幕已断开。"; ShowPanel(); return; }
            var desired = monitors.Where(m => m.Active).Select(m => m.Key).ToList();
            if (target.Active) desired.Remove(key); else desired.Add(key);
            ApplySelection(desired);
        }
        void SetPrimary(string key)
        {
            if(pendingDir!=null || applying) { ShowPanel();return; }
            RefreshMonitors();
            if(!monitors.Any(m=>m.Key==key)) { status.Text="该屏幕已断开。";ShowPanel();return; }
            ApplySelection(monitors.Where(m=>m.Active || m.Key==key).Select(m=>m.Key),key);
        }
        void ApplySelection(IEnumerable<string> selection,string primaryKey=null)
        {
            if (pendingDir != null || applying) { ShowPanel(); return; }
            var selected = new HashSet<string>(selection);
            try
            {
                if (selected.Count == 0) throw new InvalidOperationException("至少保留一个开启的屏幕，不能全部关闭。");
                if (ProtectedKey != null && !selected.Contains(ProtectedKey)) throw new InvalidOperationException("此操作会关闭受保护的串流屏幕。请保留该屏幕，或在上方更改保护对象。");
                var data = NativeDisplay.Query(true); var current = NativeDisplay.Monitors(data);
                layoutMemory.Observe(data,current,connectionSettling.IsRunning && connectionSettling.ElapsedMilliseconds<12000); layoutMemory.TrySave();
                if (selected.Any(k => !current.Any(m => m.Key == k))) throw new InvalidOperationException("有屏幕已断开，请刷新列表。");
                if (primaryKey==null && selected.SetEquals(current.Where(m => m.Active).Select(m => m.Key))) { RefreshMonitors(); status.Text = "当前已经是这个屏幕组合。"; return; }
                var plan = layoutMemory.Plan(data,current,selected,NativeDisplay.Preferred,primaryKey);
                NativeDisplay.ValidateLayout(plan);
                pendingMonitors=current;
                BeginChange(delegate { NativeDisplay.ApplyLayout(plan); }, plan,primaryKey);
            }
            catch (Exception e) { Error(e); }
        }
        void RestoreInitial()
        {
            if (pendingDir != null || applying || initial == null) return;
            try
            {
                var data=NativeDisplay.Query(true);var current=NativeDisplay.Monitors(data);
                var plan=Recovery.PlanAvailable(initial,data,current,NativeDisplay.Preferred);
                if(plan==null) { RefreshMonitors(data,current);status.Text="启动时的屏幕已断开，已保留当前可用桌面。";return; }
                if(ProtectedKey!=null && !current.Any(m=>m.Key==ProtectedKey && plan.Paths.Any(p=>NativeDisplay.TargetKey(p)==m.TargetKey))) throw new InvalidOperationException("可恢复的配置没有包含受保护屏幕，请先更改保护对象。");
                NativeDisplay.ValidateLayout(plan);pendingMonitors=current;
                BeginChange(delegate { NativeDisplay.ApplyLayout(plan); }, plan);
            }
            catch (Exception e) { Error(e); }
        }
        void BeginChange(Action action, Snapshot plan,string primaryKey=null)
        {
            var current=NativeDisplay.Query(true);
            var connected=NativeDisplay.Monitors(current);
            NativeDisplay.RequireDevices(plan,pendingMonitors,connected);
            before = NativeDisplay.Capture(current,connected);
            if (before.Paths.Length == 0) throw new InvalidOperationException("无法备份当前屏幕配置，本次取消。");
            try { pendingDir = Recovery.Start(before); }
            catch (Exception e)
            {
                // Without a writable recovery record the change could not be rolled back.
                throw new InvalidOperationException("无法写入恢复记录（" + e.Message + "），为避免无法自动恢复，本次没有更改屏幕。" + DataHint);
            }
            pendingLayout = plan; pendingTargets = plan.Paths.Select(NativeDisplay.TargetKey).ToArray(); recoveryPasses = 0;
            pendingPrimary=primaryKey;
            applying = true; SetBusy(true); ShowPanel();
            try
            {
                File.WriteAllText(Path.Combine(pendingDir, "armed"), "armed"); elapsed = Stopwatch.StartNew();
                if (!File.Exists(Path.Combine(pendingDir, "pending"))) throw new InvalidOperationException("恢复进程准备超时，本次取消。");
                action();
                NativeDisplay.RequireTargets(NativeDisplay.Query(false), pendingTargets);
                NativeDisplay.RequireLayout(NativeDisplay.Query(false),pendingLayout);
                UpdateCurrentReadout();
                if (!File.Exists(Path.Combine(pendingDir, "pending")))
                {
                    // A very slow driver may return after the guardian already reverted.
                    Recovery.RestoreAvailable(before); throw new InvalidOperationException("切换耗时过长，已请求恢复原配置。");
                }
                status.Text = "已核对实际桌面。确认保留后，将自动收回离屏窗口；黑屏时等待自动恢复。";
                ShowPanel();
            }
            catch (Exception e) { Undo(); Error(e); }
            finally { applying = false; }
        }
        void SetBusy(bool busy)
        {
            cards.Enabled = !busy; apply.Enabled = all.Enabled = original.Enabled = refresh.Enabled = protection.Enabled = recoverWindows.Enabled = !busy;
            pendingPanel.Visible = busy; TopMost = busy;
            ((TableLayoutPanel)pendingPanel.Parent).RowStyles[4].Height = busy ? 110 : 0;
        }
        void Keep()
        {
            if (pendingDir == null || applying) return;
            try
            {
                string[] expected = pendingTargets.ToArray();
                Snapshot expectedLayout = pendingLayout;
                NativeDisplay.RequireDevices(expectedLayout,pendingMonitors,NativeDisplay.Monitors(NativeDisplay.Query(true)));
                NativeDisplay.RequireTargets(NativeDisplay.Query(false), expected);
                NativeDisplay.RequireLayout(NativeDisplay.Query(false),expectedLayout);
                if (!Recovery.Claim(pendingDir, "confirmed")) { status.Text = "倒计时已结束，正在恢复原配置。"; return; }
                layoutMemory.Accept(expectedLayout,pendingMonitors,pendingPrimary);
                FinishPending();
                try
                {
                    layoutMemory.TrySave();
                    NativeDisplay.SaveCurrent(expected,expectedLayout);
                    status.Text = "已保存屏幕组合。" + WindowRecovery.RecoverAll(Handle).Message;
                    recoveryTargets = expected; recoveryPasses = 2;
                }
                catch (Exception e) { status.Text = "已结束确认，但保存或找回窗口未完成：" + e.Message; }
                status.Text += DataWarning();
            }
            catch (Exception e) { Error(e); }
        }
        void Undo()
        {
            if (pendingDir == null) return;
            try
            {
                if (Recovery.Claim(pendingDir, "restoring")) Recovery.RestoreWithReport(pendingDir, before);
                else if (!File.Exists(Path.Combine(pendingDir, "restored")))
                {
                    if (File.Exists(Path.Combine(pendingDir, "error.txt"))) Recovery.RestoreWithReport(pendingDir, before);
                    else { status.Text = "后台正在恢复，请稍候。"; return; }
                }
                string result=Recovery.ResultMessage(pendingDir);
                FinishPending(); status.Text = result;
            }
            catch (Exception e) { Error(e); }
        }
        void FinishPending()
        {
            pendingDir = null; pendingTargets = null; pendingLayout = null; pendingPrimary=null;pendingMonitors=null; elapsed = null; SetBusy(false);
            // Refresh is safe even when called while handling a failed apply.
            bool wasApplying = applying; applying = false; RefreshMonitors(); applying = wasApplying; ShowPanel();
        }
        void TickPending()
        {
            if (pendingDir == null || elapsed == null) return;
            int left = Math.Max(0, Recovery.Seconds - (int)elapsed.Elapsed.TotalSeconds);
            countdown.Text = "确认画面是否正常？" + left + " 秒后自动恢复原配置";
            if (File.Exists(Path.Combine(pendingDir, "restored"))) { string result=Recovery.ResultMessage(pendingDir);FinishPending(); status.Text = result; }
            else if (File.Exists(Path.Combine(pendingDir, "error.txt"))) status.Text = "自动恢复未成功。请点“立即恢复”重试；必要时在 Windows 显示设置中恢复。记录：" + pendingDir;
        }
        void RecoverWindows()
        {
            if (pendingDir != null || applying) { ShowPanel(); return; }
            try { status.Text = WindowRecovery.RecoverAll(Handle).Message; ShowPanel(); }
            catch (Exception e) { Error(e); }
        }
        void CheckDesktop()
        {
            bool deviceEvent=deviceEventClock.IsRunning && deviceEventClock.ElapsedMilliseconds>=800;
            if (applying || (!deviceEvent && backgroundClock.ElapsedMilliseconds < 2000)) return;
            backgroundClock.Restart();
            try
            {
                if(deviceEvent || inventoryClock.ElapsedMilliseconds>=6000 || pendingDir!=null)
                {
                    var inventory=NativeDisplay.Query(true);var connected=NativeDisplay.Monitors(inventory);
                    // Empty/transient driver enumerations must not erase remembered devices or close the UI.
                    if(!connected.Any(m=>m.Active)) { status.Text="正在等待 Windows 完成屏幕连接…";return; }
                    deviceEventClock.Reset();inventoryClock.Restart();
                    if(pendingDir!=null)
                    {
                        bool changed=before.Devices.Length!=connected.Count || before.Devices.Any(d=>!connected.Any(m=>m.Key==d.Key && m.TargetKey==d.TargetKey));
                        if(changed) { status.Text="确认期间屏幕连接发生变化，正在恢复可用屏幕。";Undo(); }
                        else UpdateCurrentReadout(connected);
                        return;
                    }
                    if(inventorySignature!=NativeDisplay.InventorySignature(inventory,connected))
                    {
                        recoveryPasses=0;RefreshMonitors(inventory,connected);return;
                    }
                }
                var actual = NativeDisplay.Query(false);
                var shown = monitors.Where(m => m.Active).Select(m => m.TargetKey).ToArray();
                if (!NativeDisplay.TargetsMatch(actual, shown))
                {
                    recoveryPasses = 0; RefreshMonitors();
                    status.Text = "Windows 或其他程序改变了屏幕配置，已刷新实际状态。"; return;
                }
                if (recoveryPasses > 0 && NativeDisplay.TargetsMatch(actual, recoveryTargets))
                {
                    recoveryPasses--;
                    var result = WindowRecovery.RecoverAll(Handle);
                    if (result.Requested > 0 || result.Failed > 0) status.Text = result.Message;
                }
            }
            catch { recoveryPasses = 0; } // A transient hot-plug must not interrupt the UI.
        }
        void ShowPanel()
        {
            Show(); WindowState = FormWindowState.Normal;
            try
            {
            var areas=WindowRecovery.Areas();
            if(areas.Count>0 && !WindowRecovery.Reachable(Bounds,areas))
            {
                Rectangle area=(areas.FirstOrDefault(a=>a.Primary)??areas[0]).Work;
                Location = new Point(area.Left + Math.Max(0, (area.Width - Width) / 2), area.Top + Math.Max(0, (area.Height - Height) / 2));
            }
            }
            catch { } // A display driver can briefly report no working area during a hot-plug.
            Activate(); BringToFront();
        }
        // The data folder is unwritable when the program folder carries a Low integrity label
        // (sandbox or managed portable folder) or the profile is read-only. Explain it, because a
        // bare "access is denied" hides that the screen list itself is still usable.
        internal static string DataHint
        {
            get { return "屏幕管家把布局和恢复记录写在 " + LayoutMemory.DataFolder + "；请把程序放在普通文件夹（例如 C:\\Tools\\屏幕管家）中运行，或修复该目录的写入权限。"; }
        }
        internal static string DataWarning()
        {
            if (LayoutMemory.LastSaveError == null) return "";
            return "\n布局记忆无法保存：" + LayoutMemory.LastSaveError + "。屏幕仍可查看和切换，但不会记住编号和排列。" + DataHint;
        }
        void Error(Exception e) { status.Text = e.Message; ShowPanel(); }
        protected override void WndProc(ref Message m)
        {
            if(m.Msg==0x219 || m.Msg==0x7e) deviceEventClock.Restart();
            if (m.Msg == 0x312)
            {
                int raw=m.WParam.ToInt32();
                int id = raw - 100;
                if (id == 0) ShowPanel();
                else if (id > 0 && id <= 9 && id <= stableOrder.Count) SetPrimary(stableOrder[id - 1]);
                else if(raw>=201 && raw<=209 && raw-200<=stableOrder.Count) Toggle(stableOrder[raw-201]);
            }
            base.WndProc(ref m);
        }
    }
}
