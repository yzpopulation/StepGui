using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace StepGui
{
    public partial class Form1 : Form
    {
        private const string FileFilterCert = "证书 (*.crt;*.pem;*.cer;*.der)|*.crt;*.pem;*.cer;*.der|所有文件 (*.*)|*.*";
        private const string FileFilterAll = "所有文件 (*.*)|*.*";
        private static readonly string[] KnownExts = { ".crt", ".pem", ".der", ".csr", ".key", ".p12", ".pfx" };

        private SplitContainer _split;
        private TreeView _tree;
        private TabControl _tabs;
        private TextBox _log;
        private StatusStrip _status;
        private ToolStripStatusLabel _statusLabel;
        private IProgress<string> _logProgress;
        private bool _busy;
        private bool _refreshing;

        private TextBox _rootCn, _rootPrefix, _rootPwd;
        private ComboBox _rootKty, _rootDur;

        private TextBox _intCa, _intCaKey, _intCaPwd, _intCn, _intPrefix, _intPwd;
        private ComboBox _intKty, _intDur;

        private TextBox _leafCa, _leafCaKey, _leafCaPwd, _leafCn, _leafSans, _leafPrefix, _leafPwd, _leafP12Pwd;
        private ComboBox _leafKty, _leafDur;
        private CheckBox _leafBundle, _leafP12, _leafP12Legacy;

        private TextBox _codeCa, _codeCaKey, _codeCaPwd, _codeCn, _codePrefix, _codePwd;
        private ComboBox _codeKty, _codeDur;

        private TextBox _inspect, _curFileTxt, _verifyRoot, _verifyInt, _p12Key, _p12Ca, _p12Pwd;
        private CheckBox _p12Legacy;
        private TabPage _detailsPage;

        private string _currentFile;
        private readonly Dictionary<string, CertMeta> _metaCache = new Dictionary<string, CertMeta>(StringComparer.OrdinalIgnoreCase);

        private static float _dpi = 96f;

        private static int S(int v)
        {
            return (int)Math.Round(v * _dpi / 96f);
        }

        private static Size SSize(int w, int h)
        {
            return new Size(S(w), S(h));
        }

        public Form1()
        {
            InitializeComponent();
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) _dpi = g.DpiX;
            AppConfig.Load();
            _logProgress = new Progress<string>(AppendLog);
            BuildUi();
            Log("step.exe : " + AppConfig.StepPath + (File.Exists(AppConfig.StepPath) ? "" : "   (未找到!)"));
            Log("证书库   : " + AppConfig.StorePath);
            InitDefaults();
            var _ = RefreshTreeAsync();
        }

        private void InitDefaults()
        {
            string rootCrt = P("root_ca.crt");
            string rootKey = P("root_ca.key");
            string intCrt = P("intermediate_ca.crt");
            string intKey = P("intermediate_ca.key");
            _intCa.Text = rootCrt;
            _intCaKey.Text = rootKey;
            _leafCa.Text = intCrt;
            _leafCaKey.Text = intKey;
            if (File.Exists(rootCrt)) _verifyRoot.Text = rootCrt;
            if (File.Exists(intCrt))
            {
                _p12Ca.Text = intCrt;
                _codeCa.Text = intCrt;
                _codeCaKey.Text = intKey;
            }
        }

        private void BuildUi()
        {
            ClientSize = SSize(1150, 720);
            MinimumSize = SSize(980, 620);
            StartPosition = FormStartPosition.CenterScreen;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, S(180)));
            Controls.Add(root);

            _split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterWidth = S(6),
                FixedPanel = FixedPanel.Panel1
            };
            _split.Panel1.Controls.Add(BuildLeftPanel());
            _tabs = new TabControl { Dock = DockStyle.Fill };
            _tabs.TabPages.Add(BuildRootTab());
            _tabs.TabPages.Add(BuildIntTab());
            _tabs.TabPages.Add(BuildLeafTab());
            _tabs.TabPages.Add(BuildCodeTab());
            _tabs.TabPages.Add(BuildDetailsTab());
            _split.Panel2.Controls.Add(_tabs);
            root.Controls.Add(_split, 0, 0);
            try
            {
                _split.Panel1MinSize = S(240);
                _split.Panel2MinSize = S(400);
                _split.SplitterDistance = S(260);
            }
            catch { }

            var logGroup = new GroupBox { Dock = DockStyle.Fill, Text = "step.exe 输出" };
            _log = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Font = new Font("Consolas", 9F)
            };
            logGroup.Controls.Add(_log);
            root.Controls.Add(logGroup, 0, 1);

            _status = new StatusStrip();
            _statusLabel = new ToolStripStatusLabel("就绪");
            _status.Items.Add(_statusLabel);
            Controls.Add(_status);
        }

        private TableLayoutPanel BuildLeftPanel()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, S(95)));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var bar = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, RowCount = 2, Height = 2 * S(45) };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            bar.RowStyles.Add(new RowStyle(SizeType.Absolute, S(45)));
            bar.RowStyles.Add(new RowStyle(SizeType.Absolute, S(45)));
            BarBtn(bar, 0, 0, "刷新", async delegate { await RefreshTreeAsync(); });
            BarBtn(bar, 1, 0, "导入...", BtnImport_Click);
            BarBtn(bar, 0, 1, "打开", BtnStoreOpen_Click);
            BarBtn(bar, 1, 1, "目录...", BtnStoreChange_Click);
            panel.Controls.Add(bar, 0, 0);

            _tree = new TreeView
            {
                Dock = DockStyle.Fill,
                HideSelection = false,
                ShowLines = true
            };
            _tree.AfterSelect += Tree_AfterSelect;
            _tree.DoubleClick += Tree_DoubleClick;
            _tree.ContextMenuStrip = BuildTreeMenu();
            panel.Controls.Add(_tree, 0, 1);
            return panel;
        }

        private static void BarBtn(TableLayoutPanel bar, int col, int row, string text, EventHandler onClick)
        {
            var b = new Button { Text = text, Dock = DockStyle.Fill, Margin = new Padding(3, 4, 3, 4) };
            b.Click += onClick;
            bar.Controls.Add(b, col, row);
        }

        private ContextMenuStrip BuildTreeMenu()
        {
            var m = new ContextMenuStrip();
            m.Items.Add("查看文本", null, delegate { ViewCurrent(false); });
            m.Items.Add("查看 JSON", null, delegate { ViewCurrent(true); });
            m.Items.Add("指纹", null, BtnFingerprint_Click);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("转为 PEM", null, BtnToPem_Click);
            m.Items.Add("验证证书链...", null, MenuVerify_Click);
            m.Items.Add("导出 PFX...", null, MenuExportP12_Click);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("打开所在文件夹", null, MenuOpenFolder_Click);
            m.Items.Add("删除", null, MenuDelete_Click);
            return m;
        }

        private TabPage BuildRootTab()
        {
            var page = new TabPage("根证书 (Root CA)");
            var g = Grid();
            _rootCn = Txt("My Root CA");
            Row(g, "Common Name (CN)", _rootCn);
            _rootKty = ComboDrop("RSA 2048", "RSA 3072", "RSA 4096", "EC P-256", "EC P-384", "EC P-521", "Ed25519");
            Row(g, "密钥类型", _rootKty);
            _rootDur = ComboEdit("87600h", "87600h", "43800h", "8760h", "2160h", "720h", "24h");
            Row(g, "有效期 (--not-after)", _rootDur);
            _rootPwd = PwdBox();
            Row(g, "私钥密码(可空)", _rootPwd);
            _rootPrefix = Txt("root_ca");
            Row(g, "输出文件名(前缀)", _rootPrefix);
            Row(g, "说明", Lbl("生成自签名根 CA，输出 前缀.crt 与 前缀.key"));
            RowAction(g, Btn("生成根证书", BtnRootCreate_Click));
            page.Controls.Add(g);
            return page;
        }

        private TabPage BuildIntTab()
        {
            var page = new TabPage("中间证书 (Intermediate CA)");
            var g = Grid();
            _intCa = Txt("");
            Row(g, "根CA证书", PathField(_intCa, Btn("浏览...", delegate { BrowseTo(_intCa, FileFilterCert); })));
            _intCaKey = Txt("");
            Row(g, "根CA私钥", PathField(_intCaKey, Btn("浏览...", delegate { BrowseTo(_intCaKey, FileFilterAll); })));
            _intCaPwd = PwdBox();
            Row(g, "根CA私钥密码", _intCaPwd);
            _intCn = Txt("My Intermediate CA");
            Row(g, "Common Name (CN)", _intCn);
            _intKty = ComboDrop("RSA 2048", "RSA 3072", "RSA 4096", "EC P-256", "EC P-384", "EC P-521", "Ed25519");
            Row(g, "密钥类型", _intKty);
            _intDur = ComboEdit("43800h", "87600h", "43800h", "8760h", "2160h", "720h", "24h");
            Row(g, "有效期 (--not-after)", _intDur);
            _intPwd = PwdBox();
            Row(g, "私钥密码(可空)", _intPwd);
            _intPrefix = Txt("intermediate_ca");
            Row(g, "输出文件名(前缀)", _intPrefix);
            RowAction(g, Btn("生成中间证书", BtnIntCreate_Click));
            page.Controls.Add(g);
            return page;
        }

        private TabPage BuildLeafTab()
        {
            var page = new TabPage("网站证书 (Leaf)");
            var g = Grid();
            _leafCa = Txt("");
            Row(g, "签发CA证书", PathField(_leafCa, Btn("浏览...", delegate { BrowseTo(_leafCa, FileFilterCert); })));
            _leafCaKey = Txt("");
            Row(g, "签发CA私钥", PathField(_leafCaKey, Btn("浏览...", delegate { BrowseTo(_leafCaKey, FileFilterAll); })));
            _leafCaPwd = PwdBox();
            Row(g, "签发CA私钥密码", _leafCaPwd);
            _leafCn = Txt("*.example.com");
            Row(g, "主题 CN / 域名", _leafCn);
            _leafSans = new TextBox
            {
                Multiline = true,
                MinimumSize = new Size(0, S(100)),
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Text = "*.example.com\r\nexample.com"
            };
            Row(g, "SAN(每行一个)", _leafSans, 116);
            Row(g, "提示", Lbl("支持普通域名、*.example.com 通配符、IPv4/IPv6"));
            _leafKty = ComboDrop("RSA 2048", "RSA 3072", "RSA 4096", "EC P-256", "EC P-384", "EC P-521");
            Row(g, "密钥类型", _leafKty);
            _leafDur = ComboEdit("8760h", "8760h", "2160h", "720h", "24h");
            Row(g, "有效期 (--not-after)", _leafDur);
            _leafPwd = PwdBox();
            Row(g, "私钥密码(可空)", _leafPwd);
            _leafPrefix = Txt("server");
            Row(g, "输出文件名(前缀)", _leafPrefix);
            _leafBundle = Chk("捆绑签发链 (--bundle)", true);
            _leafP12 = Chk("同时导出 PFX (.p12)", true);
            RowFlow(g, "输出选项", _leafBundle, _leafP12);
            _leafP12Pwd = PwdBox();
            Row(g, "PFX 密码(可空)", _leafP12Pwd);
            _leafP12Legacy = Chk("PFX 兼容旧系统 (--legacy)", false);
            RowFlow(g, "PFX 选项", _leafP12Legacy);
            RowAction(g, Btn("生成网站证书", BtnLeafCreate_Click));
            page.Controls.Add(g);
            return page;
        }

        private TabPage BuildCodeTab()
        {
            var page = new TabPage("代码签名 (Code Signing)");
            var g = Grid();
            _codeCa = Txt("");
            Row(g, "签发CA证书", PathField(_codeCa, Btn("浏览...", delegate { BrowseTo(_codeCa, FileFilterCert); })));
            _codeCaKey = Txt("");
            Row(g, "签发CA私钥", PathField(_codeCaKey, Btn("浏览...", delegate { BrowseTo(_codeCaKey, FileFilterAll); })));
            _codeCaPwd = PwdBox();
            Row(g, "签发CA私钥密码", _codeCaPwd);
            _codeCn = Txt("My Code Signing");
            Row(g, "Common Name (CN)", _codeCn);
            _codeKty = ComboDrop("RSA 2048", "RSA 3072", "RSA 4096", "EC P-256", "EC P-384", "EC P-521");
            Row(g, "密钥类型", _codeKty);
            _codeDur = ComboEdit("8760h", "8760h", "2160h", "720h", "24h");
            Row(g, "有效期 (--not-after)", _codeDur);
            _codePwd = PwdBox();
            Row(g, "私钥密码(可空)", _codePwd);
            _codePrefix = Txt("codesign");
            Row(g, "输出文件名(前缀)", _codePrefix);
            Row(g, "说明", Lbl("EKU=codeSigning，配合私钥给程序/脚本签名"));
            RowAction(g, Btn("生成代码签名证书", BtnCodeCreate_Click));
            page.Controls.Add(g);
            return page;
        }

        private TabPage BuildDetailsTab()
        {
            _detailsPage = new TabPage("证书详情 / 工具");
            var g = Grid();
            _curFileTxt = Txt("");
            _curFileTxt.ReadOnly = true;
            _curFileTxt.BackColor = Color.WhiteSmoke;
            Row(g, "当前选择", _curFileTxt);
            RowFlow(g, "查看", Btn("查看文本", delegate { ViewCurrent(false); }),
                Btn("查看 JSON", delegate { ViewCurrent(true); }),
                Btn("指纹", BtnFingerprint_Click));
            RowFlow(g, "操作", Btn("转为 PEM", BtnToPem_Click),
                Btn("打开所在文件夹", MenuOpenFolder_Click),
                Btn("删除", MenuDelete_Click));
            _verifyRoot = Txt("");
            Row(g, "验证-Root证书", PathField(_verifyRoot, Btn("浏览...", delegate { BrowseTo(_verifyRoot, FileFilterCert); })));
            _verifyInt = Txt("");
            Row(g, "验证-中间证书", PathField(_verifyInt, Btn("浏览...", delegate { BrowseTo(_verifyInt, FileFilterCert); })));
            RowFlow(g, "验证", Btn("验证证书链 (RFC5280)", BtnVerify_Click));
            _p12Key = Txt("");
            Row(g, "PFX-私钥", PathField(_p12Key, Btn("浏览...", delegate { BrowseTo(_p12Key, FileFilterAll); })));
            _p12Ca = Txt("");
            Row(g, "PFX-含CA证书", PathField(_p12Ca, Btn("浏览...", delegate { BrowseTo(_p12Ca, FileFilterCert); })));
            _p12Pwd = PwdBox();
            Row(g, "PFX 密码(可空)", _p12Pwd);
            _p12Legacy = Chk("兼容旧系统 (--legacy)", false);
            RowFlow(g, "导出", _p12Legacy, Btn("导出 PFX (.p12)", BtnExportP12_Click));
            RowFill(g, _inspect = NewInspectBox());
            _detailsPage.Controls.Add(g);
            return _detailsPage;
        }

        private static TextBox NewInspectBox()
        {
            return new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                Dock = DockStyle.Fill,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                BackColor = Color.White,
                Font = new Font("Consolas", 9.25F),
                Text = "双击左侧树中的证书查看详情 (step certificate inspect)"
            };
        }

        private static TableLayoutPanel Grid()
        {
            var t = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(S(12), S(10), S(12), S(6)),
                ColumnCount = 2
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(165)));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return t;
        }

        private static void AddCell(TableLayoutPanel t, int col, int row, Control c, bool fill = true)
        {
            if (fill) c.Dock = DockStyle.Fill;
            c.Margin = new Padding(3, 4, 3, 4);
            t.Controls.Add(c, col, row);
        }

        private int NextRow(TableLayoutPanel t, int height)
        {
            int r = t.RowCount;
            t.RowCount = r + 1;
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, S(height)));
            return r;
        }

        private static Label MakeLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(3, 4, 6, 4)
            };
        }

        private void Row(TableLayoutPanel t, string label, Control main, int height = 32)
        {
            int r = NextRow(t, height);
            if (label != null) t.Controls.Add(MakeLabel(label), 0, r);
            AddCell(t, 1, r, main);
        }

        private void RowAction(TableLayoutPanel t, Button b)
        {
            Row(t, null, Flow(b), 44);
        }

        private void RowFlow(TableLayoutPanel t, string label, params Control[] items)
        {
            if (items == null || items.Length == 0) return;
            Row(t, label, Flow(items), 40);
        }

        private static FlowLayoutPanel Flow(params Control[] items)
        {
            var f = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = true
            };
            foreach (var c in items.Where(c => c != null))
            {
                c.AutoSize = true;
                c.Margin = new Padding(3, 6, 12, 3);
                f.Controls.Add(c);
            }
            return f;
        }

        private void RowFill(TableLayoutPanel t, Control c)
        {
            int r = t.RowCount;
            t.RowCount = r + 1;
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            AddCell(t, 0, r, c);
            t.SetColumnSpan(c, 2);
        }

        private static Control PathField(TextBox tb, Button btn)
        {
            var p = new Panel { Dock = DockStyle.Fill };
            tb.Dock = DockStyle.Fill;
            p.Controls.Add(tb);
            btn.AutoSize = false;
            btn.Dock = DockStyle.Right;
            btn.Width = S(78);
            p.Controls.Add(btn);
            return p;
        }

        private static Label Lbl(string text)
        {
            return new Label { Text = text, AutoSize = true, Margin = new Padding(3, 8, 3, 2) };
        }

        private static TextBox Txt(string text)
        {
            return new TextBox { Text = text ?? "" };
        }

        private static TextBox PwdBox()
        {
            return new TextBox { UseSystemPasswordChar = true };
        }

        private static ComboBox ComboDrop(params string[] items)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            c.Items.AddRange(items);
            c.SelectedIndex = 0;
            return c;
        }

        private static ComboBox ComboEdit(string text, params string[] items)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
            c.Items.AddRange(items);
            c.Text = text;
            return c;
        }

        private static Button Btn(string text, EventHandler onClick)
        {
            var b = new Button { Text = text, AutoSize = true };
            b.Click += onClick;
            return b;
        }

        private static CheckBox Chk(string text, bool check)
        {
            return new CheckBox { Text = text, Checked = check, AutoSize = true, Margin = new Padding(3, 7, 12, 2) };
        }

        private string P(string name)
        {
            return Path.Combine(AppConfig.StorePath, name);
        }

        private void Log(string s)
        {
            AppendLog(s);
        }

        private void AppendLog(string s)
        {
            if (_log == null) return;
            if (_log.TextLength > 300000) _log.Clear();
            _log.AppendText(s + Environment.NewLine);
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _tabs.Enabled = !busy;
            UseWaitCursor = busy;
            _statusLabel.Text = busy ? "正在执行 step ..." : "就绪";
        }

        private async Task<StepResult> RunStep(string args)
        {
            if (_busy)
            {
                Warn("正在执行另一个操作，请稍候...");
                return null;
            }
            if (!File.Exists(AppConfig.StepPath))
            {
                Warn("未找到 step.exe:\r\n" + AppConfig.StepPath);
                return null;
            }
            SetBusy(true);
            try
            {
                return await StepCli.RunAsync(AppConfig.StepPath, args, AppConfig.StorePath, _logProgress);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private static string KeyArgs(ComboBox cbo)
        {
            string v = cbo.SelectedItem as string;
            if (string.IsNullOrEmpty(v)) v = "EC P-256";
            if (v == "Ed25519") return "--kty OKP";
            if (v.StartsWith("EC", StringComparison.Ordinal)) return "--kty EC --curve " + v.Substring(3);
            if (v.StartsWith("RSA", StringComparison.Ordinal))
            {
                string[] parts = v.Split(' ');
                return "--kty RSA --size " + (parts.Length > 1 ? parts[1] : "2048");
            }
            return "--kty EC --curve P-256";
        }

        private static string Dur(ComboBox cbo)
        {
            string s = (cbo.Text ?? "").Trim();
            return s.Length == 0 ? "8760h" : s;
        }

        private static string SafeName(string s, string fallback)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            s = s.Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            string lower = s.ToLowerInvariant();
            foreach (string ext in KnownExts)
            {
                if (lower.EndsWith(ext, StringComparison.Ordinal))
                {
                    s = s.Substring(0, s.Length - ext.Length);
                    break;
                }
            }
            return s.Length == 0 ? fallback : s;
        }

        private bool ConfirmOverwrite(IEnumerable<string> files)
        {
            var existing = files.Where(File.Exists).ToList();
            if (existing.Count == 0) return true;
            string msg = "以下文件已存在，覆盖吗？" + Environment.NewLine + string.Join(Environment.NewLine, existing);
            return MessageBox.Show(this, msg, "覆盖确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        private static void DeleteFiles(IEnumerable<string> files)
        {
            foreach (string f in files)
            {
                try { if (File.Exists(f)) File.Delete(f); } catch { }
            }
        }

        private void Warn(string msg)
        {
            MessageBox.Show(this, msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void Info(string msg)
        {
            MessageBox.Show(this, msg, "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void BrowseTo(TextBox target, string filter)
        {
            using (var dlg = new OpenFileDialog { Filter = filter, Title = "选择文件" })
            {
                if (File.Exists(target.Text)) dlg.FileName = target.Text;
                else dlg.InitialDirectory = AppConfig.StorePath;
                if (dlg.ShowDialog(this) == DialogResult.OK) target.Text = dlg.FileName;
            }
        }

        private static bool CaOk(TextBox txt, string name, out string path)
        {
            path = txt.Text.Trim();
            if (path.Length == 0 || !File.Exists(path))
            {
                MessageBox.Show("请先选择有效的" + name + ":\r\n" + path, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return true;
        }

        private async void BtnRootCreate_Click(object sender, EventArgs e)
        {
            string cn = _rootCn.Text.Trim();
            if (cn.Length == 0) { Warn("请填写 Common Name"); return; }
            string prefix = SafeName(_rootPrefix.Text, "root_ca");
            string crt = P(prefix + ".crt");
            string key = P(prefix + ".key");
            if (!ConfirmOverwrite(new[] { crt, key })) return;
            DeleteFiles(new[] { crt, key });
            string pwd = _rootPwd.Text ?? "";
            string pwdFile = pwd.Length > 0 ? StepCli.WritePasswordFile(pwd) : null;
            try
            {
                var sb = new StringBuilder("certificate create ");
                sb.Append(StepCli.Quote(cn)).Append(' ').Append(StepCli.Quote(crt)).Append(' ').Append(StepCli.Quote(key));
                sb.Append(" --profile root-ca");
                sb.Append(" --not-after ").Append(StepCli.Quote(Dur(_rootDur)));
                sb.Append(' ').Append(KeyArgs(_rootKty));
                sb.Append(pwdFile != null ? " --password-file " + StepCli.Quote(pwdFile) : " --no-password --insecure");
                var r = await RunStep(sb.ToString());
                if (r != null && r.Success)
                {
                    _intCa.Text = crt;
                    _intCaKey.Text = key;
                    if (_verifyRoot.Text.Trim().Length == 0) _verifyRoot.Text = crt;
                    await RefreshTreeAsync();
                    Info("根证书生成成功:\r\n" + crt + "\r\n" + key);
                }
            }
            finally
            {
                StepCli.DeletePasswordFile(pwdFile);
            }
        }

        private async void BtnIntCreate_Click(object sender, EventArgs e)
        {
            string ca, caKey;
            if (!CaOk(_intCa, "根CA证书", out ca)) return;
            if (!CaOk(_intCaKey, "根CA私钥", out caKey)) return;
            string cn = _intCn.Text.Trim();
            if (cn.Length == 0) { Warn("请填写 Common Name"); return; }
            string prefix = SafeName(_intPrefix.Text, "intermediate_ca");
            string crt = P(prefix + ".crt");
            string key = P(prefix + ".key");
            if (!ConfirmOverwrite(new[] { crt, key })) return;
            DeleteFiles(new[] { crt, key });
            string caPwdFile = (_intCaPwd.Text ?? "").Length > 0 ? StepCli.WritePasswordFile(_intCaPwd.Text) : null;
            string keyPwdFile = (_intPwd.Text ?? "").Length > 0 ? StepCli.WritePasswordFile(_intPwd.Text) : null;
            try
            {
                var sb = new StringBuilder("certificate create ");
                sb.Append(StepCli.Quote(cn)).Append(' ').Append(StepCli.Quote(crt)).Append(' ').Append(StepCli.Quote(key));
                sb.Append(" --profile intermediate-ca");
                sb.Append(" --ca ").Append(StepCli.Quote(ca));
                sb.Append(" --ca-key ").Append(StepCli.Quote(caKey));
                if (caPwdFile != null) sb.Append(" --ca-password-file ").Append(StepCli.Quote(caPwdFile));
                sb.Append(" --not-after ").Append(StepCli.Quote(Dur(_intDur)));
                sb.Append(' ').Append(KeyArgs(_intKty));
                sb.Append(keyPwdFile != null ? " --password-file " + StepCli.Quote(keyPwdFile) : " --no-password --insecure");
                var r = await RunStep(sb.ToString());
                if (r != null && r.Success)
                {
                    _leafCa.Text = crt;
                    _leafCaKey.Text = key;
                    if (_verifyInt.Text.Trim().Length == 0) _verifyInt.Text = crt;
                    if (_p12Ca.Text.Trim().Length == 0) _p12Ca.Text = crt;
                    await RefreshTreeAsync();
                    Info("中间证书生成成功:\r\n" + crt + "\r\n" + key);
                }
            }
            finally
            {
                StepCli.DeletePasswordFile(caPwdFile);
                StepCli.DeletePasswordFile(keyPwdFile);
            }
        }

        private async void BtnLeafCreate_Click(object sender, EventArgs e)
        {
            string ca, caKey;
            if (!CaOk(_leafCa, "签发CA证书", out ca)) return;
            if (!CaOk(_leafCaKey, "签发CA私钥", out caKey)) return;
            string cn = _leafCn.Text.Trim();
            if (cn.Length == 0) { Warn("请填写主题 CN / 域名"); return; }
            string prefix = SafeName(_leafPrefix.Text, "server");
            string crt = P(prefix + ".crt");
            string key = P(prefix + ".key");
            string p12 = P(prefix + ".p12");
            var targets = new List<string> { crt, key };
            if (_leafP12.Checked) targets.Add(p12);
            if (!ConfirmOverwrite(targets)) return;
            DeleteFiles(targets);
            var sans = _leafSans.Lines.Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToList();
            string caPwdFile = (_leafCaPwd.Text ?? "").Length > 0 ? StepCli.WritePasswordFile(_leafCaPwd.Text) : null;
            string keyPwdFile = (_leafPwd.Text ?? "").Length > 0 ? StepCli.WritePasswordFile(_leafPwd.Text) : null;
            try
            {
                var sb = new StringBuilder("certificate create ");
                sb.Append(StepCli.Quote(cn)).Append(' ').Append(StepCli.Quote(crt)).Append(' ').Append(StepCli.Quote(key));
                sb.Append(" --profile leaf");
                sb.Append(" --ca ").Append(StepCli.Quote(ca));
                sb.Append(" --ca-key ").Append(StepCli.Quote(caKey));
                if (caPwdFile != null) sb.Append(" --ca-password-file ").Append(StepCli.Quote(caPwdFile));
                sb.Append(" --not-after ").Append(StepCli.Quote(Dur(_leafDur)));
                sb.Append(' ').Append(KeyArgs(_leafKty));
                foreach (string san in sans) sb.Append(" --san ").Append(StepCli.Quote(san));
                if (_leafBundle.Checked) sb.Append(" --bundle");
                sb.Append(keyPwdFile != null ? " --password-file " + StepCli.Quote(keyPwdFile) : " --no-password --insecure");
                var r = await RunStep(sb.ToString());
                if (r != null && r.Success)
                {
                    Log("网站证书生成成功: " + crt);
                    string msg = "网站证书生成成功:\r\n" + crt + "\r\n" + key;
                    if (_leafP12.Checked)
                    {
                        var r2 = await ExportP12(p12, crt, key, ca, _leafP12Pwd.Text, _leafP12Legacy.Checked);
                        if (r2 != null && r2.Success) msg += "\r\n" + p12;
                        else msg += "\r\n(PFX 导出失败，详见日志)";
                    }
                    await RefreshTreeAsync();
                    Info(msg);
                }
            }
            finally
            {
                StepCli.DeletePasswordFile(caPwdFile);
                StepCli.DeletePasswordFile(keyPwdFile);
            }
        }

        private async void BtnCodeCreate_Click(object sender, EventArgs e)
        {
            string ca, caKey;
            if (!CaOk(_codeCa, "签发CA证书", out ca)) return;
            if (!CaOk(_codeCaKey, "签发CA私钥", out caKey)) return;
            string cn = _codeCn.Text.Trim();
            if (cn.Length == 0) { Warn("请填写 Common Name"); return; }
            string prefix = SafeName(_codePrefix.Text, "codesign");
            string crt = P(prefix + ".crt");
            string key = P(prefix + ".key");
            if (!ConfirmOverwrite(new[] { crt, key })) return;
            DeleteFiles(new[] { crt, key });
            string tpl = Path.Combine(Path.GetTempPath(), "stepgui_tpl_" + Guid.NewGuid().ToString("N") + ".json");
            string caPwdFile = (_codeCaPwd.Text ?? "").Length > 0 ? StepCli.WritePasswordFile(_codeCaPwd.Text) : null;
            string keyPwdFile = (_codePwd.Text ?? "").Length > 0 ? StepCli.WritePasswordFile(_codePwd.Text) : null;
            try
            {
                File.WriteAllText(tpl,
                    "{\r\n  \"subject\": {{ toJson .Subject }},\r\n  \"keyUsage\": [\"digitalSignature\"],\r\n  \"extKeyUsage\": [\"codeSigning\"]\r\n}\r\n",
                    new UTF8Encoding(false));
                var sb = new StringBuilder("certificate create ");
                sb.Append(StepCli.Quote(cn)).Append(' ').Append(StepCli.Quote(crt)).Append(' ').Append(StepCli.Quote(key));
                sb.Append(" --template ").Append(StepCli.Quote(tpl));
                sb.Append(" --ca ").Append(StepCli.Quote(ca));
                sb.Append(" --ca-key ").Append(StepCli.Quote(caKey));
                if (caPwdFile != null) sb.Append(" --ca-password-file ").Append(StepCli.Quote(caPwdFile));
                sb.Append(" --not-after ").Append(StepCli.Quote(Dur(_codeDur)));
                sb.Append(' ').Append(KeyArgs(_codeKty));
                sb.Append(keyPwdFile != null ? " --password-file " + StepCli.Quote(keyPwdFile) : " --no-password --insecure");
                var r = await RunStep(sb.ToString());
                if (r != null && r.Success)
                {
                    await RefreshTreeAsync();
                    Info("代码签名证书生成成功:\r\n" + crt + "\r\n" + key);
                }
            }
            finally
            {
                try { File.Delete(tpl); } catch { }
                StepCli.DeletePasswordFile(caPwdFile);
                StepCli.DeletePasswordFile(keyPwdFile);
            }
        }

        private async Task<StepResult> ExportP12(string p12Path, string crt, string key, string ca, string pwd, bool legacy)
        {
            string pwdFile = (pwd ?? "").Length > 0 ? StepCli.WritePasswordFile(pwd) : null;
            try
            {
                var sb = new StringBuilder("certificate p12 ");
                sb.Append(StepCli.Quote(p12Path)).Append(' ').Append(StepCli.Quote(crt)).Append(' ').Append(StepCli.Quote(key));
                if (!string.IsNullOrEmpty(ca)) sb.Append(" --ca ").Append(StepCli.Quote(ca));
                sb.Append(pwdFile != null ? " --password-file " + StepCli.Quote(pwdFile) : " --no-password --insecure");
                if (legacy) sb.Append(" --legacy");
                sb.Append(" --force");
                return await RunStep(sb.ToString());
            }
            finally
            {
                StepCli.DeletePasswordFile(pwdFile);
            }
        }

        #region 证书树

        private sealed class CertMeta
        {
            public string Kind;
            public string Cn;
            public string SubjectDn;
            public string IssuerDn;
            public DateTime? NotAfter;
            public long StampUtc;
            public long Length;
        }

        private async Task RefreshTreeAsync()
        {
            if (_refreshing || _tree == null) return;
            _refreshing = true;
            try
            {
                if (!Directory.Exists(AppConfig.StorePath)) AppConfig.EnsureStore();
                var files = Directory.GetFiles(AppConfig.StorePath)
                    .Where(f => KnownExts.Contains(extOf(f)))
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                foreach (string f in files)
                {
                    if (NeedsClassify(f)) await ClassifyFileAsync(f);
                }
                BuildTree(files);
                _statusLabel.Text = "证书库: " + AppConfig.StorePath + "    文件数: " + files.Count;
            }
            catch (Exception ex)
            {
                Log("刷新证书树失败: " + ex.Message);
            }
            finally
            {
                _refreshing = false;
            }
        }

        private bool NeedsClassify(string file)
        {
            CertMeta m;
            if (!_metaCache.TryGetValue(Path.GetFileName(file), out m)) return true;
            var fi = new FileInfo(file);
            return m.StampUtc != fi.LastWriteTimeUtc.Ticks || m.Length != fi.Length;
        }

        private async Task ClassifyFileAsync(string file)
        {
            string name = Path.GetFileName(file);
            var meta = new CertMeta();
            string ext = extOf(file);
            try
            {
                var fi = new FileInfo(file);
                meta.StampUtc = fi.LastWriteTimeUtc.Ticks;
                meta.Length = fi.Length;
                if (ext == ".key") meta.Kind = "私钥";
                else if (ext == ".p12" || ext == ".pfx") meta.Kind = "PFX";
                else if (ext == ".csr" || SniffPemHeader(file, "CERTIFICATE REQUEST")) meta.Kind = "CSR";
                else
                {
                    string target = file;
                    string temp = null;
                    if (ext == ".der")
                    {
                        temp = Path.Combine(Path.GetTempPath(), "stepgui_" + Guid.NewGuid().ToString("N") + ".pem");
                        var rf = await StepCli.RunAsync(AppConfig.StepPath,
                            "certificate format " + StepCli.Quote(file) + " --out " + StepCli.Quote(temp) + " -f",
                            AppConfig.StorePath, null);
                        if (rf != null && rf.Success) target = temp;
                    }
                    try
                    {
                        var r = await StepCli.RunAsync(AppConfig.StepPath,
                            "certificate inspect " + StepCli.Quote(target) + " --format json",
                            AppConfig.StorePath, null);
                        ParseCertMeta(r, meta);
                        if (meta.Kind == "其他" && r != null)
                            Log("警告: 无法识别 " + name + " (退出码 " + r.ExitCode + ", 输出 " + (r.Output ?? "").Length + " 字符): " +
                                ((r.Output ?? "").Length > 0 ? r.Output.Substring(0, Math.Min(150, r.Output.Length)).Replace("\r", " ").Replace("\n", " ") : "(空)"));
                    }
                    finally
                    {
                        if (temp != null) { try { File.Delete(temp); } catch { } }
                    }
                }
                if (meta.Kind == null && SniffPemHeader(file, "PRIVATE KEY")) meta.Kind = "私钥";
                if (meta.Kind == null) meta.Kind = "其他";
            }
            catch (Exception ex)
            {
                Log("分类异常 " + name + ": " + ex.GetType().Name + " " + ex.Message);
                meta.Kind = "其他";
            }
            Log("分类: " + name + " ext=" + ext + " → " + meta.Kind + (meta.Cn != null ? " CN=" + meta.Cn : ""));
            _metaCache[name] = meta;
        }

        private void ParseCertMeta(StepResult r, CertMeta meta)
        {
            if (r == null || !r.Success || string.IsNullOrEmpty(r.Output)) { meta.Kind = "其他"; return; }
            IDictionary<string, object> obj;
            try { obj = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(r.Output.Trim()); }
            catch { meta.Kind = "其他"; return; }
            if (obj == null) { meta.Kind = "其他"; return; }
            meta.SubjectDn = AsString(Get(obj, "subject_dn"));
            meta.IssuerDn = AsString(Get(obj, "issuer_dn"));
            meta.Kind = "其他";
            if (meta.SubjectDn == null || meta.IssuerDn == null) return;
            meta.Cn = ExtractCn(obj);
            var exts = AsDict(Get(obj, "extensions"));
            bool isCa = false;
            if (exts != null)
            {
                var bc = AsDict(Get(exts, "basic_constraints"));
                if (bc != null) isCa = AsBool(Get(bc, "is_ca"));
            }
            var sig = AsDict(Get(obj, "signature"));
            bool self = sig != null && AsBool(Get(sig, "self_signed"));
            var val = AsDict(Get(obj, "validity"));
            if (val != null)
            {
                DateTime d;
                if (DateTime.TryParse(AsString(Get(val, "end")), null, System.Globalization.DateTimeStyles.RoundtripKind, out d))
                    meta.NotAfter = d.ToLocalTime();
            }
            if (isCa) meta.Kind = self ? "根证书" : "中间证书";
            else meta.Kind = "网站证书";
        }

        private static object Get(IDictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v : null;
        }

        private static string AsString(object o) { return o as string; }

        private static bool AsBool(object o) { return o is bool && (bool)o; }

        private static IDictionary<string, object> AsDict(object o) { return o as IDictionary<string, object>; }

        private static string ExtractCn(IDictionary<string, object> obj)
        {
            var subject = AsDict(Get(obj, "subject"));
            if (subject == null) return null;
            object raw = Get(subject, "common_name");
            if (raw == null) return null;
            string single = raw as string;
            if (single != null) return single;
            var list = raw as System.Collections.IEnumerable;
            if (list == null) return null;
            foreach (object s in list) return AsString(s);
            return null;
        }

        private void BuildTree(List<string> files)
        {
            var expanded = new HashSet<string>();
            CollectExpanded(_tree.Nodes, expanded);

            _tree.BeginUpdate();
            _tree.Nodes.Clear();

            var metas = files.Select(f => new { File = f, Meta = _metaCache[Path.GetFileName(f)] }).ToList();
            var roots = metas.Where(x => x.Meta.Kind == "根证书").ToList();
            var ints = metas.Where(x => x.Meta.Kind == "中间证书").ToList();
            var leaves = metas.Where(x => x.Meta.Kind == "网站证书").ToList();
            var usedInts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var usedLeaves = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var catRoot = new TreeNode("根证书 / 证书链 (" + roots.Count + ")") { Name = "cat:root", NodeFont = CatFont() };
            foreach (var rt in roots)
            {
                var n = FileNode(rt.File, rt.Meta);
                catRoot.Nodes.Add(n);
                foreach (var it in ints.Where(x => DnEq(x.Meta.IssuerDn, rt.Meta.SubjectDn)))
                {
                    var ni = FileNode(it.File, it.Meta);
                    n.Nodes.Add(ni);
                    usedInts.Add(it.File);
                    foreach (var lf in leaves.Where(x => DnEq(x.Meta.IssuerDn, it.Meta.SubjectDn)))
                    {
                        ni.Nodes.Add(FileNode(lf.File, lf.Meta));
                        usedLeaves.Add(lf.File);
                    }
                }
                foreach (var lf in leaves.Where(x => DnEq(x.Meta.IssuerDn, rt.Meta.SubjectDn) && !usedLeaves.Contains(x.File)))
                {
                    n.Nodes.Add(FileNode(lf.File, lf.Meta));
                    usedLeaves.Add(lf.File);
                }
            }
            _tree.Nodes.Add(catRoot);

            AddCat("中间证书 (未挂靠)", "cat:int", ints.Where(x => !usedInts.Contains(x.File)));
            AddCat("网站证书 (未挂靠)", "cat:leaf", leaves.Where(x => !usedLeaves.Contains(x.File)));
            AddCat("CSR", "cat:csr", metas.Where(x => x.Meta.Kind == "CSR"));
            AddCat("私钥", "cat:key", metas.Where(x => x.Meta.Kind == "私钥"));
            AddCat("PFX", "cat:pfx", metas.Where(x => x.Meta.Kind == "PFX"));
            AddCat("其他", "cat:other", metas.Where(x => x.Meta.Kind == "其他"));

            _tree.EndUpdate();
            ReapplyExpanded(_tree.Nodes, expanded);
            if (_currentFile != null)
            {
                var found = FindFileNode(_tree.Nodes, Path.GetFileName(_currentFile));
                if (found != null) { _tree.SelectedNode = found; found.EnsureVisible(); }
            }
        }

        private void AddCat(string title, string key, IEnumerable<dynamic> items)
        {
            var list = items.Cast<dynamic>().ToList();
            var cat = new TreeNode(title + " (" + list.Count + ")") { Name = key, NodeFont = CatFont() };
            foreach (var it in list) cat.Nodes.Add(FileNode(it.File, (CertMeta)it.Meta));
            if (cat.Nodes.Count > 0) cat.Expand();
            _tree.Nodes.Add(cat);
        }

        private static Font CatFont()
        {
            return new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        }

        private TreeNode FileNode(string file, CertMeta m)
        {
            string name = Path.GetFileName(file);
            string title = string.IsNullOrEmpty(m.Cn) ? name : m.Cn + "  [" + name + "]";
            if (m.NotAfter.HasValue)
            {
                title += "  · " + m.NotAfter.Value.ToString("yyyy-MM-dd");
                if (m.NotAfter.Value < DateTime.Now) title += " 【已过期】";
                else if ((m.NotAfter.Value - DateTime.Now).TotalDays < 30) title += " 【即将过期】";
            }
            else if (m.Kind == "其他")
            {
                title = name;
            }
            return new TreeNode(title) { Name = "file:" + name, Tag = file };
        }

        private static bool DnEq(string a, string b)
        {
            return !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static void CollectExpanded(TreeNodeCollection nodes, HashSet<string> set)
        {
            foreach (TreeNode n in nodes)
            {
                if (n.IsExpanded) set.Add(n.Name);
                CollectExpanded(n.Nodes, set);
            }
        }

        private static void ReapplyExpanded(TreeNodeCollection nodes, HashSet<string> set)
        {
            foreach (TreeNode n in nodes)
            {
                if (set.Contains(n.Name)) n.Expand();
                ReapplyExpanded(n.Nodes, set);
            }
        }

        private static TreeNode FindFileNode(TreeNodeCollection nodes, string fileName)
        {
            foreach (TreeNode n in nodes)
            {
                if (n.Tag is string && string.Equals(Path.GetFileName((string)n.Tag), fileName, StringComparison.OrdinalIgnoreCase))
                    return n;
                var r = FindFileNode(n.Nodes, fileName);
                if (r != null) return r;
            }
            return null;
        }

        private void Tree_AfterSelect(object sender, TreeViewEventArgs e)
        {
            string f = e.Node == null ? null : e.Node.Tag as string;
            if (f == null) return;
            _currentFile = f;
            _curFileTxt.Text = f;
            string key = P(Path.GetFileNameWithoutExtension(f) + ".key");
            if (File.Exists(key)) _p12Key.Text = key;
            CertMeta m;
            if (_metaCache.TryGetValue(Path.GetFileName(f), out m) && m.Kind == "根证书")
            {
                if (_verifyRoot.Text.Trim().Length == 0) _verifyRoot.Text = f;
            }
        }

        private void Tree_DoubleClick(object sender, EventArgs e)
        {
            if (_tree.SelectedNode != null && _tree.SelectedNode.Tag is string) ViewCurrent(false);
        }

        #endregion

        private string CurrentFile()
        {
            if (_currentFile != null && File.Exists(_currentFile)) return _currentFile;
            Warn("请先在左侧证书树中选择一个文件。");
            return null;
        }

        private static string UnsupportedReason(string file)
        {
            string ext = extOf(file);
            if (ext == ".p12" || ext == ".pfx")
                return "step.exe 无法直接读取 PFX/P12 的内容。\r\n本工具的“导出 PFX”支持从 PEM 证书 + 私钥打包 PFX。";
            if (ext == ".key")
                return "这是私钥文件，不能作为证书查看。请选择 .crt / .pem / .der / .csr 文件。";
            return null;
        }

        private async void ViewCurrent(bool json)
        {
            string f = CurrentFile();
            if (f == null) return;
            _tabs.SelectedTab = _detailsPage;
            string reason = UnsupportedReason(f);
            if (reason != null) { Warn(reason); return; }
            string temp = null;
            string target = f;
            try
            {
                if (extOf(f) == ".der")
                {
                    temp = Path.Combine(Path.GetTempPath(), "stepgui_" + Guid.NewGuid().ToString("N") + ".pem");
                    var r0 = await RunStep("certificate format " + StepCli.Quote(f) + " --out " + StepCli.Quote(temp) + " -f");
                    if (r0 == null || !r0.Success) return;
                    target = temp;
                }
                string args = "certificate inspect " + StepCli.Quote(target) + (json ? " --format json" : " --format text");
                var r = await RunStep(args);
                if (r != null) _inspect.Text = r.Output.Length > 0 ? r.Output : "(无输出)";
            }
            finally
            {
                if (temp != null) { try { File.Delete(temp); } catch { } }
            }
        }

        private async void BtnFingerprint_Click(object sender, EventArgs e)
        {
            string f = CurrentFile();
            if (f == null) return;
            string reason = UnsupportedReason(f);
            if (reason != null) { Warn(reason); return; }
            var r = await RunStep("certificate fingerprint " + StepCli.Quote(f));
            if (r != null) _inspect.Text = (r.Output.Length > 0 ? r.Output : "(无输出)");
        }

        private async void BtnToPem_Click(object sender, EventArgs e)
        {
            string f = CurrentFile();
            if (f == null) return;
            string ext = extOf(f);
            if (ext == ".p12" || ext == ".pfx" || ext == ".key")
            {
                Warn("仅支持证书/CSR 文件 (PEM 或 DER) 转换为 PEM。");
                return;
            }
            string name = Path.GetFileNameWithoutExtension(f);
            string target = P(name + ".pem");
            if (string.Equals(f, target, StringComparison.OrdinalIgnoreCase))
            {
                Warn("该文件已经是 PEM。");
                return;
            }
            if (!ConfirmOverwrite(new[] { target })) return;
            var r = await RunStep("certificate format " + StepCli.Quote(f) + " --out " + StepCli.Quote(target) + " -f");
            if (r != null && r.Success)
            {
                Info("已转换为 PEM:\r\n" + target);
                await RefreshTreeAsync();
            }
        }

        private async void BtnImport_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "选择要导入的证书文件",
                Filter = "证书/密钥文件 (*.pem;*.crt;*.cer;*.der;*.csr;*.key;*.p12;*.pfx)|*.pem;*.crt;*.cer;*.der;*.csr;*.key;*.p12;*.pfx|所有文件 (*.*)|*.*"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string src = dlg.FileName;
                string name = Path.GetFileName(src);
                string dst = P(name);
                try
                {
                    if (File.Exists(dst) &&
                        MessageBox.Show(this, "文件已存在，覆盖吗？\r\n" + dst, "覆盖确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                        return;
                    File.Copy(src, dst, true);
                    Log("已导入: " + dst);
                }
                catch (Exception ex)
                {
                    Warn("导入失败: " + ex.Message);
                    return;
                }
                string pem = null;
                if (!LooksPem(dst) && extOf(dst) != ".csr" && extOf(dst) != ".key" && extOf(dst) != ".p12" && extOf(dst) != ".pfx")
                {
                    pem = P(Path.GetFileNameWithoutExtension(dst) + ".pem");
                    if (!ConfirmOverwrite(new[] { pem })) pem = null;
                }
                await RefreshTreeAsync();
                if (pem != null)
                {
                    var r = await RunStep("certificate format " + StepCli.Quote(dst) + " --out " + StepCli.Quote(pem) + " -f");
                    if (r != null && r.Success)
                    {
                        Log("已转换为 PEM: " + pem);
                        await RefreshTreeAsync();
                    }
                }
                var node = FindFileNode(_tree.Nodes, Path.GetFileName(pem ?? dst));
                if (node != null) _tree.SelectedNode = node;
            }
        }

        private static string extOf(string f)
        {
            return (Path.GetExtension(f) ?? "").ToLowerInvariant();
        }

        private static bool LooksPem(string f)
        {
            try
            {
                using (var sr = new StreamReader(f))
                {
                    char[] buf = new char[64];
                    int n = sr.Read(buf, 0, buf.Length);
                    return new string(buf, 0, n).Contains("-----BEGIN");
                }
            }
            catch { return false; }
        }

        private static bool SniffPemHeader(string f, string marker)
        {
            try
            {
                using (var sr = new StreamReader(f))
                {
                    char[] buf = new char[512];
                    int n = sr.Read(buf, 0, buf.Length);
                    return new string(buf, 0, n).Contains(marker);
                }
            }
            catch { return false; }
        }

        private async void BtnVerify_Click(object sender, EventArgs e)
        {
            string f = CurrentFile();
            if (f == null) return;
            string reason = UnsupportedReason(f);
            if (reason != null) { Warn(reason); return; }
            string root = _verifyRoot.Text.Trim();
            if (root.Length == 0 || !File.Exists(root)) { Warn("请选择用于验证的 Root 证书。"); return; }
            string inter = _verifyInt.Text.Trim();
            string chain = null;
            try
            {
                StepResult r;
                if (inter.Length > 0 && File.Exists(inter))
                {
                    chain = Path.Combine(Path.GetTempPath(), "stepgui_chain_" + Guid.NewGuid().ToString("N") + ".pem");
                    var rb = await RunStep("certificate bundle " + StepCli.Quote(f) + " " + StepCli.Quote(inter) + " " + StepCli.Quote(chain));
                    if (rb == null || !rb.Success)
                    {
                        Warn("生成捆绑文件失败，详见日志。");
                        return;
                    }
                    r = await RunStep("certificate verify " + StepCli.Quote(chain) + " --roots " + StepCli.Quote(root) + " --verbose");
                }
                else
                {
                    r = await RunStep("certificate verify " + StepCli.Quote(f) + " --roots " + StepCli.Quote(root) + " --verbose");
                }
                if (r != null && r.Success)
                    Info("验证成功: 该证书链可信任至所选 Root。");
                else
                    Warn("验证失败，详见日志输出。");
            }
            finally
            {
                if (chain != null) { try { File.Delete(chain); } catch { } }
            }
        }

        private async void BtnExportP12_Click(object sender, EventArgs e)
        {
            string crt = CurrentFile();
            if (crt == null) return;
            string reason = UnsupportedReason(crt);
            if (reason != null) { Warn(reason); return; }
            if (extOf(crt) == ".csr")
            {
                Warn("CSR 无法打包为 PFX，请选择证书文件。");
                return;
            }
            string key = _p12Key.Text.Trim();
            if (key.Length == 0) key = P(Path.GetFileNameWithoutExtension(crt) + ".key");
            if (!File.Exists(key)) { Warn("未找到私钥文件:\r\n" + key); return; }
            string ca = _p12Ca.Text.Trim();
            if (ca.Length > 0 && !File.Exists(ca)) { Warn("CA 证书文件不存在:\r\n" + ca); return; }
            string target = P(Path.GetFileNameWithoutExtension(crt) + ".p12");
            if (!ConfirmOverwrite(new[] { target })) return;
            var r = await ExportP12(target, crt, key, ca, _p12Pwd.Text, _p12Legacy.Checked);
            if (r != null && r.Success)
            {
                Info("PFX 导出成功:\r\n" + target);
                await RefreshTreeAsync();
            }
        }

        private void BtnStoreChange_Click(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog { Description = "选择证书库目录" })
            {
                dlg.SelectedPath = Directory.Exists(AppConfig.StorePath) ? AppConfig.StorePath : null;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    AppConfig.SetPaths(null, dlg.SelectedPath);
                    _metaCache.Clear();
                    _currentFile = null;
                    var _ = RefreshTreeAsync();
                }
            }
        }

        private void BtnStoreOpen_Click(object sender, EventArgs e)
        {
            try { Process.Start("explorer.exe", AppConfig.StorePath); } catch { }
        }

        private void MenuVerify_Click(object sender, EventArgs e)
        {
            _tabs.SelectedTab = _detailsPage;
            BtnVerify_Click(sender, e);
        }

        private void MenuExportP12_Click(object sender, EventArgs e)
        {
            _tabs.SelectedTab = _detailsPage;
            BtnExportP12_Click(sender, e);
        }

        private void MenuOpenFolder_Click(object sender, EventArgs e)
        {
            string f = _currentFile;
            if (f != null && File.Exists(f)) Process.Start("explorer.exe", "/select,\"" + f + "\"");
            else BtnStoreOpen_Click(sender, e);
        }

        private async void MenuDelete_Click(object sender, EventArgs e)
        {
            string f = _currentFile;
            if (f == null || !File.Exists(f)) { Warn("请先选择文件。"); return; }
            if (MessageBox.Show(this, "确定删除？\r\n" + f, "删除确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { File.Delete(f); Log("已删除: " + f); } catch (Exception ex) { Warn("删除失败: " + ex.Message); }
            if (_currentFile == f) _currentFile = null;
            await RefreshTreeAsync();
        }
    }
}
