using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Sohvaperuna.TorrentEngine;

namespace Sohvaperuna.MyTorrents
{
	public class SettingsForm : Form
	{
		private Configuration _configuration;

		private IContainer components = null;

		private Button button_Ok;

		private Button button_Close;

		private TabPage tabPage1;

		private TextBox textBox_Password;

		private TextBox textBox_Username;

		private TextBox textBox_IPAddress;

		private Label label4;

		private Label label3;

		private NumericUpDown numeric_Port;

		private Label label2;

		private Label label1;

		private TabPage tabPage2;

		private GroupBox groupBox1;

		private ComboBox combo_Backend;

		private Label label_BackendHint;

		private CheckBox checkBoxNotifications;

		private TabControl tabControl1;

		private GroupBox groupBox2;

		private GroupBox groupBox3;

		private NumericUpDown numeric_OutsideTimer;

		private Label label6;

		private NumericUpDown numeric_InsideTimer;

		private Label label5;

		private Label label8;

		private Label label7;

		private SearchSitesPage _sitesPage;

		private FinishedPage _finishedPage;

		private AutoGrabberPage _grabberPage;
		private MoviesPage _moviesPage; // 2026 (1.3.43)
		private BackgroundPage _backgroundPage; // 2026 (1.3.45)

		public SettingsForm(ref Configuration configuration)
		{
			_configuration = configuration;
			InitializeComponent();
			// 2026: "Search sites" tab (Jackett + your own sites). Widen the window to fit it.
			int extra = 350; // 1.3.42: room for the three buttons bottom left
			base.ClientSize = new System.Drawing.Size(base.ClientSize.Width + extra, base.ClientSize.Height);
			tabControl1.Width += extra;
			button_Ok.Left += extra;
			button_Close.Left += extra;
			// 2026: taller too - room for the favourites on the Auto grabber tab and for the Downloads list
			int extraHeight = 158;
			base.ClientSize = new System.Drawing.Size(base.ClientSize.Width, base.ClientSize.Height + extraHeight);
			tabControl1.Height += extraHeight;
			button_Ok.Top += extraHeight;
			button_Close.Top += extraHeight;
			foreach (TabPage page in new[] { tabPage1, tabPage2 })
			{
				foreach (Control c in page.Controls)
				{
					if (c is GroupBox || (c is TextBox && c != textBox_IPAddress && c != textBox_Username)) c.Width += extra; // 2026: the address and username boxes sit next to Port / Password - widening them covered those
				}
			}
			combo_Backend.Width += extra;
			label_BackendHint.Width += extra;
			_sitesPage = new SearchSitesPage();
			tabControl1.TabPages.Add(_sitesPage);
			_finishedPage = new FinishedPage();
			tabControl1.TabPages.Add(_finishedPage);
			_grabberPage = new AutoGrabberPage();
			tabControl1.TabPages.Add(_grabberPage);
			_moviesPage = new MoviesPage();
			tabControl1.TabPages.Add(_moviesPage);
			ShowsPage shows = new ShowsPage(); // 2026: Auto grabber shows, get one episode now, size report
			DownloadsPage downloads = new DownloadsPage(); // 2026: the torrent program's list
			AboutPage about = new AboutPage(); // 2026: version, credits and links
			_backgroundPage = new BackgroundPage();
			CheckUpPage checkUp = new CheckUpPage(); // 2026 (1.3.49)
			// 2026 (1.3.45): everyday tabs first, the set-up-once ones last
			tabControl1.TabPages.Clear();
			tabControl1.TabPages.AddRange(new TabPage[] { _moviesPage, _grabberPage, shows, downloads, _finishedPage, checkUp, tabPage2, tabPage1, _sitesPage, _backgroundPage, about }); // 1.3.52: Search sites after the torrent program
			tabControl1.SelectedTab = FirstStep();
			// 2026 (1.3.66): remember the last tab - it opens there next time (unless a setup step still needs doing)
			tabControl1.SelectedIndexChanged += (s, e) =>
			{
				if (tabControl1.SelectedTab != null) Configuration.SetExtra("Config.LastTab", tabControl1.SelectedTab.Text);
			};
			// 2026 (1.3.45): the boxes on these tabs grow with the window (and the long text / folder boxes in them)
			foreach (TabPage page in new TabPage[] { _grabberPage, _moviesPage, _finishedPage, _backgroundPage })
			{
				TabPage pg = page;
				pg.Resize += (s, e) => Stretch(pg);
				pg.HandleCreated += (s, e) => Stretch(pg);
			}
			AddSaveFolderControls();
			// 2026: the setup assistant lives in "MyTorrents Configuration.exe" in the MediaPortal folder
			string setupExe = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MyTorrents Configuration.exe");
			if (System.IO.File.Exists(setupExe))
			{
				Button setup = new Button { Text = "Setup assistant...", Size = new System.Drawing.Size(130, 23), Location = new System.Drawing.Point(tabControl1.Left, button_Ok.Top) };
				setup.Click += (s, e) =>
				{
					try
					{
						System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(setupExe, "/setup full") { UseShellExecute = true });
						DialogResult = DialogResult.Cancel;
						Close(); // the assistant writes the settings itself
					}
					catch (Exception ex)
					{
						MessageBox.Show(this, "Could not start the setup assistant: " + ex.Message, "MyTorrents");
					}
				};
				Controls.Add(setup);
				Button updates = new Button { Text = "Check for updates", Size = new System.Drawing.Size(130, 23), Location = new System.Drawing.Point(setup.Right + 6, button_Ok.Top) };
				updates.Click += (s, e) =>
				{
					try
					{
						System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(setupExe, "/setup") { UseShellExecute = true });
						DialogResult = DialogResult.Cancel;
						Close();
					}
					catch (Exception ex)
					{
						MessageBox.Show(this, "Could not start the update check: " + ex.Message, "MyTorrents");
					}
				};
				Controls.Add(updates);
				// 2026 (1.3.42): remove MyTorrents and, if you like, the programs its setup installed
				Button remove = new Button { Text = "Uninstall MyTorrents...", Size = new System.Drawing.Size(130, 23), Location = new System.Drawing.Point(updates.Right + 6, button_Ok.Top) };
				remove.Click += (s, e) =>
				{
					try
					{
						System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(setupExe, "/remove") { UseShellExecute = true });
						DialogResult = DialogResult.Cancel;
						Close();
					}
					catch (Exception ex)
					{
						MessageBox.Show(this, "Could not start Uninstall MyTorrents: " + ex.Message, "MyTorrents");
					}
				};
				Controls.Add(remove);
			}
			// 2026 (1.3.42): make the window bigger and everything grows with it - the tabs fill the space, the buttons
			// stay along the bottom (so the Auto grabber tab needs less scrolling)
			tabControl1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
			button_Ok.Anchor = button_Close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
			foreach (Control c in Controls)
			{
				if (c is Button && c != button_Ok && c != button_Close) c.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
			}
			FormBorderStyle = FormBorderStyle.Sizable;
			MaximizeBox = true;
			MinimumSize = Size;
			// open taller when the screen has room (up to 950 high)
			try
			{
				int room = Screen.FromControl(this).WorkingArea.Height - 40;
				int want = Math.Min(950, room);
				if (want > Height) Height = want;
			}
			catch
			{
			}
		}

		/// 2026 (1.3.45): widens the boxes on a tab to the window's width; inside them, the boxes that reached the right
		/// edge (folders, favourites...) widen too. Each control's first width is remembered so it can shrink back.
		private static void Stretch(TabPage page)
		{
			int room = page.ClientSize.Width - 16 - (page.VerticalScroll.Visible ? 0 : 0);
			foreach (GroupBox g in page.Controls.OfType<GroupBox>().Where(x => (x.Anchor & AnchorStyles.Right) == 0))
			{
				if (!(g.Tag is int)) g.Tag = g.Width;
				int baseWidth = (int)g.Tag;
				int width = Math.Max(baseWidth, room - g.Left + 8);
				int delta = width - g.Width;
				if (delta == 0) continue;
				g.Width = width;
				foreach (Control c in g.Controls)
				{
					if (!(c is TextBox || c is ComboBox || c is ListView)) continue;
					if (!(c.Tag is int)) c.Tag = c.Width;
					if ((int)c.Tag + c.Left < baseWidth - 60) continue; // only the ones that reached the right side
					c.Width = Math.Max((int)c.Tag, c.Width + delta);
				}
			}
		}

		/// 2026 (1.3.45): open on the next setting-up step still to do - the torrent program, then search sites, then
		/// the library folders - and once all is set up, on TV shows.
		private TabPage FirstStep()
		{
			try
			{
				ConfigData c = _configuration.LoadConfiguration();
				int port;
				if (string.IsNullOrWhiteSpace(c.Server) || !int.TryParse(c.Port, out port) || port <= 0 || Configuration.GetExtra("Setup.Done", "") != "true" && !ClientAnswers(c)) return tabPage2;
			}
			catch
			{
				return tabPage2;
			}
			try
			{
				string file = _configuration.Settings["TorrentSearch.Config"] as string;
				string xml = file != null && System.IO.File.Exists(file) ? System.IO.File.ReadAllText(file) : "";
				bool jackett = System.Text.RegularExpressions.Regex.IsMatch(xml, @"<apikey>\s*[^<\s]+\s*</apikey>");
				bool own = xml.IndexOf("<site", StringComparison.OrdinalIgnoreCase) >= 0 || xml.IndexOf("<engine", StringComparison.OrdinalIgnoreCase) >= 0;
				if (!jackett && !own) return _sitesPage;
			}
			catch
			{
			}
			if (string.IsNullOrEmpty(Configuration.GetExtra("Finished.TVFolder", "")) && string.IsNullOrEmpty(LibraryFolders.DefaultTV())) return _finishedPage;
			string last = Configuration.GetExtra("Config.LastTab", ""); // 2026 (1.3.66)
			foreach (TabPage t in tabControl1.TabPages)
				if (t.Text == last) return t;
			return _grabberPage;
		}

		/// Quick check (2 seconds) that the torrent program answers.
		private static bool ClientAnswers(ConfigData c)
		{
			try
			{
				using (System.Net.Sockets.TcpClient t = new System.Net.Sockets.TcpClient())
				{
					IAsyncResult r = t.BeginConnect(c.Server, int.Parse(c.Port), null, null);
					bool ok = r.AsyncWaitHandle.WaitOne(2000) && t.Connected;
					return ok;
				}
			}
			catch
			{
				return false;
			}
		}

		// 2026: show (and change) where the torrent program saves downloads
		private readonly TextBox _saveFolder = new TextBox();

		private string _saveFolderRead = ""; // what the torrent program said, to see whether it was typed over
		private readonly Label _saveFolderNote = new Label { AutoSize = true, ForeColor = System.Drawing.Color.DimGray };

		/// 2026: the download folder - type or paste a folder, or use the buttons (on their own row, so they are
		/// never pushed off the edge on TVs / screens set to a large text size)
		private void AddSaveFolderControls()
		{
			int y = 125;
			tabPage1.Controls.Add(new Label { Text = "Downloads are saved to  (type or paste a folder, or choose one)", Location = new System.Drawing.Point(6, y), AutoSize = true });
			_saveFolder.SetBounds(9, y + 17, tabPage1.Width - 24, 20);
			_saveFolder.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
			tabPage1.Controls.Add(_saveFolder);
			Button change = new Button { Text = "Choose folder...", AutoSize = true, Location = new System.Drawing.Point(9, y + 42) };
			Button open = new Button { Text = "Open in Explorer", AutoSize = true };
			Button check = new Button { Text = "Check", AutoSize = true };
			tabPage1.Controls.Add(change);
			tabPage1.Controls.Add(open);
			tabPage1.Controls.Add(check);
			open.Location = new System.Drawing.Point(change.Right + 6, y + 42);
			check.Location = new System.Drawing.Point(open.Right + 6, y + 42);
			change.SizeChanged += (s2, e2) => { open.Left = change.Right + 6; check.Left = open.Right + 6; };
			open.SizeChanged += (s2, e2) => check.Left = open.Right + 6;
			_saveFolderNote.Location = new System.Drawing.Point(6, y + 74);
			_saveFolderNote.Text = "The torrent program's own default folder. Click Check to read it (the program must be running).";
			tabPage1.Controls.Add(_saveFolderNote);
			check.Click += (s2, e2) => ReadSaveFolder();
			change.Click += (s2, e2) => ChangeSaveFolder(null);
			open.Click += (s2, e2) =>
			{
				string f = _saveFolder.Text.Trim();
				if (!System.IO.Directory.Exists(f))
				{
					MessageBox.Show(this, "That folder does not exist yet:\n" + f, "Download folder");
					return;
				}
				try
				{
					System.Diagnostics.Process.Start("explorer.exe", "\"" + f + "\"");
				}
				catch (Exception ex)
				{
					MessageBox.Show(this, "Could not open the folder: " + ex.Message, "Download folder");
				}
			};
			Shown += (s2, e2) => ReadSaveFolder();
		}

		/// A client for the backend and address currently in this window.
		private ITorrentClient ClientFromForm()
		{
			string key = BackendKeys[Math.Max(0, combo_Backend.SelectedIndex)];
			string host = textBox_IPAddress.Text.Trim();
			int port = (int)numeric_Port.Value;
			if (key == "qbittorrent") return new Sohvaperuna.QBittorrentEngine.QBittorrentClient(host, port, textBox_Username.Text.Trim(), textBox_Password.Text.Trim(), "240");
			if (key == "biglybt") return new Sohvaperuna.TransmissionEngine.TransmissionClient(host, port, textBox_Username.Text.Trim(), textBox_Password.Text.Trim(), "240");
			return null;
		}

		private void ReadSaveFolder()
		{
			ITorrentClient c = ClientFromForm();
			if (c == null)
			{
				_saveFolder.Text = "";
				_saveFolderNote.Text = "Shown for qBittorrent and BiglyBT.";
				return;
			}
			_saveFolderNote.Text = "Asking the torrent program...";
			System.Threading.ThreadPool.QueueUserWorkItem(_ =>
			{
				string f = "", note;
				try
				{
					f = c.Connect() ? SaveFolder.Current(c) : "";
					note = f.Length > 0 ? "New downloads go here. Finished ones are then copied to your library (When finished tab)."
						: "Could not ask the torrent program - is it running, and are the address and login right?";
				}
				catch (Exception ex)
				{
					note = "Could not ask the torrent program: " + ex.Message;
				}
				if (IsDisposed) return;
				BeginInvoke((Action)(() =>
				{
					_saveFolder.Text = f;
					_saveFolderRead = f;
					_saveFolderNote.Text = note;
				}));
			});
		}

		/// Tells the torrent program to save new downloads in "path" (null = choose with the folder window).
		private bool ChangeSaveFolder(string path)
		{
			ITorrentClient c = ClientFromForm();
			if (!(c is ISaveFolder setter))
			{
				MessageBox.Show(this, "The folder can be changed here for qBittorrent and BiglyBT.", "Download folder");
				return false;
			}
			if (path == null)
			{
				using (FolderBrowserDialog d = new FolderBrowserDialog { SelectedPath = _saveFolder.Text, ShowNewFolderButton = true, Description = "Where should new downloads be saved?" })
				{
					if (d.ShowDialog(this) != DialogResult.OK) return false;
					path = d.SelectedPath;
				}
			}
			try
			{
				System.IO.Directory.CreateDirectory(path);
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, "Could not use that folder:\n" + path + "\n" + ex.Message, "Download folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return false;
			}
			Cursor = Cursors.WaitCursor;
			bool ok = c.Connect() && setter.SetSaveFolder(path);
			Cursor = Cursors.Default;
			if (ok)
			{
				ReadSaveFolder();
			}
			else
			{
				MessageBox.Show(this, "The torrent program did not accept the folder. Is it running?", "Download folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
			return ok;
		}

		private void SettingsForm_Load(object sender, EventArgs e)
		{
			LoadSettings();
		}

		private void SettingsForm_FormClosing(object sender, FormClosingEventArgs e)
		{
			if (!e.Cancel)
			{
				SaveSettings();
				if (DialogResult == DialogResult.OK)
				{
					// a folder typed or pasted into the box is passed on to the torrent program
					string typed = _saveFolder.Text.Trim();
					if (typed.Length > 0 && !typed.Equals(_saveFolderRead, StringComparison.OrdinalIgnoreCase))
					{
						ChangeSaveFolder(typed);
					}
					_finishedPage?.Save();
					_grabberPage?.Save();
					_moviesPage?.Save();
					_backgroundPage?.Save();
					_sitesPage?.Save(); // MyTorrents.xml: Jackett + my sites (only on Ok, like the other settings)
				}
			}
		}

		private void LoadSettings()
		{
			textBox_IPAddress.Text = _configuration.Settings["uTorrent.Server.IP"] as string;
			int savedPort;
			int.TryParse(_configuration.Settings["uTorrent.Server.Port"] as string, out savedPort);
			numeric_Port.Value = Math.Max(numeric_Port.Minimum, Math.Min(numeric_Port.Maximum, savedPort > 0 ? savedPort : 8080));
			textBox_Username.Text = (_configuration.Settings["uTorrent.Username"] as string ?? "").Trim();
			textBox_Password.Text = (_configuration.Settings["uTorrent.Password"] as string ?? "").Trim();
			string backend = (_configuration.Settings["uTorrent.Backend"] as string ?? "utorrent").ToLowerInvariant();
			int index = Array.FindIndex(BackendKeys, k => k == backend);
			combo_Backend.SelectedIndex = index < 0 ? 0 : index;
			checkBoxNotifications.Checked = Convert.ToBoolean(_configuration.Settings["Torrent.Notifications"]);
			try
			{
				numeric_InsideTimer.Value = Convert.ToInt32(_configuration.Settings["Torrent.Timer.Inside"] as string);
				numeric_OutsideTimer.Value = Convert.ToInt32(_configuration.Settings["Torrent.Timer.Outside"] as string);
			}
			catch
			{
				numeric_InsideTimer.Value = 5m;
				numeric_OutsideTimer.Value = 240m;
				SaveSettings();
			}
		}

		private void SaveSettings()
		{
			_configuration.Settings["uTorrent.Server.IP"] = textBox_IPAddress.Text.Trim();
			_configuration.Settings["uTorrent.Server.Port"] = numeric_Port.Value.ToString();
			_configuration.Settings["Torrent.Timer.Inside"] = numeric_InsideTimer.Value.ToString();
			_configuration.Settings["Torrent.Timer.Outside"] = numeric_OutsideTimer.Value.ToString();
			_configuration.Settings["uTorrent.Username"] = textBox_Username.Text.Trim();
			_configuration.Settings["uTorrent.Password"] = textBox_Password.Text.Trim();
			_configuration.Settings["uTorrent.Backend"] = BackendKeys[Math.Max(0, combo_Backend.SelectedIndex)];
			_configuration.Settings["Torrent.Notifications"] = checkBoxNotifications.Checked.ToString();
		}

		private void button_Ok_Click(object sender, EventArgs e)
		{
		}

		// Values stored in MediaPortal.xml ("uTorrent.Backend"), in drop-down order
		private static readonly string[] BackendKeys = { "utorrent", "azureus", "deluge", "qbittorrent", "biglybt" };

		private static readonly string[] BackendNames = { "uTorrent", "Azureus / Vuze (XML over HTTP)", "Deluge (Web UI)", "qBittorrent (Web UI)", "BiglyBT (Web Remote) / Transmission" };

		private static readonly int[] DefaultPorts = { 8080, 6884, 8112, 8080, 9091 };

		private static readonly string[] BackendHints =
		{
			"uTorrent: Options > Preferences > Advanced > Web UI.",
			"Needs the old 'XML over HTTP' plugin. For BiglyBT use the BiglyBT option instead.",
			"Deluge: enable the Web UI plugin. Only the password is used.",
			"qBittorrent: Tools > Options > Web UI. Tick 'Bypass authentication for clients on localhost' or enter the Web UI login.",
			"BiglyBT: install the 'Web Remote' plugin (Tools > Plugins). Enter its username/password if you set one."
		};

		private int _lastBackend = -1;

		private void combo_Backend_SelectedIndexChanged(object sender, EventArgs e)
		{
			int i = Math.Max(0, combo_Backend.SelectedIndex);
			bool usesLogin = BackendKeys[i] != "azureus";
			bool usesName = usesLogin && BackendKeys[i] != "deluge";
			textBox_Username.Visible = usesName;
			label3.Visible = usesName; // 2026: reading .Visible back is false until the window shows - the label stayed hidden
			textBox_Password.Visible = usesLogin;
			label4.Visible = usesLogin;
			tabPage1.Text = BackendNames[i].Split(' ')[0];
			label_BackendHint.Text = BackendHints[i];
			// switching backend in the dialog: suggest that client's usual port
			if (_lastBackend >= 0 && _lastBackend != i && numeric_Port.Value == DefaultPorts[_lastBackend])
			{
				numeric_Port.Value = DefaultPorts[i];
			}
			_lastBackend = i;
			tabPage1.Update();
		}

		private void label5_Click(object sender, EventArgs e)
		{
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing && components != null)
			{
				components.Dispose();
			}
			base.Dispose(disposing);
		}

		private void InitializeComponent()
		{
			this.button_Ok = new System.Windows.Forms.Button();
			this.button_Close = new System.Windows.Forms.Button();
			this.tabPage1 = new System.Windows.Forms.TabPage();
			this.textBox_Password = new System.Windows.Forms.TextBox();
			this.textBox_Username = new System.Windows.Forms.TextBox();
			this.textBox_IPAddress = new System.Windows.Forms.TextBox();
			this.label4 = new System.Windows.Forms.Label();
			this.label3 = new System.Windows.Forms.Label();
			this.numeric_Port = new System.Windows.Forms.NumericUpDown();
			this.label2 = new System.Windows.Forms.Label();
			this.label1 = new System.Windows.Forms.Label();
			this.tabPage2 = new System.Windows.Forms.TabPage();
			this.groupBox2 = new System.Windows.Forms.GroupBox();
			this.checkBoxNotifications = new System.Windows.Forms.CheckBox();
			this.groupBox1 = new System.Windows.Forms.GroupBox();
			this.combo_Backend = new System.Windows.Forms.ComboBox();
			this.label_BackendHint = new System.Windows.Forms.Label();
			this.tabControl1 = new System.Windows.Forms.TabControl();
			this.groupBox3 = new System.Windows.Forms.GroupBox();
			this.numeric_InsideTimer = new System.Windows.Forms.NumericUpDown();
			this.label5 = new System.Windows.Forms.Label();
			this.numeric_OutsideTimer = new System.Windows.Forms.NumericUpDown();
			this.label6 = new System.Windows.Forms.Label();
			this.label7 = new System.Windows.Forms.Label();
			this.label8 = new System.Windows.Forms.Label();
			this.tabPage1.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)this.numeric_Port).BeginInit();
			this.tabPage2.SuspendLayout();
			this.groupBox2.SuspendLayout();
			this.groupBox1.SuspendLayout();
			this.tabControl1.SuspendLayout();
			this.groupBox3.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)this.numeric_InsideTimer).BeginInit();
			((System.ComponentModel.ISupportInitialize)this.numeric_OutsideTimer).BeginInit();
			base.SuspendLayout();
			this.button_Ok.DialogResult = System.Windows.Forms.DialogResult.OK;
			this.button_Ok.Location = new System.Drawing.Point(112, 336);
			this.button_Ok.Name = "button_Ok";
			this.button_Ok.Size = new System.Drawing.Size(75, 23);
			this.button_Ok.TabIndex = 0;
			this.button_Ok.Text = "&Ok";
			this.button_Ok.UseVisualStyleBackColor = true;
			this.button_Ok.Click += new System.EventHandler(button_Ok_Click);
			this.button_Close.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this.button_Close.Location = new System.Drawing.Point(193, 336);
			this.button_Close.Name = "button_Close";
			this.button_Close.Size = new System.Drawing.Size(75, 23);
			this.button_Close.TabIndex = 1;
			this.button_Close.Text = "&Close";
			this.button_Close.UseVisualStyleBackColor = true;
			this.tabPage1.Controls.Add(this.textBox_Password);
			this.tabPage1.Controls.Add(this.textBox_Username);
			this.tabPage1.Controls.Add(this.textBox_IPAddress);
			this.tabPage1.Controls.Add(this.label4);
			this.tabPage1.Controls.Add(this.label3);
			this.tabPage1.Controls.Add(this.numeric_Port);
			this.tabPage1.Controls.Add(this.label2);
			this.tabPage1.Controls.Add(this.label1);
			this.tabPage1.Location = new System.Drawing.Point(4, 22);
			this.tabPage1.Name = "tabPage1";
			this.tabPage1.Padding = new System.Windows.Forms.Padding(3);
			this.tabPage1.Size = new System.Drawing.Size(348, 292);
			this.tabPage1.TabIndex = 0;
			this.tabPage1.Text = "uTorrent";
			this.tabPage1.UseVisualStyleBackColor = true;
			this.textBox_Password.Location = new System.Drawing.Point(164, 86);
			this.textBox_Password.Name = "textBox_Password";
			this.textBox_Password.PasswordChar = '*';
			this.textBox_Password.Size = new System.Drawing.Size(150, 20);
			this.textBox_Password.TabIndex = 3;
			this.textBox_Username.Location = new System.Drawing.Point(9, 86);
			this.textBox_Username.Name = "textBox_Username";
			this.textBox_Username.Size = new System.Drawing.Size(143, 20);
			this.textBox_Username.TabIndex = 2;
			this.textBox_IPAddress.Location = new System.Drawing.Point(9, 29);
			this.textBox_IPAddress.Name = "textBox_IPAddress";
			this.textBox_IPAddress.Size = new System.Drawing.Size(222, 20);
			this.textBox_IPAddress.TabIndex = 0;
			this.label4.AutoSize = true;
			this.label4.Location = new System.Drawing.Point(161, 70);
			this.label4.Name = "label4";
			this.label4.Size = new System.Drawing.Size(53, 13);
			this.label4.TabIndex = 6;
			this.label4.Text = "Password";
			this.label3.AutoSize = true;
			this.label3.Location = new System.Drawing.Point(6, 70);
			this.label3.Name = "label3";
			this.label3.Size = new System.Drawing.Size(55, 13);
			this.label3.TabIndex = 5;
			this.label3.Text = "Username";
			this.numeric_Port.Location = new System.Drawing.Point(244, 30);
			this.numeric_Port.Maximum = new decimal(new int[4] { 65535, 0, 0, 0 });
			this.numeric_Port.Name = "numeric_Port";
			this.numeric_Port.Size = new System.Drawing.Size(70, 20);
			this.numeric_Port.TabIndex = 1;
			this.label2.AutoSize = true;
			this.label2.Location = new System.Drawing.Point(241, 13);
			this.label2.Name = "label2";
			this.label2.Size = new System.Drawing.Size(26, 13);
			this.label2.TabIndex = 1;
			this.label2.Text = "Port";
			this.label1.AutoSize = true;
			this.label1.Location = new System.Drawing.Point(6, 13);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(58, 13);
			this.label1.TabIndex = 0;
			this.label1.Text = "IP Address";
			this.tabPage2.Controls.Add(this.groupBox3);
			this.tabPage2.Controls.Add(this.groupBox2);
			this.tabPage2.Controls.Add(this.groupBox1);
			this.tabPage2.Location = new System.Drawing.Point(4, 22);
			this.tabPage2.Name = "tabPage2";
			this.tabPage2.Padding = new System.Windows.Forms.Padding(3);
			this.tabPage2.Size = new System.Drawing.Size(348, 292);
			this.tabPage2.TabIndex = 1;
			this.tabPage2.Text = "Torrent client"; // 2026 (1.3.38): was "Configuration"
			this.tabPage2.UseVisualStyleBackColor = true;
			this.groupBox2.Controls.Add(this.checkBoxNotifications);
			this.groupBox2.Location = new System.Drawing.Point(9, 112);
			this.groupBox2.Name = "groupBox2";
			this.groupBox2.Size = new System.Drawing.Size(323, 56);
			this.groupBox2.TabIndex = 5;
			this.groupBox2.TabStop = false;
			this.groupBox2.Text = "Misc";
			this.checkBoxNotifications.AutoSize = true;
			this.checkBoxNotifications.Location = new System.Drawing.Point(20, 19);
			this.checkBoxNotifications.Name = "checkBoxNotifications";
			this.checkBoxNotifications.Size = new System.Drawing.Size(118, 17);
			this.checkBoxNotifications.TabIndex = 3;
			this.checkBoxNotifications.Text = "&Enable notifications";
			this.checkBoxNotifications.UseVisualStyleBackColor = true;
			this.groupBox1.Controls.Add(this.combo_Backend);
			this.groupBox1.Controls.Add(this.label_BackendHint);
			this.groupBox1.Location = new System.Drawing.Point(6, 6);
			this.groupBox1.Name = "groupBox1";
			this.groupBox1.Size = new System.Drawing.Size(326, 100);
			this.groupBox1.TabIndex = 4;
			this.groupBox1.TabStop = false;
			this.groupBox1.Text = "Backend";
			this.combo_Backend.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.combo_Backend.Items.AddRange(BackendNames);
			this.combo_Backend.Location = new System.Drawing.Point(15, 22);
			this.combo_Backend.Name = "combo_Backend";
			this.combo_Backend.Size = new System.Drawing.Size(295, 21);
			this.combo_Backend.TabIndex = 0;
			this.combo_Backend.SelectedIndexChanged += new System.EventHandler(combo_Backend_SelectedIndexChanged);
			this.label_BackendHint.Location = new System.Drawing.Point(15, 50);
			this.label_BackendHint.Name = "label_BackendHint";
			this.label_BackendHint.Size = new System.Drawing.Size(295, 45);
			this.label_BackendHint.TabIndex = 1;
			this.tabControl1.Controls.Add(this.tabPage2);
			this.tabControl1.Controls.Add(this.tabPage1);
			this.tabControl1.Location = new System.Drawing.Point(12, 12);
			this.tabControl1.Name = "tabControl1";
			this.tabControl1.SelectedIndex = 0;
			this.tabControl1.Size = new System.Drawing.Size(356, 318);
			this.tabControl1.TabIndex = 2;
			this.groupBox3.Controls.Add(this.label8);
			this.groupBox3.Controls.Add(this.label7);
			this.groupBox3.Controls.Add(this.numeric_OutsideTimer);
			this.groupBox3.Controls.Add(this.label6);
			this.groupBox3.Controls.Add(this.numeric_InsideTimer);
			this.groupBox3.Controls.Add(this.label5);
			this.groupBox3.Location = new System.Drawing.Point(9, 174);
			this.groupBox3.Name = "groupBox3";
			this.groupBox3.Size = new System.Drawing.Size(323, 100);
			this.groupBox3.TabIndex = 6;
			this.groupBox3.TabStop = false;
			this.groupBox3.Text = "Update Frequency";
			this.numeric_InsideTimer.Location = new System.Drawing.Point(114, 19);
			this.numeric_InsideTimer.Maximum = new decimal(new int[4] { 65535, 0, 0, 0 });
			this.numeric_InsideTimer.Name = "numeric_InsideTimer";
			this.numeric_InsideTimer.Size = new System.Drawing.Size(51, 20);
			this.numeric_InsideTimer.TabIndex = 3;
			this.numeric_InsideTimer.Value = new decimal(new int[4] { 5, 0, 0, 0 });
			this.label5.AutoSize = true;
			this.label5.Location = new System.Drawing.Point(17, 22);
			this.label5.Name = "label5";
			this.label5.Size = new System.Drawing.Size(91, 13);
			this.label5.TabIndex = 2;
			this.label5.Text = "Inside MyTorrents";
			this.numeric_OutsideTimer.Location = new System.Drawing.Point(114, 44);
			this.numeric_OutsideTimer.Maximum = new decimal(new int[4] { 65535, 0, 0, 0 });
			this.numeric_OutsideTimer.Name = "numeric_OutsideTimer";
			this.numeric_OutsideTimer.Size = new System.Drawing.Size(51, 20);
			this.numeric_OutsideTimer.TabIndex = 5;
			this.numeric_OutsideTimer.Value = new decimal(new int[4] { 240, 0, 0, 0 });
			this.label6.AutoSize = true;
			this.label6.Location = new System.Drawing.Point(9, 47);
			this.label6.Name = "label6";
			this.label6.Size = new System.Drawing.Size(99, 13);
			this.label6.TabIndex = 4;
			this.label6.Text = "Outside MyTorrents";
			this.label7.AutoSize = true;
			this.label7.Location = new System.Drawing.Point(168, 22);
			this.label7.Name = "label7";
			this.label7.Size = new System.Drawing.Size(47, 13);
			this.label7.TabIndex = 6;
			this.label7.Text = "seconds";
			this.label8.AutoSize = true;
			this.label8.Location = new System.Drawing.Point(168, 47);
			this.label8.Name = "label8";
			this.label8.Size = new System.Drawing.Size(47, 13);
			this.label8.TabIndex = 7;
			this.label8.Text = "seconds";
			base.AcceptButton = this.button_Ok;
			base.AutoScaleDimensions = new System.Drawing.SizeF(6f, 13f);
			base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			base.CancelButton = this.button_Close;
			base.ClientSize = new System.Drawing.Size(380, 371);
			base.Controls.Add(this.tabControl1);
			base.Controls.Add(this.button_Close);
			base.Controls.Add(this.button_Ok);
			base.Name = "SettingsForm";
			this.Text = "MyTorrent settings";
			base.FormClosing += new System.Windows.Forms.FormClosingEventHandler(SettingsForm_FormClosing);
			base.Load += new System.EventHandler(SettingsForm_Load);
			this.tabPage1.ResumeLayout(false);
			this.tabPage1.PerformLayout();
			((System.ComponentModel.ISupportInitialize)this.numeric_Port).EndInit();
			this.tabPage2.ResumeLayout(false);
			this.groupBox2.ResumeLayout(false);
			this.groupBox2.PerformLayout();
			this.groupBox1.ResumeLayout(false);
			this.groupBox1.PerformLayout();
			this.tabControl1.ResumeLayout(false);
			this.groupBox3.ResumeLayout(false);
			this.groupBox3.PerformLayout();
			((System.ComponentModel.ISupportInitialize)this.numeric_InsideTimer).EndInit();
			((System.ComponentModel.ISupportInitialize)this.numeric_OutsideTimer).EndInit();
			base.ResumeLayout(false);
		}
	}
}
