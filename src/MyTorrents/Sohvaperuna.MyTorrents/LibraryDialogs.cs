using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Sohvaperuna.MyTorrents
{
	/// <summary>2026 (1.3.46): MyTorrents Configuration's windows for "Collections you could make" and "Stereo-only movies".</summary>
	public static class LibraryDialogs
	{
		private static Form NewForm(IWin32Window owner, string title, int w, int h)
		{
			return new Form
			{
				Text = title,
				StartPosition = FormStartPosition.CenterParent,
				ClientSize = new Size(w, h),
				MinimizeBox = false,
				ShowInTaskbar = false,
				Font = new Font("Microsoft Sans Serif", 8.25f)
			};
		}

		/// 2026 (1.3.50): right-click "Open location" and double-click on a list of files.
		public static void AddOpen(ListView lv, Func<ListViewItem, string> pathOf) => AddOpen(lv, pathOf, null);

		/// movieOf: the film (title, year) for "Look it up on TMDb" (1.3.52), or null.
		public static void AddOpen(ListView lv, Func<ListViewItem, string> pathOf, Func<ListViewItem, Tuple<string, int>> movieOf)
		{
			ContextMenuStrip menu = new ContextMenuStrip();
			ToolStripMenuItem open = new ToolStripMenuItem("Open location");
			menu.Items.Add(open);
			if (movieOf != null)
			{
				ToolStripMenuItem tmdb = new ToolStripMenuItem("Look it up on TMDb");
				tmdb.Click += (s, e) => { var m = lv.SelectedItems.Count > 0 ? movieOf(lv.SelectedItems[0]) : null; if (m != null) LookUp.TmdbMovie(m.Item1, m.Item2); };
				menu.Items.Add(tmdb);
			}
			// one right-click menu per list (the look-ups are added to the same one)
			lv.ContextMenuStrip = menu;
			lv.MouseDown += (s, e) =>
			{
				if (e.Button != MouseButtons.Right) return;
				ListViewItem hit = lv.GetItemAt(e.X, e.Y);
				if (hit != null && !hit.Selected) { lv.SelectedItems.Clear(); hit.Selected = true; }
			};
			menu.Opening += (s, e) => { if (lv.SelectedItems.Count == 0) e.Cancel = true; };
			open.Click += (s, e) => OpenLocation(lv.SelectedItems.Count == 0 ? null : pathOf(lv.SelectedItems[0]));
			lv.MouseDoubleClick += (s, e) =>
			{
				ListViewItem i = lv.GetItemAt(e.X, e.Y);
				if (i == null) return;
				if (lv.CheckBoxes) i.Checked = !i.Checked; // a double-click also flips the tick - put it back
				OpenLocation(pathOf(i));
			};
		}

		public static void OpenLocation(string path)
		{
			if (string.IsNullOrEmpty(path)) return;
			try
			{
				if (System.IO.File.Exists(path)) System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\"");
				else
				{
					string dir = System.IO.Directory.Exists(path) ? path : System.IO.Path.GetDirectoryName(path);
					while (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir)) dir = System.IO.Path.GetDirectoryName(dir);
					if (!string.IsNullOrEmpty(dir)) System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\"");
				}
			}
			catch
			{
			}
		}

		public static void CollectionsWindow(IWin32Window owner)
		{
			List<Collections.Suggestion> list = Collections.Suggestions();
			using (Form f = NewForm(owner, "Collections you could make", 800, 440))
			{
				Label l = new Label
				{
					Text = list.Count == 0 ? "Nothing to suggest - every movie that belongs to a collection is already in one (or you said no)." :
						"From Moving Pictures' own information. Tick the ones to do, then \"Move them\" - the movies' folders are moved into your Collections folder (" + Collections.Root + "). Nothing else changes.",
					Location = new Point(10, 10), Size = new Size(780, 30), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
				};
				ListView lv = new ListView { View = View.Details, CheckBoxes = true, FullRowSelect = true, Location = new Point(10, 44), Size = new Size(780, 340), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
				lv.Columns.Add("Collection", 220);
				lv.Columns.Add("What", 120);
				lv.Columns.Add("Your movies", 420);
				foreach (Collections.Suggestion s in list)
					lv.Items.Add(new ListViewItem(new[] { s.Name, s.FolderExists ? "add to yours" : "new (" + s.Films.Count + " movies)", string.Join(", ", s.Films.Select(x => x.Display)) }) { Tag = s });
				new ListSorter(lv, "Collections").Apply(); // 1.3.60: click a heading to sort, like every other list
				AddOpen(lv, i => ((Collections.Suggestion)i.Tag).Films.Select(x => x.File).FirstOrDefault());
				LookUp.AddTo(lv, i => LookUp.What.Collection(((Collections.Suggestion)i.Tag).Name)); // 1.3.52
				Button move = new Button { Text = "Move them", Location = new Point(10, 396), AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
				Button no = new Button { Text = "Not these", Location = new Point(100, 396), AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
				Button missing = new Button { Text = "What's missing from it...", Location = new Point(190, 396), AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
				Button all = new Button { Text = "Tick all", Location = new Point(355, 396), Width = 80, Anchor = AnchorStyles.Left | AnchorStyles.Bottom }; // 2026 (1.3.48)
				all.Click += (s, e) =>
				{
					bool tick = all.Text == "Tick all";
					foreach (ListViewItem i in lv.Items) i.Checked = tick;
				};
				lv.ItemChecked += (s, e) => all.Text = lv.Items.Count > 0 && lv.CheckedItems.Count == lv.Items.Count ? "Untick all" : "Tick all";
				Label note = new Label { Location = new Point(445, 401), AutoSize = true, ForeColor = Color.DimGray, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
				Button close = new Button { Text = "Close", DialogResult = DialogResult.Cancel, Location = new Point(710, 396), Width = 80, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
				move.Click += (s, e) =>
				{
					List<Collections.Suggestion> pick = lv.CheckedItems.Cast<ListViewItem>().Select(i => (Collections.Suggestion)i.Tag).ToList();
					if (pick.Count == 0) return;
					move.Enabled = no.Enabled = missing.Enabled = false;
					note.Text = "Moving " + pick.Sum(x => x.Films.Count) + " movies - on a NAS this can take a while...";
					BackgroundWorker w = new BackgroundWorker();
					List<string> problems = new List<string>();
					w.DoWork += (s2, e2) => { foreach (Collections.Suggestion x in pick) { string r = Collections.Make(x); if (r.Length > 0) problems.Add(r); } };
					w.RunWorkerCompleted += (s2, e2) =>
					{
						MessageBox.Show(f, problems.Count == 0 ? "Done. Moving Pictures finds them in their new place at its next scan." : string.Join("\r\n", problems), "Collections",
							MessageBoxButtons.OK, problems.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
						f.Close();
					};
					w.RunWorkerAsync();
				};
				no.Click += (s, e) =>
				{
					foreach (ListViewItem i in lv.CheckedItems.Cast<ListViewItem>().ToList())
					{
						Collections.Ignore((Collections.Suggestion)i.Tag);
						lv.Items.Remove(i);
					}
				};
				missing.Click += (s, e) =>
				{
					ListViewItem sel = lv.SelectedItems.Count > 0 ? lv.SelectedItems[0] : lv.CheckedItems.Count > 0 ? lv.CheckedItems[0] : null;
					if (sel == null)
					{
						note.Text = "Pick a collection first.";
						return;
					}
					Collections.Suggestion x = (Collections.Suggestion)sel.Tag;
					Cursor.Current = Cursors.WaitCursor;
					List<Collections.Film> gone = Collections.Missing(x);
					Cursor.Current = Cursors.Default;
					if (gone.Count == 0)
					{
						MessageBox.Show(f, "Nothing missing from " + x.Name + " - or TMDb couldn't be asked (MP-TVSeries needs to be installed).", "Collections");
						return;
					}
					if (MessageBox.Show(f, x.Name + " - you don't have:\r\n\r\n" + string.Join("\r\n", gone.Select(g => "  " + g.Display)) + "\r\n\r\nAdd them to Wanted movies?", "Collections",
						MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
					{
						int n = gone.Count(g => WantedFilms.Add(g.Title, g.Year > 0 ? g.Year.ToString() : ""));
						note.Text = n + " added to Wanted movies.";
					}
				};
				f.Controls.AddRange(new Control[] { l, lv, move, no, missing, all, note, close });
				f.Shown += (s9, e9) => all.Text = lv.Items.Count > 0 && lv.CheckedItems.Count == lv.Items.Count ? "Untick all" : "Tick all";
				f.CancelButton = close;
				f.ShowDialog(owner);
			}
		}

		/// 2026 (1.3.49): movies whose names don't match the movie naming choice - tick and rename.
		public static void RenameWindow(IWin32Window owner)
		{
			if (System.Diagnostics.Process.GetProcessesByName("MediaPortal").Length > 0)
			{
				MessageBox.Show(owner, "Close MediaPortal first - while it runs it has your movies open, and Moving Pictures needs to be told the new names.", "Rename my movies", MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}
			Cursor.Current = Cursors.WaitCursor;
			List<MovieRenamer.Item> list = MovieRenamer.Proposals();
			Cursor.Current = Cursors.Default;
			using (Form f = NewForm(owner, "Rename my movies to match", 900, 480))
			{
				Label l = new Label
				{
					Text = list.Count == 0 ? "All your movies already match your naming choice." :
						list.Count + " movies don't match your naming choice (When finished tab). Tick the ones to rename - their .nfo, subtitles and artwork with the same name go too, and Moving Pictures keeps them (watched, artwork). Nothing is renamed until you press Rename.",
					Location = new Point(10, 10), Size = new Size(880, 30), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
				};
				ListView lv = new ListView { View = View.Details, CheckBoxes = true, FullRowSelect = true, Location = new Point(10, 44), Size = new Size(880, 384), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
				lv.Columns.Add("Movie", 200);
				lv.Columns.Add("Now", 330);
				lv.Columns.Add("Becomes", 330);
				foreach (MovieRenamer.Item i in list)
				{
					string now = (i.FolderToo ? System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(i.OldFile)) + "\\" : "") + System.IO.Path.GetFileName(i.OldFile);
					string becomes = (i.FolderToo ? System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(i.NewFile)) + "\\" : "") + System.IO.Path.GetFileName(i.NewFile);
					lv.Items.Add(new ListViewItem(new[] { i.Display, now, becomes }) { Tag = i, ToolTipText = i.OldFile + "\r\n-> " + i.NewFile });
				}
				lv.ShowItemToolTips = true;
				new ListSorter(lv, "RenameMovies").Apply(); // 1.3.60
				AddOpen(lv, i => System.IO.File.Exists(((MovieRenamer.Item)i.Tag).NewFile) ? ((MovieRenamer.Item)i.Tag).NewFile : ((MovieRenamer.Item)i.Tag).OldFile,
					i => Tuple.Create(((MovieRenamer.Item)i.Tag).Title, ((MovieRenamer.Item)i.Tag).Year));
				Button go = new Button { Text = "Rename", Location = new Point(10, 440), Width = 80, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
				Button all = new Button { Text = "Tick all", Location = new Point(100, 440), Width = 80, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
				all.Click += (s, e) =>
				{
					bool tick = all.Text == "Tick all";
					foreach (ListViewItem i in lv.Items) i.Checked = tick;
				};
				lv.ItemChecked += (s, e) => all.Text = lv.Items.Count > 0 && lv.CheckedItems.Count == lv.Items.Count ? "Untick all" : "Tick all";
				Label note = new Label { Location = new Point(190, 445), AutoSize = true, ForeColor = Color.DimGray, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
				Button close = new Button { Text = "Close", DialogResult = DialogResult.Cancel, Location = new Point(810, 440), Width = 80, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
				go.Click += (s, e) =>
				{
					List<ListViewItem> pick = lv.CheckedItems.Cast<ListViewItem>().ToList();
					if (pick.Count == 0) return;
					go.Enabled = all.Enabled = false;
					note.Text = "Renaming " + pick.Count + " - on a NAS this can take a moment...";
					BackgroundWorker w = new BackgroundWorker();
					List<string> problems = new List<string>();
					List<ListViewItem> done = new List<ListViewItem>();
					w.DoWork += (s2, e2) =>
					{
						foreach (ListViewItem i in pick)
						{
							string r = MovieRenamer.Rename((MovieRenamer.Item)i.Tag);
							if (r.Length > 0) problems.Add(r);
							else done.Add(i);
						}
					};
					w.RunWorkerCompleted += (s2, e2) =>
					{
						foreach (ListViewItem i in done) lv.Items.Remove(i);
						go.Enabled = all.Enabled = true;
						note.Text = done.Count + " renamed" + (problems.Count > 0 ? ", " + problems.Count + " not" : "") + ".";
						if (problems.Count > 0) MessageBox.Show(f, string.Join("\r\n", problems.Take(20)), "Rename my movies", MessageBoxButtons.OK, MessageBoxIcon.Warning);
					};
					w.RunWorkerAsync();
				};
				f.Controls.AddRange(new Control[] { l, lv, go, all, note, close });
				f.Shown += (s9, e9) => all.Text = "Tick all";
				f.CancelButton = close;
				f.ShowDialog(owner);
			}
		}

		/// 2026 (1.3.65): Movies to improve - poor copies and stereo-only movies in one list (used to be two windows).
		/// Each movie looks for the best it can: poor + stereo = a better copy, 5.1 if there is one; poor = a better
		/// copy; stereo = a 5.1 copy. Sorts by any heading, the year too.
		public static void ImproveWindow(IWin32Window owner)
		{
			List<Improve.Item> list = Improve.List();
			using (Form f = NewForm(owner, "Movies to improve", 940, 480))
			{
				Label l = new Label
				{
					Text = list.Count + " movies could be better: poor copies (too small, or a lower picture than " + Films.Quality + ") and stereo-only movies, from Moving Pictures. " +
						"Tick them and press Look for them - each one looks for what it needs (the last column). Click a heading to sort, by year too.",
					Location = new Point(10, 10), Size = new Size(920, 30), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
				};
				ListView lv = new ListView { View = View.Details, CheckBoxes = true, FullRowSelect = true, Location = new Point(10, 44), Size = new Size(920, 350), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
				lv.Columns.Add("Movie", 250);
				lv.Columns.Add("Year", 50);
				lv.Columns.Add("Picture", 170);
				lv.Columns.Add("Sound", 70);
				lv.Columns.Add("Size", 70);
				lv.Columns.Add("Looks for", 220);
				lv.Columns.Add("File", 250);
				foreach (Improve.Item m in list)
					lv.Items.Add(new ListViewItem(new[] { m.Title, m.Year > 0 ? m.Year.ToString() : "", m.PictureText + (m.Poor.Length == 0 && m.Resolution.Length > 0 ? " (" + m.Resolution + ")" : ""), m.SoundText,
						m.Size > 0 ? (m.Size / 1024 / 1024) + " MB" : "", m.LooksFor, m.File }) { Tag = m });
				new ListSorter(lv, "Improve");
				AddOpen(lv, i => ((Improve.Item)i.Tag).File, i => Tuple.Create(((Improve.Item)i.Tag).Title, ((Improve.Item)i.Tag).Year));
				CheckBox del = new CheckBox { Text = "Delete the old copy once the new one is safely in place (untick to keep both)", AutoSize = true, Checked = SoundUpgrades.DeleteOld, Location = new Point(10, 402), Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
				CheckBox auto = new CheckBox { Text = "Look for " + SoundUpgrades.AutoPerDay + " of the stereo ones by themselves each day", AutoSize = true, Checked = SoundUpgrades.Auto, Location = new Point(10, 424), Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
				Button look = new Button { Text = "Look for them", Location = new Point(10, 448), AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
				Button no = new Button { Text = "Not these", Location = new Point(120, 448), AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
				Button all = new Button { Text = "Tick all", Location = new Point(210, 448), Width = 80, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
				Label note = new Label { Location = new Point(300, 453), AutoSize = true, ForeColor = Color.DimGray, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
				Button close = new Button { Text = "Close", DialogResult = DialogResult.Cancel, Location = new Point(850, 448), Width = 80, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
				// 2026 (1.3.65): the ones you said "Not these" to - look them over, put them back
				Button hidden = new Button { Text = "Hidden (" + Improve.Hidden().Count + ")...", AutoSize = true, Location = new Point(730, 448), Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
				hidden.Click += (s, e) =>
				{
					if (HiddenWindow(f) == 0) return;
					// something put back - fill the list again
					lv.Items.Clear();
					foreach (Improve.Item m in Improve.List())
						lv.Items.Add(new ListViewItem(new[] { m.Title, m.Year > 0 ? m.Year.ToString() : "", m.PictureText + (m.Poor.Length == 0 && m.Resolution.Length > 0 ? " (" + m.Resolution + ")" : ""), m.SoundText,
							m.Size > 0 ? (m.Size / 1024 / 1024) + " MB" : "", m.LooksFor, m.File }) { Tag = m });
					hidden.Text = "Hidden (" + Improve.Hidden().Count + ")...";
				};
				del.CheckedChanged += (s, e) => Configuration.SetExtra("Films.UpgradeDeleteOld", del.Checked ? "true" : "false");
				auto.CheckedChanged += (s, e) => Configuration.SetExtra("Films.UpgradeAuto", auto.Checked ? "true" : "false");
				all.Click += (s, e) =>
				{
					bool tick = all.Text == "Tick all";
					foreach (ListViewItem i in lv.Items) i.Checked = tick;
				};
				lv.ItemChecked += (s, e) => all.Text = lv.Items.Count > 0 && lv.CheckedItems.Count == lv.Items.Count ? "Untick all" : "Tick all";
				look.Click += (s, e) =>
				{
					int n = 0;
					foreach (ListViewItem i in lv.CheckedItems.Cast<ListViewItem>().ToList())
					{
						if (Improve.Look((Improve.Item)i.Tag)) n++;
						lv.Items.Remove(i);
					}
					note.Text = n + " added to Wanted movies.";
				};
				no.Click += (s, e) =>
				{
					foreach (ListViewItem i in lv.CheckedItems.Cast<ListViewItem>().ToList())
					{
						Improve.Ignore((Improve.Item)i.Tag);
						lv.Items.Remove(i);
					}
				};
				f.Controls.AddRange(new Control[] { l, lv, del, auto, look, no, all, note, hidden, close });
				f.CancelButton = close;
				f.ShowDialog(owner);
			}
		}

		/// 2026 (1.3.65): movies hidden from Movies to improve - tick and Put back. Returns how many were put back.
		private static int HiddenWindow(IWin32Window owner)
		{
			int back = 0;
			using (Form f = NewForm(owner, "Hidden from Movies to improve", 700, 420))
			{
				Label l = new Label { Text = "Movies you said \"Not these\" to. Tick any to put back on Movies to improve (they come back if they still need it).", Location = new Point(10, 10), Size = new Size(680, 30), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
				ListView lv = new ListView { View = View.Details, CheckBoxes = true, FullRowSelect = true, Location = new Point(10, 44), Size = new Size(680, 320), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
				lv.Columns.Add("Movie", 260);
				lv.Columns.Add("Year", 50);
				lv.Columns.Add("Size", 70);
				lv.Columns.Add("File", 280);
				foreach (Improve.Item m in Improve.Hidden())
					lv.Items.Add(new ListViewItem(new[] { m.Title, m.Year > 0 ? m.Year.ToString() : "", m.Size > 0 ? (m.Size / 1024 / 1024) + " MB" : "", m.File }) { Tag = m });
				new ListSorter(lv, "ImproveHidden");
				AddOpen(lv, i => ((Improve.Item)i.Tag).File, i => Tuple.Create(((Improve.Item)i.Tag).Title, ((Improve.Item)i.Tag).Year));
				Button put = new Button { Text = "Put back", Location = new Point(10, 376), AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
				Button all = new Button { Text = "Tick all", Location = new Point(100, 376), Width = 80, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
				Button close = new Button { Text = "Close", DialogResult = DialogResult.Cancel, Location = new Point(610, 376), Width = 80, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
				all.Click += (s, e) =>
				{
					bool tick = all.Text == "Tick all";
					foreach (ListViewItem i in lv.Items) i.Checked = tick;
				};
				lv.ItemChecked += (s, e) => all.Text = lv.Items.Count > 0 && lv.CheckedItems.Count == lv.Items.Count ? "Untick all" : "Tick all";
				put.Click += (s, e) =>
				{
					foreach (ListViewItem i in lv.CheckedItems.Cast<ListViewItem>().ToList())
					{
						Improve.PutBack(((Improve.Item)i.Tag).File);
						lv.Items.Remove(i);
						back++;
					}
				};
				f.Controls.AddRange(new Control[] { l, lv, put, all, close });
				f.CancelButton = close;
				f.ShowDialog(owner);
			}
			return back;
		}
	}
}
