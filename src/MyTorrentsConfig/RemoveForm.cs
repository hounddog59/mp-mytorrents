using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Sohvaperuna.MyTorrentsConfig
{
	/// <summary>
	/// 2026 (1.3.42): "Remove MyTorrents" - also takes away the programs the setup assistant installed (only those, each
	/// with its own tick box), and MyTorrents' settings if you want. Downloads and the library are never touched.
	/// MyTorrents itself is removed last in MediaPortal's Extension Manager.
	/// </summary>
	internal sealed class RemoveForm : Form
	{
		private sealed class Program_
		{
			public string Key, Name, Note;
			public CheckBox Box;
		}

		private readonly List<Program_> _programs = new List<Program_>();
		private readonly RadioButton _keepSettings = new RadioButton { Text = "Keep my MyTorrents settings (if I install it again, they're still there)", AutoSize = true, Checked = true };
		private readonly RadioButton _removeSettings = new RadioButton { Text = "Remove my MyTorrents settings and lists too", AutoSize = true };
		private readonly Button _go = new Button { Text = "Uninstall", Width = 90 };
		private readonly Button _close = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
		private readonly Label _status = new Label { AutoSize = false, ForeColor = Color.DimGray };

		public RemoveForm()
		{
			Text = "Uninstall MyTorrents";
			Font = new Font("Microsoft Sans Serif", 8.25f);
			StartPosition = FormStartPosition.CenterScreen;
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MaximizeBox = MinimizeBox = false;
			ClientSize = new Size(520, 360);
			try
			{
				Icon = Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
			}
			catch
			{
			}

			int y = 12;
			Controls.Add(new Label { Text = "Also remove these programs? Tick the ones you don't use for anything else.", Location = new Point(12, y), AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
			y += 24;
			List<string> ours = SetupCore.InstalledBySetup();
			bool noRecord = !File.Exists(SetupCore.InstalledFile);
			Add("qbittorrent", "qBittorrent", SetupCore.QBittorrent().Installed, ours, noRecord);
			Add("biglybt", "BiglyBT", SetupCore.BiglyBT().Installed, ours, noRecord);
			Add("jackett", "Jackett (finds torrents on the sites)", SetupCore.Jackett().Installed, ours, noRecord);
			Add("flaresolverr", "FlareSolverr (helps Jackett with some sites)", SetupCore.FindFlareSolverrExe() != null, ours, true);
			if (_programs.Count == 0)
			{
				Controls.Add(new Label { Text = "None - the setup assistant didn't install any programs on this PC.", Location = new Point(24, y), AutoSize = true, ForeColor = Color.DimGray });
				y += 22;
			}
			foreach (Program_ p in _programs)
			{
				p.Box.Location = new Point(24, y);
				Controls.Add(p.Box);
				y += 22;
				if (p.Note.Length > 0)
				{
					Controls.Add(new Label { Text = p.Note, Location = new Point(42, y - 4), AutoSize = true, ForeColor = Color.DimGray });
					y += 16;
				}
			}
			y += 10;
			Controls.Add(new Label { Text = "Your settings", Location = new Point(12, y), AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
			y += 22;
			_keepSettings.Location = new Point(24, y);
			Controls.Add(_keepSettings);
			y += 22;
			_removeSettings.Location = new Point(24, y);
			Controls.Add(_removeSettings);
			y += 30;
			Controls.Add(new Label
			{
				Text = "Your downloads and your TV and movie library are never touched.\r\nLast step: MyTorrents itself is removed in MediaPortal's Extension Manager (this window opens it for you).",
				Location = new Point(12, y), Size = new Size(496, 32)
			});
			y += 40;
			_status.Location = new Point(12, y);
			_status.Size = new Size(496, 32);
			Controls.Add(_status);
			_go.Location = new Point(ClientSize.Width - 200, ClientSize.Height - 34);
			_close.Location = new Point(ClientSize.Width - 102, ClientSize.Height - 34);
			_go.Click += (s, e) => Go();
			Controls.Add(_go);
			Controls.Add(_close);
			CancelButton = _close;
			ClientSize = new Size(520, Math.Max(300, y + 80));
			_go.Top = _close.Top = ClientSize.Height - 34;
		}

		private void Add(string key, string name, bool installed, List<string> ours, bool unsure)
		{
			if (!installed) return;
			bool mine = ours.Contains(key);
			if (!mine && !unsure) return; // installed before MyTorrents - not ours to offer
			_programs.Add(new Program_
			{
				Key = key,
				Name = name,
				Note = mine ? "" : key == "flaresolverr" ? "" : "MyTorrents can't tell whether its setup installed this or you had it before",
				Box = new CheckBox { Text = name + (mine || key == "flaresolverr" ? "  - installed by the MyTorrents setup" : ""), AutoSize = true }
			});
		}

		private void Go()
		{
			List<Program_> picked = _programs.Where(p => p.Box.Checked).ToList();
			string what = (picked.Count > 0 ? string.Join(", ", picked.Select(p => p.Name.Split('(')[0].Trim())) : "no programs") +
				(_removeSettings.Checked ? " and your MyTorrents settings" : "");
			if (MessageBox.Show(this, "Uninstall: remove " + what + "?\r\n\r\nClose MediaPortal first if it is running.", Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
			_go.Enabled = _close.Enabled = false;
			bool removeSettings = _removeSettings.Checked;
			new Thread(() =>
			{
				List<string> problems = new List<string>();
				try
				{
					// 2026 (1.3.60): the desktop icon (the installer leaves it in place at updates, so it's taken away here)
					try
					{
						string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "MyTorrents Configuration.lnk");
						if (File.Exists(lnk)) File.Delete(lnk);
					}
					catch
					{
					}
					// 2026 (1.3.45): the background helper - stop it and don't start it with Windows any more
					try
					{
						Sohvaperuna.MyTorrents.BackgroundPage.StopHelper();
						using (RegistryKey run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
						{
							run?.DeleteValue(Sohvaperuna.MyTorrents.BackgroundPage.RunValue, false);
						}
					}
					catch
					{
					}
					// FlareSolverr is MyTorrents' own copy - no administrator rights needed
					if (picked.Any(p => p.Key == "flaresolverr"))
					{
						Say("Removing FlareSolverr...");
						try
						{
							SetupCore.StopFlareSolverr();
							Thread.Sleep(1500);
							using (RegistryKey run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
							{
								run?.DeleteValue(SetupCore.RunKeyName, false);
							}
							if (Directory.Exists(SetupCore.FlareSolverrDir)) Directory.Delete(SetupCore.FlareSolverrDir, true);
						}
						catch (Exception ex)
						{
							problems.Add("FlareSolverr: " + ex.Message);
						}
					}
					List<string> admin = new List<string>();
					if (picked.Any(p => p.Key == "qbittorrent")) admin.Add("winget-uninstall|" + SetupCore.QbWingetId);
					if (picked.Any(p => p.Key == "biglybt")) admin.Add("winget-uninstall|" + SetupCore.BiglyWingetId);
					if (picked.Any(p => p.Key == "jackett")) admin.Add("jackett-uninstall");
					if (admin.Count > 0)
					{
						Say("Removing programs - answer Yes if Windows asks for permission. This can take a few minutes.");
						foreach (string r in SetupCore.RunAdmin(admin))
						{
							if (r.StartsWith("FAIL|")) problems.Add(r.Substring(5).Replace("|", ": "));
						}
					}
					if (removeSettings)
					{
						Say("Removing your MyTorrents settings...");
						int failed = Sohvaperuna.MyTorrents.MyFiles.DeleteAll();
						if (failed > 0) problems.Add(failed + " settings file(s) could not be deleted (is MediaPortal still running?)");
						try
						{
							if (File.Exists(SetupCore.InstalledFile)) File.Delete(SetupCore.InstalledFile);
						}
						catch
						{
						}
					}
				}
				catch (Exception ex)
				{
					problems.Add(ex.Message);
				}
				Invoke((MethodInvoker)(() =>
				{
					_status.Text = "";
					string done = problems.Count == 0 ? "Done." : "Done, but some things could not be removed:\r\n\r\n" + string.Join("\r\n", problems) + "\r\n\r\nYou can remove those in Windows Settings > Apps.";
					MessageBox.Show(this, done + "\r\n\r\nLast step: in MediaPortal's Extension Manager, find MyTorrents and click Uninstall. It opens now.", Text, MessageBoxButtons.OK,
						problems.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
					OpenExtensionManager();
					Close();
				}));
			}) { IsBackground = true, Name = "Remove MyTorrents" }.Start();
		}

		private void Say(string text)
		{
			try
			{
				Invoke((MethodInvoker)(() => _status.Text = text));
			}
			catch
			{
			}
		}

		private static void OpenExtensionManager()
		{
			try
			{
				string exe = Path.Combine(SetupCore.MediaPortalDir, "MPEInstaller.exe");
				if (File.Exists(exe)) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
			}
			catch
			{
			}
		}
	}
}
