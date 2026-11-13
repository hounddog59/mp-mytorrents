using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Sohvaperuna.TorrentSearch;

namespace Sohvaperuna.MyTorrents
{
	/// <summary>
	/// 2026: "Shows" tab - the shows the Auto grabber looks after (A-Z, as MediaPortal last saw them), "Get the next
	/// episode now - just this once", and the size report.
	/// </summary>
	public class ShowsPage : TabPage
	{
		private readonly ListView _list = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
		private readonly Button _get = new Button { Text = "Get next episode now...", Width = 160 };
		private readonly Button _refresh = new Button { Text = "Refresh", Width = 80 };
		private readonly Button _checkAll = new Button { Text = "Check all now", Width = 100 };   // 2026 (1.3.38)
		private readonly Button _removed = new Button { Text = "Removed shows...", Width = 115 }; // 2026 (1.3.38)
		private readonly Button _missing = new Button { Text = "Missing report...", Width = 110 };   // 2026 (1.3.41)
		private readonly Button _cantFind = new Button { Text = "Can't find...", Width = 90 }; // 2026 (1.3.42)      // 2026 (1.3.41)
		private ListSorter _sorter;
		// 2026 (1.3.45): Automatic / Manual - one line at the top (was a big box on the TV shows tab); saved straight away
		private readonly ComboBox _mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
		private readonly ToolTip _tips = new ToolTip { AutoPopDelay = 15000 };
		private readonly Label _note = new Label { AutoSize = false, ForeColor = Color.DimGray };
		private readonly TextBox _report = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font(FontFamily.GenericMonospace, 8.25f), BackColor = SystemColors.Window };

		public ShowsPage()
		{
			Text = "Show list"; // 2026 (1.3.45): was "Shows"; 1.3.52: was "My shows"
			Padding = new Padding(3);
			UseVisualStyleBackColor = true;
			Font = new Font("Microsoft Sans Serif", 8.25f);

			_list.Columns.Add("Show", 190);
			_list.Columns.Add("Next", 60);
			_list.Columns.Add("Missing", 55);
			_list.Columns.Add("Airs", 75);
			_list.Columns.Add("Last look", 200);
			Label modeLabel = new Label { Text = "How MyTorrents works", Location = new Point(8, 11), AutoSize = true };
			_mode.Location = new Point(130, 8);
			_mode.Items.AddRange(new object[] { "Automatic - keeps my running shows up to date", "Manual - only what I search for, add or grab" });
			_mode.SelectedIndex = AutoAdd.Enabled ? 0 : 1;
			_mode.SelectedIndexChanged += (s, e) =>
			{
				Configuration.SetExtra("AutoGrabber.AutoAddRunning", _mode.SelectedIndex == 0 ? "true" : "false");
				_note.Text = _mode.SelectedIndex == 0 ? "Automatic: your running shows are kept up to date by themselves." : "Manual: only what you search for, add or grab the missing ones of.";
			};
			_tips.SetToolTip(_mode, "Automatic: running shows in your TV library are added to the Auto grabber and new episodes are downloaded as they come out.\r\n" +
				"Manual: nothing is added by itself - only what you search for, shows you add, and \"Grab the missing ones\".");
			Controls.Add(modeLabel);
			Controls.Add(_mode);
			_list.Location = new Point(8, 36);
			_list.SelectedIndexChanged += (s, e) => _get.Enabled = Selected() != null && Selected().Next.Length > 0;
			_list.DoubleClick += (s, e) => GetNow();
			// 2026 (1.3.38): click a heading to sort; right-click a show for Check now / Search again / Remove
			_sorter = new ListSorter(_list, "Shows")
				.Key(1, i => EpisodeKey(((AutoGrabberStatus.Show)i.Tag).Next))
				.Key(2, i => ((AutoGrabberStatus.Show)i.Tag).MissingCount)
				.Key(3, i => { DateTime d = ((AutoGrabberStatus.Show)i.Tag).AirsAt; return d == DateTime.MaxValue ? null : (IComparable)d; })
				.Key(4, i => { DateTime d = ((AutoGrabberStatus.Show)i.Tag).LookedAt; return d == DateTime.MinValue ? null : (IComparable)d; });
			ContextMenuStrip menu = new ContextMenuStrip();
			ToolStripItem checkOne = menu.Items.Add("Check this show now", null, (s, e) => CheckNow(Selected()));
			ToolStripItem again = menu.Items.Add("Search again for the next episode...", null, (s, e) => GetNow());
			ToolStripItem different = menu.Items.Add("Try a different copy...", null, (s, e) => DifferentCopy(Selected())); // 2026 (1.3.42)
			ToolStripItem whatsMissing = menu.Items.Add("What's missing...", null, (s, e) => WhatsMissing(Selected()));
			ToolStripItem grabMissing = menu.Items.Add("Grab the missing ones", null, (s, e) => GrabMissing(Selected()));
			menu.Items.Add(new ToolStripSeparator());
			// 2026 (1.3.52): look the show up
			menu.Items.Add("Look it up on TheTVDB", null, (s, e) => { AutoGrabberStatus.Show x = Selected(); if (x != null) LookUp.Tvdb(x.SearchName.Length > 0 ? x.SearchName : x.Name, x.Id); });
			menu.Items.Add("Look it up on TMDb", null, (s, e) => { AutoGrabberStatus.Show x = Selected(); if (x != null) LookUp.TmdbTv(x.SearchName.Length > 0 ? x.SearchName : x.Name); });
			menu.Items.Add(new ToolStripSeparator());
			ToolStripItem remove = menu.Items.Add("Remove from the Auto grabber...", null, (s, e) => Remove(Selected()));
			menu.Opening += (s, e) =>
			{
				AutoGrabberStatus.Show x = Selected();
				if (x == null) { e.Cancel = true; return; }
				checkOne.Enabled = remove.Enabled = true;
				again.Enabled = x.Next.Length > 0;
				grabMissing.Enabled = x.MissingCount > 0;
				different.Enabled = Grabbed.ForShow(x.Id).Count > 0;
			};
			_list.ContextMenuStrip = menu;
			_list.MouseDown += (s, e) =>
			{
				if (e.Button != MouseButtons.Right) return;
				ListViewItem hit = _list.GetItemAt(e.X, e.Y);
				if (hit != null) { _list.SelectedItems.Clear(); hit.Selected = true; }
			};
			_checkAll.Click += (s, e) => CheckNow(null);
			_removed.Click += (s, e) => ShowRemoved();
			_missing.Click += (s, e) => MissingReport();
			_cantFind.Click += (s, e) => CantFindDialog();
			_get.Location = new Point(8, 0);
			_get.Click += (s, e) => GetNow();
			_get.Enabled = false;
			_refresh.Click += (s, e) => Fill();
			_note.Location = new Point(8, 0);

			// 2026 (1.3.42): buttons grow to fit their text (large text sizes / TVs cut "Wanted movies..." short)
			foreach (Button b in new[] { _get, _refresh, _checkAll, _removed, _missing, _cantFind })
			{
				b.AutoSize = true;
				b.AutoSizeMode = AutoSizeMode.GrowAndShrink; // just as wide as the text needs
				b.Padding = new Padding(6, 0, 6, 0);
				b.MinimumSize = new Size(70, 23);
			}
			Controls.Add(_list);
			Controls.Add(_get);
			Controls.Add(_refresh);
			Controls.Add(_checkAll);
			Controls.Add(_removed);
			Controls.Add(_missing);
			Controls.Add(_cantFind);
			Controls.Add(_note);
			Controls.Add(new Label { Text = "Size report - what size limit fits", Font = new Font("Microsoft Sans Serif", 8.25f, FontStyle.Bold), AutoSize = true, Name = "reportHeading" });
			Controls.Add(_report);

			VisibleChanged += (s, e) => { if (Visible) Fill(); };
			Resize += (s, e) => Arrange();
		}

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);
			Arrange();
		}

		private void Arrange()
		{
			int w = Math.Max(300, ClientSize.Width - 16);
			int h = ClientSize.Height;
			int reportHeight = 150;
			int listHeight = Math.Max(80, h - 36 - 30 - 20 - reportHeight - 12);
			_list.Size = new Size(w, listHeight);
			_get.Top = _refresh.Top = _checkAll.Top = _removed.Top = _missing.Top = _cantFind.Top = _list.Bottom + 4;
			_refresh.Left = _get.Right + 6;
			_checkAll.Left = _refresh.Right + 6;
			_removed.Left = _checkAll.Right + 6;
			_missing.Left = _removed.Right + 6;
			_cantFind.Left = _missing.Right + 6;
			_note.Location = new Point(_cantFind.Right + 10, _get.Top + 5);
			_note.Size = new Size(Math.Max(50, w - _note.Left + 8), 18);
			Control heading = Controls["reportHeading"];
			heading.Location = new Point(8, _get.Bottom + 8);
			_report.Location = new Point(8, heading.Bottom + 4);
			_report.Size = new Size(w, Math.Max(40, h - _report.Top - 6));
			_list.Columns[4].Width = Math.Max(120, _list.ClientSize.Width - 190 - 60 - 55 - 75 - 4);
		}

		private AutoGrabberStatus.Show Selected() => _list.SelectedItems.Count == 1 ? _list.SelectedItems[0].Tag as AutoGrabberStatus.Show : null;

		private void Fill()
		{
			string keep = Selected()?.Id;
			List<AutoGrabberStatus.Show> shows = AutoGrabberStatus.Read().OrderBy(x => WatchListView.SortName(x.Name)).ToList();
			_list.BeginUpdate();
			_list.Items.Clear();
			foreach (AutoGrabberStatus.Show x in shows)
			{
				ListViewItem item = new ListViewItem(new[] { x.Name, x.Next.Length > 0 ? x.Next : "-", x.MissingCount > 0 ? x.MissingCount.ToString() : "-", x.Airs, x.LastLook }) { Tag = x };
				_list.Items.Add(item);
				if (x.Id == keep) item.Selected = true;
			}
			_list.EndUpdate();
			_sorter.Apply();
			DateTime when = AutoGrabberStatus.Written;
			_note.Text = shows.Count == 0 ? "MediaPortal writes this list while it runs - start it once."
				: shows.Count + " shows, as MediaPortal saw them at " + when.ToString("h:mm tt");
			List<string> report;
			try
			{
				report = SizeRecord.Report(SeriesItem.MaxSizeMB);
			}
			catch (Exception ex)
			{
				report = new List<string> { "Could not read the size record: " + ex.Message };
			}
			_report.Text = string.Join("\r\n", report);
			_get.Enabled = Selected() != null && Selected().Next.Length > 0;
		}

		private void GetNow()
		{
			AutoGrabberStatus.Show show = Selected();
			if (show == null || show.Next.Length == 0) return;
			Cursor = Cursors.WaitCursor;
			_get.Enabled = false;
			_note.Text = "Searching your sites for " + show.Name + " " + show.Next + "...";
			ThreadPool.QueueUserWorkItem(_ =>
			{
				string error = null;
				List<TorrentMatch> copies = new List<TorrentMatch>();
				try
				{
					copies = EpisodeCopies.Find(show.Id, show.SearchName, show.Next, out error);
				}
				catch (Exception ex)
				{
					error = ex.Message;
				}
				try
				{
					BeginInvoke((MethodInvoker)(() =>
					{
						Cursor = Cursors.Default;
						_get.Enabled = true;
						_note.Text = "";
						if (copies.Count == 0)
						{
							MessageBox.Show(this, error ?? (show.Name + " " + show.Next + ": no copies found yet (none seeded, or only fakes)."), "Get " + show.Next + " now", MessageBoxButtons.OK, MessageBoxIcon.Information);
							return;
						}
						TorrentMatch chosen = Pick(show, copies);
						if (chosen == null) return;
						try
						{
							GrabRequests.Add(show.Id, show.Name, show.Next, chosen);
							MessageBox.Show(this, "Sent to MediaPortal:\r\n\r\n" + chosen.Title + "\r\n\r\nIt starts the download within 20 seconds (or when MediaPortal next starts), " +
								"and copies it to your library when it has finished. Your settings are not changed.", "Get " + show.Next + " now", MessageBoxButtons.OK, MessageBoxIcon.Information);
						}
						catch (Exception ex)
						{
							MessageBox.Show(this, "Could not hand it to MediaPortal: " + ex.Message, "MyTorrents", MessageBoxButtons.OK, MessageBoxIcon.Warning);
						}
					}));
				}
				catch
				{
				}
			});
		}

		/// "S02E07" -> 2007 for sorting; nothing due -> null (at the end).
		private static IComparable EpisodeKey(string code)
		{
			System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(code ?? "", @"S(\d+)E(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
			return m.Success ? (IComparable)(int.Parse(m.Groups[1].Value) * 1000 + int.Parse(m.Groups[2].Value)) : null;
		}

		/// 2026 (1.3.38): "Check now" - MediaPortal looks for the due episodes within 20 seconds instead of at the next check.
		private void CheckNow(AutoGrabberStatus.Show show)
		{
			try
			{
				if (show == null) GrabRequests.AddCheck("*", "all shows");
				else GrabRequests.AddCheck(show.Id, show.Name);
				_note.Text = "Sent to MediaPortal - it checks " + (show == null ? "all your shows" : show.Name) + " within 20 seconds.";
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, "Could not hand it to MediaPortal: " + ex.Message, "MyTorrents", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
		}

		/// 2026 (1.3.42): episodes and movies looked for a long time without a copy that fits.
		private void CantFindDialog()
		{
			using (Form f = new Form())
			{
				f.Text = "Can't find";
				f.StartPosition = FormStartPosition.CenterParent;
				f.ClientSize = new Size(640, 400);
				f.MinimizeBox = false;
				f.ShowInTaskbar = false;
				f.Font = Font;
				Label l = new Label { Location = new Point(10, 10), Size = new Size(620, 30), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
				ListView lv = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = true, Location = new Point(10, 42), Size = new Size(620, 310), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
				LookUp.AddTo(lv, it => { CantFind.Item x = it.Tag as CantFind.Item; int y; return x == null ? null : x.IsMovie ? LookUp.What.Movie(x.Name, int.TryParse(x.Year, out y) ? y : 0) : LookUp.What.Show(x.Name, x.ShowId); }); // 1.3.52
				lv.Columns.Add("What", 230);
				lv.Columns.Add("Looking for", 80);
				lv.Columns.Add("Why", 290);
				ListSorter cantSorter = new ListSorter(lv, "CantFind"); // 1.3.60
				Button look = new Button { Text = "Look now", Location = new Point(10, 362), Width = 90, Enabled = false, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
				Button stop = new Button { Text = "Stop looking", Location = new Point(106, 362), Width = 100, Enabled = false, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
				Button close = new Button { Text = "Close", DialogResult = DialogResult.Cancel, Location = new Point(550, 362), Width = 80, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
				Action fill = () =>
				{
					List<CantFind.Item> items = CantFind.All();
					l.Text = items.Count == 0 ? "Nothing - everything was found within " + CantFind.Days + " days of looking." :
						"Looked for " + CantFind.Days + " days or more without a copy that fits. They're still looked for once a day (not the stopped ones).";
					lv.BeginUpdate();
					lv.Items.Clear();
					foreach (CantFind.Item x in items)
					{
						ListViewItem i = new ListViewItem(new[] { (x.IsMovie ? "Movie: " : "") + x.What, x.Since, (x.State == "skip" ? "(stopped) " : "") + x.Why }) { Tag = x };
						if (x.State == "skip") i.ForeColor = Color.Gray;
						lv.Items.Add(i);
					}
					lv.EndUpdate();
					cantSorter.Apply();
				};
				lv.SelectedIndexChanged += (s, e) => look.Enabled = stop.Enabled = lv.SelectedItems.Count > 0;
				look.Click += (s, e) =>
				{
					foreach (ListViewItem i in lv.SelectedItems)
					{
						CantFind.Item x = (CantFind.Item)i.Tag;
						if (x.IsMovie) WantedFilms.LookAgain(x.Name, x.Year);
						else
						{
							CantFind.LookNow(x.Key);
							GrabRequests.AddCheck(x.ShowId, x.Name);
						}
					}
					fill();
					MessageBox.Show(f, "Sent to MediaPortal - it looks for them within a few minutes.", "Can't find", MessageBoxButtons.OK, MessageBoxIcon.Information);
				};
				stop.Click += (s, e) =>
				{
					if (MessageBox.Show(f, "Stop looking for the ones you picked?\r\n\r\nMovies are taken off Wanted movies. Episodes stay on this list (greyed) - pick one and click Look now to start again.",
						"Can't find", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
					foreach (ListViewItem i in lv.SelectedItems)
					{
						CantFind.Item x = (CantFind.Item)i.Tag;
						if (x.IsMovie) WantedFilms.Remove(x.Name, x.Year);
						else CantFind.StopLooking(x.Key);
					}
					fill();
				};
				f.Controls.AddRange(new Control[] { l, lv, look, stop, close });
				f.CancelButton = close;
				fill();
				f.ShowDialog(this);
			}
		}

		/// 2026 (1.3.42): pick an episode the Auto grabber got this week and look for a different copy of it.
		private void DifferentCopy(AutoGrabberStatus.Show show)
		{
			if (show == null) return;
			List<Tuple<string, string, string, DateTime>> got = Grabbed.ForShow(show.Id);
			if (got.Count == 0) return;
			using (Form f = new Form())
			{
				f.Text = "Try a different copy - " + show.Name;
				f.StartPosition = FormStartPosition.CenterParent;
				f.ClientSize = new Size(520, 300);
				f.MinimizeBox = f.MaximizeBox = false;
				f.ShowInTaskbar = false;
				f.Font = Font;
				Label l = new Label { Text = "Episodes the Auto grabber got this week. Pick one - this copy is not used again.", Location = new Point(10, 10), Size = new Size(500, 18) };
				ListBox box = new ListBox { Location = new Point(10, 32), Size = new Size(500, 170), IntegralHeight = false };
				foreach (Tuple<string, string, string, DateTime> g in got) box.Items.Add(g.Item1 + "  -  " + g.Item2 + "  (" + g.Item4.ToString("ddd d MMM") + ")");
				RadioButton del = new RadioButton { Text = "Delete this copy and its files", Location = new Point(10, 210), AutoSize = true, Checked = true };
				RadioButton keep = new RadioButton { Text = "Keep this copy's files (only take it out of the torrent program)", Location = new Point(10, 232), AutoSize = true };
				Label note = new Label { Text = "Episodes already in your library are never touched.", Location = new Point(10, 256), AutoSize = true, ForeColor = Color.DimGray };
				Button ok = new Button { Text = "Try another", DialogResult = DialogResult.OK, Location = new Point(340, 266), Width = 80, Enabled = false };
				Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(430, 266), Width = 80 };
				box.SelectedIndexChanged += (s, e) => ok.Enabled = box.SelectedIndex >= 0;
				f.Controls.AddRange(new Control[] { l, box, del, keep, note, ok, cancel });
				f.AcceptButton = ok;
				f.CancelButton = cancel;
				if (f.ShowDialog(this) != DialogResult.OK || box.SelectedIndex < 0) return;
				Tuple<string, string, string, DateTime> pick = got[box.SelectedIndex];
				try
				{
					GrabRequests.AddDifferentCopy(pick.Item3, pick.Item2, del.Checked);
					GrabRequests.AddCheck(show.Id, show.Name);
					_note.Text = "Sent to MediaPortal - it looks for a different copy of " + show.Name + " " + pick.Item1 + " within 20 seconds.";
				}
				catch (Exception ex)
				{
					MessageBox.Show(this, "Could not hand it to MediaPortal: " + ex.Message, "MyTorrents", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				}
			}
		}

		/// 2026 (1.3.41): the aired episodes a show is missing, season by season.
		private void WhatsMissing(AutoGrabberStatus.Show show)
		{
			if (show == null) return;
			string text = MissingText(show.Missing);
			if (show.MissingCount == 0)
			{
				MessageBox.Show(this, show.Name + " is up to date - nothing that has aired is missing." + (show.Next.Length > 0 ? "\r\n\r\nNext to get: " + show.Next + (show.Airs.Length > 0 ? " (airs " + show.Airs + ")" : "") : ""), "What's missing", MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}
			if (MessageBox.Show(this, show.Name + " - " + show.MissingCount + " aired episodes missing:\r\n\r\n" + text + "\r\n\r\nGrab the missing ones now?", "What's missing", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
				GrabMissing(show);
		}

		internal static string MissingText(string codes)
		{
			List<string> list = (codes ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Where(c => c.Length >= 4).ToList();
			return string.Join("\r\n", list.GroupBy(c => c.Substring(0, 3)).OrderBy(g => g.Key).Select(g =>
			{
				int n;
				int.TryParse(g.Key.Substring(1), out n);
				return "Season " + n + ": " + g.Count() + " missing - " + string.Join(", ", g.Select(c => c.Substring(3)));
			}));
		}

		private void GrabMissing(AutoGrabberStatus.Show show)
		{
			if (show == null) return;
			try
			{
				GrabRequests.AddGrabMissing(show.Id, show.Name);
				_note.Text = "Sent to MediaPortal - it looks for " + show.Name + "'s missing episodes within 20 seconds.";
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, "Could not hand it to MediaPortal: " + ex.Message, "MyTorrents", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
		}

		/// 2026 (1.3.41): every show in your library with aired episodes missing (written by MediaPortal).
		private void MissingReport()
		{
			List<string[]> rows = Extras.ReadReport();
			using (Form f = new Form())
			{
				f.Text = "Missing episodes - whole library";
				f.StartPosition = FormStartPosition.CenterParent;
				f.ClientSize = new Size(560, 400);
				f.MinimizeBox = false;
				f.ShowInTaskbar = false;
				f.Font = Font;
				Label l = new Label { Text = rows.Count == 0 ? "Nothing missing - or MediaPortal hasn't written the report yet (it does while it runs)." : rows.Count + " shows have aired episodes you don't have. Pick one to see what's missing.", Location = new Point(10, 10), Size = new Size(540, 30), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
				ListView lv = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = true, Location = new Point(10, 42), Size = new Size(540, 200), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
				lv.Columns.Add("Show", 400);
				lv.Columns.Add("Missing", 100);
				foreach (string[] r in rows) lv.Items.Add(new ListViewItem(new[] { r[1], r[2] }) { Tag = r });
				new ListSorter(lv, "MissingReport").Key(1, i => { int n; int.TryParse(((string[])i.Tag)[2], out n); return n; });
				LookUp.AddTo(lv, i => LookUp.What.Show(((string[])i.Tag)[1], ((string[])i.Tag)[0])); // 1.3.52
				TextBox detail = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Location = new Point(10, 248), Size = new Size(540, 110), BackColor = SystemColors.Window, Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
				Button grab = new Button { Text = "Grab the missing ones", Location = new Point(300, 366), Width = 160, Enabled = false, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
				Button close = new Button { Text = "Close", DialogResult = DialogResult.Cancel, Location = new Point(470, 366), Width = 80, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
				lv.SelectedIndexChanged += (s, e) =>
				{
					grab.Enabled = lv.SelectedItems.Count > 0;
					detail.Text = lv.SelectedItems.Count == 1 ? MissingText(((string[])lv.SelectedItems[0].Tag)[3]) : lv.SelectedItems.Count > 1 ? lv.SelectedItems.Count + " shows picked" : "";
				};
				grab.Click += (s, e) =>
				{
					try
					{
						foreach (ListViewItem i in lv.SelectedItems) GrabRequests.AddGrabMissing(((string[])i.Tag)[0], ((string[])i.Tag)[1]);
						MessageBox.Show(f, "Sent to MediaPortal - it starts looking within 20 seconds. Shows not in the Auto grabber yet are added to it.", "Missing episodes", MessageBoxButtons.OK, MessageBoxIcon.Information);
					}
					catch (Exception ex)
					{
						MessageBox.Show(f, "Could not hand it to MediaPortal: " + ex.Message, "MyTorrents", MessageBoxButtons.OK, MessageBoxIcon.Warning);
					}
				};
				f.Controls.AddRange(new Control[] { l, lv, detail, grab, close });
				f.CancelButton = close;
				f.ShowDialog(this);
			}
		}

		/// 2026 (1.3.38): take a show out of the Auto grabber (same as in MediaPortal: it isn't added again by itself).
		private void Remove(AutoGrabberStatus.Show show)
		{
			if (show == null) return;
			if (MessageBox.Show(this, "Take " + show.Name + " out of the Auto grabber?\r\n\r\nIt stops looking for its episodes and isn't added again by itself. " +
				"Episodes you already have are not touched. You can put it back with \"Removed shows...\".", "Remove " + show.Name,
				MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
			try
			{
				GrabRequests.AddRemove(show.Id, show.Name);
				_note.Text = "Sent to MediaPortal - " + show.Name + " is removed within 20 seconds.";
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, "Could not hand it to MediaPortal: " + ex.Message, "MyTorrents", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
		}

		/// 2026 (1.3.38): the shows you took out of the Auto grabber, with "Put back".
		private void ShowRemoved()
		{
			List<string[]> removed = AutoAdd.RemovedShows();
			using (Form f = new Form())
			{
				f.Text = "Removed shows";
				f.StartPosition = FormStartPosition.CenterParent;
				f.ClientSize = new Size(380, 320);
				f.MinimizeBox = false;
				f.ShowInTaskbar = false;
				f.Font = Font;
				Label l = new Label { Text = removed.Count == 0 ? "You haven't removed any shows." : "Shows you took out of the Auto grabber - they aren't added again by themselves.", Location = new Point(10, 10), Size = new Size(360, 30) };
				// 1.3.52: a list like the others - sortable, right-click to look it up ("so it's consistent")
				List<string[]> sorted = removed.OrderBy(r => r[1], StringComparer.CurrentCultureIgnoreCase).ToList();
				ListView box = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = true, Location = new Point(10, 42), Size = new Size(360, 230), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
				box.Columns.Add("Show", 330);
				foreach (string[] r in sorted) box.Items.Add(new ListViewItem(r[1]) { Tag = r });
				new ListSorter(box, "Removed");
				LookUp.AddTo(box, i => LookUp.What.Show(((string[])i.Tag)[1], ((string[])i.Tag)[0]));
				Button back = new Button { Text = "Put back", Location = new Point(200, 284), Width = 80, Enabled = false, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
				Button close = new Button { Text = "Close", DialogResult = DialogResult.Cancel, Location = new Point(290, 284), Width = 80, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
				box.SelectedIndexChanged += (s, e) => back.Enabled = box.SelectedItems.Count > 0;
				back.Click += (s, e) =>
				{
					try
					{
						foreach (ListViewItem i in box.SelectedItems) GrabRequests.AddPutBack(((string[])i.Tag)[0], ((string[])i.Tag)[1]);
						MessageBox.Show(f, "Sent to MediaPortal - a running show comes back at its next check (within the hour).", "Put back", MessageBoxButtons.OK, MessageBoxIcon.Information);
						f.Close();
					}
					catch (Exception ex)
					{
						MessageBox.Show(f, "Could not hand it to MediaPortal: " + ex.Message, "MyTorrents", MessageBoxButtons.OK, MessageBoxIcon.Warning);
					}
				};
				f.Controls.AddRange(new Control[] { l, box, back, close });
				f.CancelButton = close;
				f.ShowDialog(this);
			}
		}

		/// The copies, smallest first - the one picked, or null.
		private TorrentMatch Pick(AutoGrabberStatus.Show show, List<TorrentMatch> copies)
		{
			using (Form f = new Form())
			{
				f.Text = "Get " + show.Name + " " + show.Next + " now - just this once";
				f.StartPosition = FormStartPosition.CenterParent;
				f.ClientSize = new Size(560, 300);
				f.MinimizeBox = f.MaximizeBox = false;
				f.ShowInTaskbar = false;
				f.Font = Font;
				Label l = new Label { Text = "Pick a copy (smallest first). It is downloaded whatever your size limit says - nothing in your settings changes.", Location = new Point(10, 10), Size = new Size(540, 30) };
				ListBox box = new ListBox { Location = new Point(10, 42), Size = new Size(540, 210), IntegralHeight = false };
				foreach (TorrentMatch m in copies.Take(30)) box.Items.Add(EpisodeCopies.Describe(m) + "   -   " + m.Title);
				box.SelectedIndex = 0;
				Button ok = new Button { Text = "Download", DialogResult = DialogResult.OK, Location = new Point(380, 264), Width = 80 };
				Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(470, 264), Width = 80 };
				box.DoubleClick += (s, e) => { f.DialogResult = DialogResult.OK; f.Close(); };
				f.Controls.AddRange(new Control[] { l, box, ok, cancel });
				f.AcceptButton = ok;
				f.CancelButton = cancel;
				return f.ShowDialog(this) == DialogResult.OK && box.SelectedIndex >= 0 ? copies[box.SelectedIndex] : null;
			}
		}
	}
}
