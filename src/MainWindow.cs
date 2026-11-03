using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace PeStubGen
{
    public class DllRow : INotifyPropertyChanged
    {
        private bool _selected;
        private bool _isSystem;
        private string _sourceText;

        public ImportedModule Module { get; private set; }

        public DllRow(ImportedModule module)
        {
            Module = module;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Raise(string name)
        {
            PropertyChangedEventHandler h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }

        public string FileName { get { return Module.FileName; } }

        public int SymbolCount { get { return Module.Symbols.Count; } }

        public string Preview
        {
            get
            {
                StringBuilder sb = new StringBuilder();
                int n = Module.Symbols.Count < 3 ? Module.Symbols.Count : 3;
                for (int i = 0; i < n; i++)
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(Module.Symbols[i].Display);
                }
                if (Module.Symbols.Count > 3) sb.Append(", ...");
                return sb.ToString();
            }
        }

        public bool Selected
        {
            get { return _selected; }
            set
            {
                if (_selected == value) return;
                _selected = value;
                Raise("Selected");
            }
        }

        public bool IsSystem
        {
            get { return _isSystem; }
            set
            {
                if (_isSystem == value) return;
                _isSystem = value;
                Raise("IsSystem");
                Raise("VerdictText");
                Raise("TypeBadge");
            }
        }

        public string VerdictText
        {
            get { return _isSystem ? "系统" : "用户"; }
        }

        public string SourceText
        {
            get { return _sourceText; }
            set { _sourceText = value; Raise("SourceText"); }
        }

        public Brush TypeBadge
        {
            get
            {
                return _isSystem
                    ? new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A))
                    : new SolidColorBrush(Color.FromRgb(0x1F, 0x7A, 0xD4));
            }
        }

        public void ApplyVerdict(DllVerdict v)
        {
            _isSystem = v.IsSystem;
            _sourceText = v.SourceText;
            Raise("IsSystem");
            Raise("VerdictText");
            Raise("TypeBadge");
            Raise("SourceText");
        }

        public void Override(bool system)
        {
            IsSystem = system;
            SourceText = "手动指定";
        }
    }

    public class MainWindow : Window
    {
        private readonly AppSettings _settings;
        private PeInfo _info;
        private GccProbe _probe;
        private bool _busy;

        /// <summary>无头模式下不弹任何对话框（自动化验证用）。</summary>
        public bool SilentMode;

        private readonly ObservableCollection<DllRow> _rows = new ObservableCollection<DllRow>();

        private TextBox _exeBox;
        private TextBox _gccBox;
        private TextBox _outBox;
        private TextBox _logBox;
        private TextBlock _summary;
        private TextBlock _status;
        private TextBlock _gccStatus;
        private DataGrid _grid;
        private CheckBox _compileChk;
        private Button _genBtn;
        private ProgressBar _progress;

        public MainWindow(string initialPath)
        {
            _settings = AppSettings.Load();

            Title = "PE 导出桩生成器 — 补环境";
            Width = 1100;
            Height = 780;
            MinWidth = 940;
            MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");
            FontSize = 13;
            AllowDrop = true;

            BuildUi();

            Drop += OnDrop;
            DragOver += delegate(object s, DragEventArgs e)
            {
                e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            };

            Loaded += delegate
            {
                if (!string.IsNullOrEmpty(_settings.GccPath)) _gccBox.Text = _settings.GccPath;
                else
                {
                    string found = Toolchain.FindInPath();
                    if (found != null) _gccBox.Text = found;
                }
                ProbeGcc(false);

                if (!string.IsNullOrEmpty(initialPath) && File.Exists(initialPath)) LoadExe(initialPath);
                else if (!string.IsNullOrEmpty(_settings.LastExeDir) && Directory.Exists(_settings.LastExeDir))
                    AppendLog("上次使用的目录：" + _settings.LastExeDir);
            };

            Closing += delegate { SaveSettings(); };
        }

        // ---------------------------------------------------------------- UI

        private void BuildUi()
        {
            Grid root = new Grid();
            root.Margin = new Thickness(14, 12, 14, 10);
            for (int i = 0; i < 7; i++) root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions[0].Height = GridLength.Auto;
            root.RowDefinitions[1].Height = GridLength.Auto;
            root.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);
            root.RowDefinitions[3].Height = GridLength.Auto;
            root.RowDefinitions[4].Height = GridLength.Auto;
            root.RowDefinitions[5].Height = new GridLength(160);
            root.RowDefinitions[6].Height = GridLength.Auto;
            Content = root;

            // 第 0 行：目标文件
            DockPanel fileRow = new DockPanel();
            fileRow.LastChildFill = true;
            fileRow.Margin = new Thickness(0, 0, 0, 8);

            TextBlock fileLabel = MakeLabel("目标程序", 0, 0);
            DockPanel.SetDock(fileLabel, Dock.Left);
            fileRow.Children.Add(fileLabel);

            Button browse = MakeButton("选择 EXE…", 108);
            browse.Click += delegate { DoBrowse(); };
            DockPanel.SetDock(browse, Dock.Right);
            fileRow.Children.Add(browse);

            Button rescan = MakeButton("重新解析", 88);
            rescan.Margin = new Thickness(6, 0, 6, 0);
            rescan.Click += delegate { if (!string.IsNullOrEmpty(_exeBox.Text)) LoadExe(_exeBox.Text); };
            DockPanel.SetDock(rescan, Dock.Right);
            fileRow.Children.Add(rescan);

            _exeBox = MakeTextBox("把 EXE 拖到这里，或者点右侧按钮选择", true);
            _exeBox.Margin = new Thickness(0, 0, 0, 0);
            fileRow.Children.Add(_exeBox);
            Put(root, 0, fileRow);

            // 第 1 行：概要
            _summary = new TextBlock();
            _summary.Text = "尚未选择目标程序。";
            _summary.Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
            _summary.Margin = new Thickness(2, 0, 0, 6);
            _summary.TextWrapping = TextWrapping.Wrap;
            Put(root, 1, _summary);

            // 第 2 行：模块表格
            _grid = new DataGrid();
            _grid.AutoGenerateColumns = false;
            _grid.CanUserAddRows = false;
            _grid.CanUserDeleteRows = false;
            _grid.CanUserResizeRows = false;
            _grid.RowHeaderWidth = 0;
            _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
            _grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
            _grid.HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6));
            _grid.SelectionMode = DataGridSelectionMode.Extended;
            _grid.SelectionUnit = DataGridSelectionUnit.FullRow;
            _grid.ItemsSource = _rows;
            _grid.Margin = new Thickness(0, 0, 0, 8);
            
            DataGridCheckBoxColumn chk = new DataGridCheckBoxColumn();
            chk.Header = "生成";
            chk.Width = 46;
            Binding selBinding = new Binding("Selected");
            selBinding.Mode = BindingMode.TwoWay;
            selBinding.UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged;
            chk.Binding = selBinding;
            _grid.Columns.Add(chk);

            AddTextColumn("模块", "FileName", 170, true);
            AddTextColumn("符号", "SymbolCount", 50, true);
            AddTextColumn("判定", "VerdictText", 60, true);
            AddTextColumn("判定依据", "SourceText", 150, true);
            AddTextColumn("导入符号预览", "Preview", 480, true);

            Style rowStyle = new Style(typeof(DataGridRow));
            DataTrigger systemTrigger = new DataTrigger();
            systemTrigger.Binding = new Binding("IsSystem");
            systemTrigger.Value = true;
            systemTrigger.Setters.Add(new Setter(DataGridRow.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A))));
            systemTrigger.Setters.Add(new Setter(DataGridRow.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF7))));
            rowStyle.Triggers.Add(systemTrigger);
            _grid.RowStyle = rowStyle;

            Put(root, 2, _grid);

            // 第 3 行：编译器 + 输出目录
            StackPanel ops = new StackPanel();
            ops.Margin = new Thickness(0, 0, 0, 8);

            DockPanel gccRow = new DockPanel();
            gccRow.LastChildFill = true;
            gccRow.Margin = new Thickness(0, 0, 0, 6);
            TextBlock gccLabel = MakeLabel("编译器", 0, 0);
            DockPanel.SetDock(gccLabel, Dock.Left);
            gccRow.Children.Add(gccLabel);

            Button gccBrowse = MakeButton("浏览…", 74);
            gccBrowse.Margin = new Thickness(6, 0, 0, 0);
            gccBrowse.Click += delegate { BrowseGcc(); };
            DockPanel.SetDock(gccBrowse, Dock.Right);
            gccRow.Children.Add(gccBrowse);

            Button probeBtn = MakeButton("检测", 62);
            probeBtn.Margin = new Thickness(6, 0, 6, 0);
            probeBtn.Click += delegate { ProbeGcc(true); };
            DockPanel.SetDock(probeBtn, Dock.Right);
            gccRow.Children.Add(probeBtn);

            _gccBox = MakeTextBox("i686-w64-mingw32-gcc.exe 或 D:\\MinGW\\bin\\gcc.exe", false);
            gccRow.Children.Add(_gccBox);

            _gccStatus = new TextBlock();
            _gccStatus.FontSize = 12;
            _gccStatus.Margin = new Thickness(0, 0, 0, 6);
            _gccStatus.TextWrapping = TextWrapping.Wrap;

            DockPanel outRow = new DockPanel();
            outRow.LastChildFill = true;
            TextBlock outLabel = MakeLabel("输出目录", 0, 0);
            DockPanel.SetDock(outLabel, Dock.Left);
            outRow.Children.Add(outLabel);

            Button openBtn = MakeButton("打开", 62);
            openBtn.Margin = new Thickness(6, 0, 0, 0);
            openBtn.Click += delegate { OpenOutput(); };
            DockPanel.SetDock(openBtn, Dock.Right);
            outRow.Children.Add(openBtn);

            Button outBrowse = MakeButton("浏览…", 74);
            outBrowse.Margin = new Thickness(6, 0, 6, 0);
            outBrowse.Click += delegate { BrowseOutput(); };
            DockPanel.SetDock(outBrowse, Dock.Right);
            outRow.Children.Add(outBrowse);

            _outBox = MakeTextBox("选择目标程序后自动填入 <EXE 名>环境\\", false);
            outRow.Children.Add(_outBox);

            ops.Children.Add(gccRow);
            ops.Children.Add(_gccStatus);
            ops.Children.Add(outRow);
            Put(root, 3, ops);

            // 第 4 行：操作按钮
            DockPanel actions = new DockPanel();
            actions.LastChildFill = false;
            actions.Margin = new Thickness(0, 0, 0, 8);

            _genBtn = MakeButton("生成桩 DLL", 118);
            _genBtn.FontWeight = FontWeights.SemiBold;
            _genBtn.Click += delegate { StartGenerate(); };
            DockPanel.SetDock(_genBtn, Dock.Left);
            actions.Children.Add(_genBtn);

            _compileChk = new CheckBox();
            _compileChk.Content = "生成源码后立即用 gcc 编译";
            _compileChk.IsChecked = _settings.Compile;
            _compileChk.VerticalAlignment = VerticalAlignment.Center;
            _compileChk.Margin = new Thickness(14, 0, 0, 0);
            DockPanel.SetDock(_compileChk, Dock.Left);
            actions.Children.Add(_compileChk);

            StackPanel quick = new StackPanel();
            quick.Orientation = Orientation.Horizontal;
            quick.HorizontalAlignment = HorizontalAlignment.Right;
            DockPanel.SetDock(quick, Dock.Right);

            Button quickUser = MakeSmallButton("只选用户 DLL");
            quickUser.Click += delegate { SelectOnlyUser(); };
            quick.Children.Add(quickUser);

            Button quickAll = MakeSmallButton("全选");
            quickAll.Click += delegate { SelectAll(true); };
            quick.Children.Add(quickAll);

            Button quickNone = MakeSmallButton("全不选");
            quickNone.Click += delegate { SelectAll(false); };
            quick.Children.Add(quickNone);

            Button markUser = MakeSmallButton("标为用户");
            markUser.Click += delegate { MarkRows(false); };
            quick.Children.Add(markUser);

            Button markSys = MakeSmallButton("标为系统");
            markSys.Click += delegate { MarkRows(true); };
            quick.Children.Add(markSys);

            actions.Children.Add(quick);
            Put(root, 4, actions);

            // 第 5 行：日志
            GroupBox logGroup = new GroupBox();
            logGroup.Header = "日志";
            logGroup.Margin = new Thickness(0, 0, 0, 8);
            logGroup.Padding = new Thickness(6);

            _logBox = new TextBox();
            _logBox.IsReadOnly = true;
            _logBox.AcceptsReturn = true;
            _logBox.TextWrapping = TextWrapping.NoWrap;
            _logBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _logBox.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            _logBox.FontFamily = new FontFamily("Consolas, 微软雅黑");
            _logBox.FontSize = 12;
            _logBox.Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA));
            _logBox.BorderThickness = new Thickness(0);
            logGroup.Content = _logBox;
            Put(root, 5, logGroup);

            // 第 6 行：状态条
            DockPanel statusBar = new DockPanel();
            statusBar.LastChildFill = true;

            _progress = new ProgressBar();
            _progress.Width = 160;
            _progress.Height = 16;
            _progress.IsIndeterminate = true;
            _progress.Visibility = Visibility.Collapsed;
            _progress.Margin = new Thickness(0, 0, 10, 0);
            _progress.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(_progress, Dock.Right);
            statusBar.Children.Add(_progress);

            _status = new TextBlock();
            _status.Text = "就绪";
            _status.Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
            _status.VerticalAlignment = VerticalAlignment.Center;
            statusBar.Children.Add(_status);
            Put(root, 6, statusBar);
        }

        private static void Put(Grid grid, int row, UIElement element)
        {
            Grid.SetRow(element, row);
            grid.Children.Add(element);
        }

        private static TextBlock MakeLabel(string text, double left, double right)
        {
            TextBlock tb = new TextBlock();
            tb.Text = text;
            tb.MinWidth = 64;
            tb.VerticalAlignment = VerticalAlignment.Center;
            tb.Margin = new Thickness(left, 0, right, 0);
            return tb;
        }

        private static TextBox MakeTextBox(string hint, bool readOnly)
        {
            TextBox tb = new TextBox();
            tb.Height = 28;
            tb.VerticalContentAlignment = VerticalAlignment.Center;
            tb.Padding = new Thickness(4, 0, 4, 0);
            tb.IsReadOnly = readOnly;
            if (!string.IsNullOrEmpty(hint) && !readOnly) tb.ToolTip = hint;
            return tb;
        }

        private static Button MakeButton(string text, double width)
        {
            Button b = new Button();
            b.Content = text;
            b.Width = width;
            b.Height = 28;
            b.Margin = new Thickness(0);
            b.Padding = new Thickness(0);
            return b;
        }

        private static Button MakeSmallButton(string text)
        {
            Button b = new Button();
            b.Content = text;
            b.Height = 26;
            b.Padding = new Thickness(10, 0, 10, 0);
            b.Margin = new Thickness(6, 0, 0, 0);
            return b;
        }

        private void AddTextColumn(string header, string path, double width, bool readOnly)
        {
            DataGridTextColumn col = new DataGridTextColumn();
            col.Header = header;
            col.Binding = new Binding(path);
            col.Width = new DataGridLength(width, DataGridLengthUnitType.Pixel);
            col.IsReadOnly = readOnly;
            _grid.Columns.Add(col);
        }

        // ------------------------------------------------------------- 行为

        private void DoBrowse()
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Title = "选择要补环境的目标程序";
            dlg.Filter = "可执行文件 (*.exe;*.dll)|*.exe;*.dll|所有文件 (*.*)|*.*";
            if (!string.IsNullOrEmpty(_settings.LastExeDir) && Directory.Exists(_settings.LastExeDir))
                dlg.InitialDirectory = _settings.LastExeDir;
            if (dlg.ShowDialog(this) == true) LoadExe(dlg.FileName);
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files == null || files.Length == 0) return;
            string file = files[0];
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext != ".exe" && ext != ".dll")
            {
                MessageBox.Show("只接受 .exe 或 .dll 文件。", "PE 导出桩生成器", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            LoadExe(file);
        }

        private void LoadExe(string path)
        {
            try
            {
                Cursor = Cursors.Wait;
                PeInfo info = PeImageReader.Read(path);
                _info = info;
                _exeBox.Text = info.FilePath;

                string exeDir = Path.GetDirectoryName(info.FilePath);
                _settings.LastExeDir = exeDir;
                _outBox.Text = Path.Combine(exeDir, Path.GetFileNameWithoutExtension(info.FilePath) + "环境");

                _rows.Clear();
                int userCount = 0;
                for (int i = 0; i < info.Modules.Count; i++)
                {
                    ImportedModule m = info.Modules[i];
                    DllRow row = new DllRow(m);
                    DllVerdict v = DllClassifier.Classify(m, info.Is64Bit, exeDir);
                    row.ApplyVerdict(v);
                    row.Selected = !v.IsSystem;
                    if (!v.IsSystem) userCount++;
                    row.PropertyChanged += delegate { UpdateCounts(); };
                    _rows.Add(row);
                }

                AppendLog("解析完成：" + info.FilePath);
                AppendLog("  " + info.ArchName + " / " + info.MachineText + "，导入模块 " + info.Modules.Count
                    + " 个，导入符号 " + info.SymbolCount + " 个，其中用户 DLL " + userCount + " 个。");
                if (info.Is64Bit)
                    AppendLog("  注意：目标是 64 位，需要 x86_64 工具链；32 位 gcc 不能出 64 位 DLL。");
                else
                    AppendLog("  注意：目标是 32 位，必须用 32 位工具链（i686 / mingw32）。");

                UpdateCounts();
                _status.Text = "解析完成";
            }
            catch (Exception ex)
            {
                _info = null;
                _rows.Clear();
                UpdateCounts();
                AppendLog("解析失败：" + ex.Message);
                MessageBox.Show("解析失败：\r\n\r\n" + ex.Message, "PE 导出桩生成器", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Cursor = Cursors.Arrow;
            }
        }

        private void UpdateCounts()
        {
            if (_info == null)
            {
                _summary.Text = "尚未选择目标程序。";
                return;
            }
            int selected = 0;
            int user = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Selected) selected++;
                if (!_rows[i].IsSystem) user++;
            }
            _summary.Text = string.Format(
                "{0} · {1} · ImageBase 0x{2} · 导入 {3} 个模块 / {4} 个符号 · 用户 DLL {5} 个 · 已勾选 {6} 个",
                _info.ArchName, _info.MachineText, _info.ImageBase.ToString("X"),
                _info.Modules.Count, _info.SymbolCount, user, selected);
        }

        private void SelectOnlyUser()
        {
            for (int i = 0; i < _rows.Count; i++) _rows[i].Selected = !_rows[i].IsSystem;
            UpdateCounts();
        }

        private void SelectAll(bool value)
        {
            for (int i = 0; i < _rows.Count; i++) _rows[i].Selected = value;
            UpdateCounts();
        }

        private void MarkRows(bool system)
        {
            if (_grid.SelectedItems == null || _grid.SelectedItems.Count == 0)
            {
                MessageBox.Show("先在表格里选中要改判定的行（可以按住 Ctrl 多选）。", "PE 导出桩生成器",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            foreach (object o in _grid.SelectedItems)
            {
                DllRow r = o as DllRow;
                if (r != null)
                {
                    r.Override(system);
                    r.Selected = !system;
                }
            }
            UpdateCounts();
        }

        private void BrowseGcc()
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Title = "选择 gcc.exe（32 位目标要用 32 位编译器）";
            dlg.Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*";
            if (dlg.ShowDialog(this) == true)
            {
                _gccBox.Text = dlg.FileName;
                ProbeGcc(true);
            }
        }

        private void ProbeGcc(bool verbose)
        {
            string path = _gccBox.Text;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                _probe = null;
                _gccStatus.Text = "未找到 gcc。可以手动指定路径，或把 MinGW 的 bin 目录加进 PATH。";
                _gccStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));
                if (verbose) AppendLog("编译器检测：" + _gccStatus.Text);
                return;
            }

            _probe = Toolchain.Probe(path);
            if (!_probe.Ok)
            {
                _gccStatus.Text = "编译器不可用：" + _probe.Error;
                _gccStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));
            }
            else
            {
                bool okForTarget = _info == null || _probe.Bits == (_info.Is64Bit ? 64 : 32);
                _gccStatus.Text = _probe.Triple + " · " + _probe.Bits + " 位 · " + _probe.Version
                    + (okForTarget ? "" : "  ← 与目标程序位数不符！");
                _gccStatus.Foreground = okForTarget
                    ? new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32))
                    : new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));
            }
            if (verbose) AppendLog("编译器检测：" + _probe.Summary);
            _settings.GccPath = path;
        }

        private void BrowseOutput()
        {
            System.Windows.Forms.FolderBrowserDialog dlg = new System.Windows.Forms.FolderBrowserDialog();
            dlg.Description = "选择输出目录（桩 DLL 会平铺放进去，源码放 src 子目录）";
            if (!string.IsNullOrEmpty(_outBox.Text) && Directory.Exists(_outBox.Text))
                dlg.SelectedPath = _outBox.Text;
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) _outBox.Text = dlg.SelectedPath;
        }

        private void OpenOutput()
        {
            string dir = _outBox.Text;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                MessageBox.Show("输出目录还不存在，先生成一次再打开。", "PE 导出桩生成器",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try { Process.Start("explorer.exe", "\"" + dir + "\""); }
            catch (Exception ex) { AppendLog("打开目录失败：" + ex.Message); }
        }

        private void StartGenerate()
        {
            if (_busy) return;
            if (_info == null)
            {
                MessageBox.Show("先选择目标程序。", "PE 导出桩生成器", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            List<string> selected = new List<string>();
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Selected && !_rows[i].IsSystem) selected.Add(_rows[i].FileName);
            }
            if (selected.Count == 0)
            {
                MessageBox.Show("没有勾选任何用户 DLL。系统 DLL 不会被生成的。", "PE 导出桩生成器",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            bool compile = _compileChk.IsChecked == true;
            if (compile)
            {
                GccProbe p = Toolchain.Probe(_gccBox.Text);
                string problem = Toolchain.CheckForTarget(p, _info.Is64Bit);
                if (problem != null)
                {
                    MessageBox.Show("编译器不可用：\r\n\r\n" + problem, "PE 导出桩生成器",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                _probe = p;
            }

            string outDir = _outBox.Text;
            if (string.IsNullOrEmpty(outDir))
            {
                MessageBox.Show("输出目录为空。", "PE 导出桩生成器", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            GenerateOptions opt = new GenerateOptions();
            opt.ExePath = _info.FilePath;
            opt.OutputDir = outDir;
            opt.Compile = compile;
            opt.GccPath = _gccBox.Text;
            opt.GccTriple = _probe == null ? null : _probe.Triple;
            opt.DllFileNames = selected;

            PeInfo info = _info;
            _settings.Compile = compile;
            _settings.LastOutputDir = outDir;
            SaveSettings();

            SetBusy(true);
            _logBox.Clear();
            AppendLog("开始生成：共 " + selected.Count + " 个用户 DLL"
                + (compile ? "，生成后编译。" : "，只生成源码。"));

            Task.Factory.StartNew(delegate
            {
                Generator gen = new Generator(AppendLog);
                GenerateResult r = gen.Run(info, opt);

                Dispatcher.Invoke(new Action(delegate
                {
                    SetBusy(false);
                    if (!string.IsNullOrEmpty(r.Error))
                    {
                        _status.Text = "失败：" + r.Error;
                        Notify("生成失败：\r\n\r\n" + r.Error, MessageBoxImage.Error);
                        return;
                    }
                    _status.Text = "完成：成功 " + r.OkCount + " 个，失败 " + r.FailCount + " 个";
                    AppendLog("输出目录：" + r.OutputDir);
                    if (compile)
                    {
                        if (r.FailCount == 0)
                        {
                            Notify("全部完成：" + r.OkCount + " 个桩 DLL 已生成并校验通过。\r\n\r\n"
                                + r.OutputDir + "\r\n\r\n把这些 DLL 放到目标 EXE 同目录再试一次。",
                                MessageBoxImage.Information);
                        }
                        else
                        {
                            Notify("部分 DLL 失败（" + r.FailCount + " 个）。看日志面板和 build.log 里的 gcc 输出。",
                                MessageBoxImage.Warning);
                        }
                    }
                    else
                    {
                        Notify("源码已生成到 " + r.SrcDir + "\r\n\r\n编好之后记得把 DLL 放到 EXE 同目录。",
                            MessageBoxImage.Information);
                    }
                }));
            });
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _genBtn.IsEnabled = !busy;
            _progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            _status.Text = busy ? "正在生成…" : "就绪";
            Cursor = busy ? Cursors.Wait : Cursors.Arrow;
        }

        private void AppendLog(string line)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action<string>(AppendLog), line);
                return;
            }
            _logBox.AppendText(line + "\r\n");
            _logBox.ScrollToEnd();
        }

        /// <summary>离屏截图模式用：加载目标程序并刷新界面。</summary>
        public void LoadForShot(string path)
        {
            LoadExe(path);
            UpdateLayout();
        }

        public bool IsBusy { get { return _busy; } }

        public string StatusText { get { return _status.Text; } }

        public string LogText { get { return _logBox.Text; } }

        public void SetOutputDir(string dir) { _outBox.Text = dir; }

        public void SetCompile(bool value) { _compileChk.IsChecked = value; }

        public void StartGenerateForShot() { StartGenerate(); }

        private void Notify(string text, MessageBoxImage icon)
        {
            AppendLog(text.Replace("\r\n", " "));
            if (SilentMode) return;
            MessageBox.Show(text, "PE 导出桩生成器", MessageBoxButton.OK, icon);
        }

        private void SaveSettings()
        {
            _settings.GccPath = _gccBox.Text;
            if (_info != null) _settings.LastExeDir = Path.GetDirectoryName(_info.FilePath);
            if (_compileChk != null) _settings.Compile = _compileChk.IsChecked == true;
            _settings.Save();
        }
    }
}
