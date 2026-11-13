using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Sohvaperuna.MyTorrents
{
	/// <summary>
	/// 2026 (1.3.43): "Movies" tab - everything for movies in one place: quality and size, how often wanted movies are
	/// looked for, your Trakt list (MCM), and the Wanted movies list itself (add / look now / remove).
	/// </summary>
	public class MoviesPage : TabPage
	{
		public static readonly int[] LookChoices = { 1, 3, 6, 12, 24 };

		private readonly ComboBox _quality = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
		private readonly NumericUpDown _size = new NumericUpDown { Minimum = 0, Maximum = 100000, Increment = 100, Width = 70 };
		private readonly ComboBox _sound = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 }; // 2026 (1.3.45)
		private readonly TextBox _favs = new TextBox { Width = 230 }; // 1.3.48: shorter, the hint sits beside it (scrolls when the list is long)
		private readonly ComboBox _look = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
		private readonly ComboBox _minSize = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, DropDownWidth = 300 }; // 2026 (1.3.47)
		private readonly CheckBox _traktOn = new CheckBox { AutoSize = true };
		private readonly TextBox _traktList = new TextBox { Width = 90 };
		private readonly CheckBox _traktRemove = new CheckBox { AutoSize = true, Text = "When I remove a movie from Wanted movies, take it off my Trakt list too" }; // 2026 (1.3.45)
		private readonly GroupBox _wanted = new GroupBox { Text = "Wanted movies" };
		// 2026 (1.3.44): where new movies go (no more Media Center Master)
		private readonly RadioButton _iFile = new RadioButton { AutoSize = true, Text = "Leave them in my new-movies folder - I file them myself" };
		private readonly RadioButton _fileForMe = new RadioButton { AutoSize = true, Text = "File them for me - into Movies, or Collections\\[collection]\\ when the movie is part of one" };
		private readonly RadioButton _askFirst = new RadioButton { AutoSize = true, Text = "File them for me, but show me first (they wait in the new-movies folder for your OK)" }; // 1.3.45
		private readonly Button _waiting = new Button { Text = "Waiting for your OK...", AutoSize = true };
		private readonly ComboBox _moviesFolder = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 360 };
		private readonly ComboBox _collectionsFolder = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 360 };
		private readonly CheckBox _nfo = new CheckBox { AutoSize = true, Text = "Write a small .nfo with the movie's IMDb id next to it (Moving Pictures then picks the exact movie)" };
		private readonly ListView _list = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = true };
		private readonly ListSorter _wantedSorter; // 1.3.60
		private readonly TextBox _title = new TextBox { Width = 230 };
		private readonly TextBox _year = new TextBox { Width = 50, MaxLength = 4 };
		private readonly Button _add = new Button { Text = "Add", AutoSize = true };
		private readonly Button _lookNow = new Button { Text = "Look now", AutoSize = true, Enabled = false };
		private readonly Button _remove = new Button { Text = "Remove", AutoSize = true, Enabled = false };
		private readonly Button _reread = new Button { Text = "Read my movies again", AutoSize = true };
		private readonly Button _export = new Button { Text = "Save what's not found...", AutoSize = true }; // 2026 (1.3.44)
		private readonly Label _note = new Label { AutoSize = true, ForeColor = Color.DimGray };

		public MoviesPage()
		{
			Text = "Movies";
			Padding = new Padding(3);
			UseVisualStyleBackColor = true;
			Font = new Font("Microsoft Sans Serif", 8.25f);

			GroupBox box = new GroupBox { Text = "Movies", Location = new Point(8, 8), Size = new Size(548, 182) };
			Controls.Add(box);
			Row(box, "Movie quality", 20, _quality);
			foreach (string q in SeriesItem.QualityChoices) _quality.Items.Add(q);
			_quality.SelectedItem = SeriesItem.QualityChoices.Contains(Films.Quality) ? Films.Quality : "1080p";
			box.Controls.Add(new Label { Text = "Biggest movie", Location = new Point(260, 23), AutoSize = true });
			_size.Location = new Point(340, 20);
			_size.Value = Math.Min(_size.Maximum, Films.MaxSizeMB);
			box.Controls.Add(_size);
			box.Controls.Add(new Label { Text = "MB  (0 = no limit)", Location = new Point(_size.Right + 6, 23), AutoSize = true });
			box.Controls.Add(new Label { Text = "Smallest", Location = new Point(285, 51), AutoSize = true });
			_minSize.Location = new Point(340, 48);
			foreach (long m in Films.MinSizeChoices) _minSize.Items.Add(Films.MinSizeText(m));
			_minSize.SelectedIndex = Math.Max(0, Array.IndexOf(Films.MinSizeChoices, Films.MinSizeSetting));
			box.Controls.Add(_minSize);
			Row(box, "Look for wanted movies", 48, _look);
			foreach (int h in LookChoices) _look.Items.Add("every " + h + (h == 1 ? " hour" : " hours"));
			_look.SelectedIndex = Array.IndexOf(LookChoices, WantedFilms.LookEveryHours) < 0 ? 1 : Array.IndexOf(LookChoices, WantedFilms.LookEveryHours);
			_traktOn.Text = "Get what's on my Trakt list";
			_traktOn.Location = new Point(12, 76);
			_traktOn.Checked = Configuration.GetExtra("Trakt.ListOn", "true") != "false";
			box.Controls.Add(_traktOn);
			_traktList.Location = new Point(190, 74);
			_traktList.Text = TraktList.ListName;
			box.Controls.Add(_traktList);
			box.Controls.Add(new Label { Text = "(taken off the list once it's in your movies)", Location = new Point(_traktList.Right + 6, 77), AutoSize = true, ForeColor = Color.DimGray });
			_traktRemove.Location = new Point(30, 100);
			_traktRemove.Checked = TraktList.RemoveDropped;
			box.Controls.Add(_traktRemove);
			Row(box, "Sound", 126, _sound);
			foreach (string c in Sound.Choices) _sound.Items.Add(Sound.Text(c));
			_sound.SelectedIndex = Math.Max(0, Array.IndexOf(Sound.Choices, Sound.Movies));
			Row(box, "Favourites", 154, _favs);
			_favs.Text = string.Join(", ", Films.Favourites);
			box.Controls.Add(new Label { Text = "release groups / sites, with commas", Location = new Point(_favs.Right + 8, 157), AutoSize = true, ForeColor = Color.DimGray });
			ToolTip favTip = new ToolTip();
			favTip.SetToolTip(_favs, _favs.Text.Length > 0 ? _favs.Text : "Part of the copy's or site's name, e.g. YTS");
			_favs.TextChanged += (s, e) => favTip.SetToolTip(_favs, _favs.Text);

			// 2026 (1.3.44): where new movies go
			GroupBox where = new GroupBox { Text = "New movies", Location = new Point(8, 196), Size = new Size(548, 204) };
			Controls.Add(where);
			_iFile.Location = new Point(12, 20);
			_fileForMe.Location = new Point(12, 42);
			_askFirst.Location = new Point(12, 64);
			where.Controls.Add(_iFile);
			where.Controls.Add(_fileForMe);
			where.Controls.Add(_askFirst);
			_iFile.Checked = MovieFiling.Mode != "library" && MovieFiling.Mode != "ask";
			_fileForMe.Checked = MovieFiling.Mode == "library";
			_askFirst.Checked = MovieFiling.Mode == "ask";
			List<string> mpFolders = new List<string>();
			try
			{
				mpFolders = LibraryFolders.Movies().Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
			}
			catch
			{
			}
			where.Controls.Add(new Label { Text = "Movies folder", Location = new Point(30, 93), AutoSize = true });
			_moviesFolder.Location = new Point(150, 90);
			_moviesFolder.Items.AddRange(mpFolders.Cast<object>().ToArray());
			_moviesFolder.Text = MovieFiling.MoviesFolder;
			where.Controls.Add(_moviesFolder);
			Button moviesBrowse = Browse(_moviesFolder, 514, 89); // 2026 (1.3.57): Browse buttons (laptop test, 7 Nov)
			where.Controls.Add(moviesBrowse);
			where.Controls.Add(new Label { Text = "Collections folder", Location = new Point(30, 119), AutoSize = true });
			_collectionsFolder.Location = new Point(150, 116);
			_collectionsFolder.Items.AddRange(mpFolders.Cast<object>().ToArray());
			_collectionsFolder.Text = MovieFiling.CollectionsFolder;
			where.Controls.Add(_collectionsFolder);
			Button collectionsBrowse = Browse(_collectionsFolder, 514, 115);
			where.Controls.Add(collectionsBrowse);
			_nfo.Location = new Point(12, 148);
			_nfo.Checked = MovieFiling.WriteNfo;
			where.Controls.Add(_nfo);
			_waiting.Location = new Point(30, 172);
			_waiting.Click += (s, e) => ShowWaiting();
			where.Controls.Add(_waiting);
			EventHandler en = (s, e) =>
			{
				_moviesFolder.Enabled = _collectionsFolder.Enabled = moviesBrowse.Enabled = collectionsBrowse.Enabled = _fileForMe.Checked || _askFirst.Checked;
				_waiting.Text = "Waiting for your OK (" + MovieFiling.Waiting().Count + ")...";
			};
			_fileForMe.CheckedChanged += en;
			_askFirst.CheckedChanged += en;
			en(null, null);

			// 2026 (1.3.46): ideas for the movies you already have
			GroupBox lib = new GroupBox { Text = "Your movies", Location = new Point(8, 406), Size = new Size(548, 82) };
			Controls.Add(lib);
			Button coll = new Button { Text = "Collections you could make...", AutoSize = true, Location = new Point(12, 18) };
			coll.Click += (s, e) => { Cursor.Current = Cursors.WaitCursor; LibraryDialogs.CollectionsWindow(this); };
			lib.Controls.Add(coll);
			Button stereo = new Button { Text = "Movies to improve (better copy / 5.1)...", AutoSize = true }; // 2026 (1.3.65): poor copies + stereo-only in one
			lib.Controls.Add(stereo);
			stereo.Location = new Point(coll.Left + coll.PreferredSize.Width + 10, 18);
			stereo.Click += (s, e) => { Cursor.Current = Cursors.WaitCursor; LibraryDialogs.ImproveWindow(this); };
			Button names = new Button { Text = "Rename to match my naming...", AutoSize = true }; // 2026 (1.3.49)
			lib.Controls.Add(names);
			names.Location = new Point(stereo.Left + stereo.PreferredSize.Width + 10, 18);
			names.Click += (s, e) => LibraryDialogs.RenameWindow(this);

			// the Wanted movies list
			_wanted.Location = new Point(8, 494);
			_wanted.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
			Controls.Add(_wanted);
			_list.Columns.Add("Movie", 260);
			_list.Columns.Add("State", 260);
			// 1.3.60: click a heading to sort (and Year / Added to sort by)
			_list.Columns.Add("Year", 50);
			_list.Columns.Add("Added", 80);
			_wantedSorter = new ListSorter(_list, "Wanted")
				.Key(0, i => MediaNames.Key((i.Tag as WantedFilms.Film)?.Title ?? ""))
				.Key(2, i => { int y; int.TryParse((i.Tag as WantedFilms.Film)?.Year, out y); return y; })
				.Key(3, i => (i.Tag as WantedFilms.Film)?.Added ?? DateTime.MinValue);
			// 2026 (1.3.52): right-click - look it up on TMDb
			ContextMenuStrip wmenu = new ContextMenuStrip();
			wmenu.Items.Add("Look it up on TMDb", null, (s, e) =>
			{
				WantedFilms.Film f = _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as WantedFilms.Film : null;
				int y;
				if (f != null) LookUp.TmdbMovie(f.Title, int.TryParse(f.Year, out y) ? y : 0);
			});
			_list.ContextMenuStrip = wmenu;
			_list.MouseDown += (s, e) =>
			{
				if (e.Button != MouseButtons.Right) return;
				ListViewItem hit = _list.GetItemAt(e.X, e.Y);
				if (hit != null && !hit.Selected) { _list.SelectedItems.Clear(); hit.Selected = true; }
			};
			_list.Location = new Point(10, 20);
			_list.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
			_list.SelectedIndexChanged += (s, e) => _lookNow.Enabled = _remove.Enabled = _list.SelectedItems.Count > 0;
			_list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) RemoveSelected(); };
			_wanted.Controls.Add(_list);
			foreach (Control c in new Control[] { _title, _year, _add, _lookNow, _remove, _reread, _export, _note }) c.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
			_wanted.Controls.Add(new Label { Text = "Title", AutoSize = true, Name = "titleLabel", Anchor = AnchorStyles.Bottom | AnchorStyles.Left });
			_wanted.Controls.Add(new Label { Text = "Year", AutoSize = true, Name = "yearLabel", Anchor = AnchorStyles.Bottom | AnchorStyles.Left });
			_wanted.Controls.AddRange(new Control[] { _title, _year, _add, _lookNow, _remove, _reread, _export, _note });
			_export.Click += (s, e) => ExportList();
			_add.Click += (s, e) => AddMovie();
			_title.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { AddMovie(); e.SuppressKeyPress = true; } };
			_year.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { AddMovie(); e.SuppressKeyPress = true; } };
			_lookNow.Click += (s, e) =>
			{
				foreach (ListViewItem i in _list.SelectedItems)
				{
					WantedFilms.Film x = (WantedFilms.Film)i.Tag;
					WantedFilms.LookAgain(x.Title, x.Year);
				}
				Fill();
				_note.Text = "MediaPortal looks for them within about 5 minutes (while it runs).";
			};
			_remove.Click += (s, e) => RemoveSelected();
			_reread.Click += (s, e) =>
			{
				try
				{
					GrabRequests.AddRereadMovies();
					_note.Text = "Sent to MediaPortal - it reads your movies folders again in the background.";
				}
				catch (Exception ex)
				{
					MessageBox.Show(this, "Could not hand it to MediaPortal: " + ex.Message, "MyTorrents", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				}
			};

			AutoScroll = true;
			Resize += (s, e) => Arrange();
			VisibleChanged += (s, e) => { if (Visible) Fill(); };
		}

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);
			Arrange();
		}

		private void Arrange()
		{
			int w = Math.Max(548, ClientSize.Width - 16);
			_wanted.Size = new Size(w, Math.Max(220, ClientSize.Height - _wanted.Top - 8));
			int bottom = _wanted.ClientSize.Height;
			_list.Size = new Size(_wanted.ClientSize.Width - 20, Math.Max(80, bottom - 20 - 70));
			int y1 = _list.Bottom + 8, y2 = y1 + 30;
			Control tl = _wanted.Controls["titleLabel"], yl = _wanted.Controls["yearLabel"];
			tl.Location = new Point(10, y1 + 3);
			_title.Location = new Point(45, y1);
			yl.Location = new Point(_title.Right + 10, y1 + 3);
			_year.Location = new Point(yl.Right + 6, y1);
			_add.Location = new Point(_year.Right + 10, y1 - 1);
			_lookNow.Location = new Point(10, y2);
			_remove.Location = new Point(_lookNow.Right + 6, y2);
			_reread.Location = new Point(_remove.Right + 20, y2);
			_export.Location = new Point(_reread.Right + 6, y2);
			_note.Location = new Point(_export.Right + 10, y2 + 5);
			if (_list.Columns.Count > 1) _list.Columns[1].Width = Math.Max(150, _list.ClientSize.Width - _list.Columns[0].Width - 4);
		}

		private static void Row(Control parent, string text, int y, Control box)
		{
			parent.Controls.Add(new Label { Text = text, Location = new Point(12, y + 3), AutoSize = true });
			box.Location = new Point(150, y);
			parent.Controls.Add(box);
		}

		private void Fill()
		{
			HashSet<string> keep = new HashSet<string>(_list.SelectedItems.Cast<ListViewItem>().Select(i => i.Name));
			HashSet<string> cantFind = new HashSet<string>(CantFind.All().Where(x => x.IsMovie).Select(x => x.Name + "|" + x.Year));
			_list.BeginUpdate();
			_list.Items.Clear();
			foreach (WantedFilms.Film x in WantedFilms.Read())
			{
				string state = WantedFilms.StateText(x) + (cantFind.Contains(x.Title + "|" + x.Year) ? "  - on the Can't find list" : "");
				ListViewItem i = new ListViewItem(new[] { x.Display, state, x.Year, x.Added.ToString("d MMM yyyy") }) { Tag = x, Name = x.Title + "|" + x.Year };
				if (cantFind.Contains(x.Title + "|" + x.Year)) i.ForeColor = Color.DarkOrange;
				i.Selected = keep.Contains(i.Name);
				_list.Items.Add(i);
			}
			_list.EndUpdate();
			_wantedSorter.Apply();
			_wanted.Text = "Wanted movies (" + _list.Items.Count + ")";
		}

		/// 2026 (1.3.44): the wanted movies (and the Can't find episodes) as a text file - to look for them yourself.
		private void ExportList()
		{
			// only what hasn't been found - not the ones downloading or already in your movies
			List<WantedFilms.Film> films = WantedFilms.Read().Where(f => f.State == "wanted").ToList();
			List<CantFind.Item> eps = CantFind.AllNotFound();
			System.Text.StringBuilder sb = new System.Text.StringBuilder();
			sb.AppendLine("MyTorrents - not found yet, " + DateTime.Now.ToString("d MMM yyyy h:mm tt"));
			sb.AppendLine();
			sb.AppendLine("MOVIES (" + films.Count + ")");
			foreach (WantedFilms.Film f in films.OrderBy(f => f.Title)) sb.AppendLine("  " + f.Display + "   - " + WantedFilms.StateText(f));
			sb.AppendLine();
			sb.AppendLine("EPISODES (" + eps.Count + ")");
			foreach (CantFind.Item x in eps) sb.AppendLine("  " + x.What + "   - looking for " + x.Since + ", " + x.Why);
			using (SaveFileDialog d = new SaveFileDialog { Title = "Save what hasn't been found yet", FileName = "MyTorrents not found yet " + DateTime.Now.ToString("yyyy-MM-dd") + ".txt", Filter = "Text file (*.txt)|*.txt",
				InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) })
			{
				if (d.ShowDialog(this) != DialogResult.OK) return;
				try
				{
					System.IO.File.WriteAllText(d.FileName, sb.ToString());
					try
					{
						Clipboard.SetText(sb.ToString());
					}
					catch
					{
					}
					_note.Text = "Saved (and copied, ready to paste): " + System.IO.Path.GetFileName(d.FileName);
					System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(d.FileName) { UseShellExecute = true });
				}
				catch (Exception ex)
				{
					MessageBox.Show(this, "Could not save it: " + ex.Message, "MyTorrents", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				}
			}
		}

		/// 2026 (1.3.45): new movies waiting for your OK - tick the ones to file, or leave them where they are.
		private void ShowWaiting()
		{
			List<MovieFiling.Pending> list = MovieFiling.Waiting();
			using (Form f = new Form())
			{
				f.Text = "New movies waiting for your OK";
				f.StartPosition = FormStartPosition.CenterParent;
				f.ClientSize = new Size(720, 360);
				f.MinimizeBox = false;
				f.ShowInTaskbar = false;
				f.Font = Font;
				Label l = new Label { Text = list.Count == 0 ? "Nothing waiting." : "Tick the ones to file where it says, then OK. \"Leave them\" keeps the ticked ones in the new-movies folder.", Location = new Point(10, 10), Size = new Size(700, 30) };
				ListView lv = new ListView { View = View.Details, CheckBoxes = true, FullRowSelect = true, Location = new Point(10, 42), Size = new Size(700, 270), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
				lv.Columns.Add("Movie", 260);
				lv.Columns.Add("Goes to", 420);
				foreach (MovieFiling.Pending p in list) lv.Items.Add(new ListViewItem(new[] { p.Title, p.To }) { Tag = p, Checked = true });
				new ListSorter(lv, "FileMovies").Apply(); // 1.3.60
				LibraryDialogs.AddOpen(lv, i => (i.Tag as MovieFiling.Pending)?.From); // 1.3.50
				LookUp.AddTo(lv, i => LookUp.What.FromName((i.Tag as MovieFiling.Pending)?.Title ?? "")); // 1.3.52
				Button ok = new Button { Text = "OK - file them", Location = new Point(10, 322), AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
				Button leave = new Button { Text = "Leave them", Location = new Point(130, 322), AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
				Button all = new Button { Text = "Tick all", Location = new Point(230, 322), Width = 80, Anchor = AnchorStyles.Left | AnchorStyles.Bottom }; // 2026 (1.3.48)
				all.Click += (s2, e2) =>
				{
					bool tick = all.Text == "Tick all";
					foreach (ListViewItem i in lv.Items) i.Checked = tick;
				};
				lv.ItemChecked += (s2, e2) => all.Text = lv.Items.Count > 0 && lv.CheckedItems.Count == lv.Items.Count ? "Untick all" : "Tick all";
				Button close = new Button { Text = "Close", DialogResult = DialogResult.Cancel, Location = new Point(630, 322), Width = 80, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
				ok.Click += (s, e) =>
				{
					List<string> problems = new List<string>();
					foreach (ListViewItem i in lv.CheckedItems)
					{
						string r = MovieFiling.Approve((MovieFiling.Pending)i.Tag);
						if (r.Length > 0) problems.Add(r);
					}
					MessageBox.Show(f, problems.Count == 0 ? "Filed. Moving Pictures finds them at its next scan." : string.Join("\r\n", problems), "Movies", MessageBoxButtons.OK, problems.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
					f.Close();
				};
				leave.Click += (s, e) =>
				{
					foreach (ListViewItem i in lv.CheckedItems) MovieFiling.Forget((MovieFiling.Pending)i.Tag);
					f.Close();
				};
				f.Controls.AddRange(new Control[] { l, lv, ok, leave, all, close });
				f.Shown += (s9, e9) => all.Text = lv.Items.Count > 0 && lv.CheckedItems.Count == lv.Items.Count ? "Untick all" : "Tick all";
				f.CancelButton = close;
				f.ShowDialog(this);
			}
			_waiting.Text = "Waiting for your OK (" + MovieFiling.Waiting().Count + ")...";
		}

		private void AddMovie()
		{
			string t = (_title.Text ?? "").Trim(), y = (_year.Text ?? "").Trim();
			if (t.Length == 0) return;
			int n;
			if (y.Length > 0 && !(int.TryParse(y, out n) && n > 1880 && n < 2100))
			{
				MessageBox.Show(this, "The year should be like 2024 - or leave it empty.", "Wanted movies", MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}
			_note.Text = WantedFilms.Add(t, y) ? t + " added - MediaPortal looks for it soon." : t + " is already on the list.";
			_title.Text = _year.Text = "";
			Fill();
			_title.Focus();
		}

		private void RemoveSelected()
		{
			if (_list.SelectedItems.Count == 0) return;
			foreach (ListViewItem i in _list.SelectedItems)
			{
				WantedFilms.Film x = (WantedFilms.Film)i.Tag;
				WantedFilms.Remove(x.Title, x.Year);
			}
			Fill();
		}

		public void Save()
		{
			Configuration.SetExtra("Films.Quality", _quality.SelectedItem as string ?? "1080p");
			Configuration.SetExtra("Films.MaxSizeMB", ((long)_size.Value).ToString());
			Configuration.SetExtra("Films.MinSizeMB", Films.MinSizeChoices[Math.Max(0, _minSize.SelectedIndex)].ToString());
			Configuration.SetExtra("Films.LookEveryHours", LookChoices[Math.Max(0, _look.SelectedIndex)].ToString());
			Configuration.SetExtra("Films.Sound", Sound.Choices[Math.Max(0, _sound.SelectedIndex)]);
			Configuration.SetExtra("Films.Favourites", string.Join(", ", (_favs.Text ?? "").Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries).Select(f => f.Trim()).Where(f => f.Length > 0)));
			Configuration.SetExtra("Films.FileInto", _fileForMe.Checked ? "library" : _askFirst.Checked ? "ask" : "new");
			Configuration.SetExtra("Films.MoviesFolder", (_moviesFolder.Text ?? "").Trim());
			Configuration.SetExtra("Films.CollectionsFolder", (_collectionsFolder.Text ?? "").Trim());
			Configuration.SetExtra("Films.WriteNfo", _nfo.Checked ? "true" : "false");
			if ((_fileForMe.Checked || _askFirst.Checked) && (_moviesFolder.Text ?? "").Trim().Length == 0)
				MessageBox.Show(this, "\"File them for me\" needs a Movies folder - until you pick one, new movies stay in your new-movies folder.", "Movies", MessageBoxButtons.OK, MessageBoxIcon.Information);
			Configuration.SetExtra("Trakt.ListOn", _traktOn.Checked ? "true" : "false");
			Configuration.SetExtra("Trakt.ListRemoveDropped", _traktRemove.Checked ? "true" : "false");
			Configuration.SetExtra("Trakt.ListName", (_traktList.Text ?? "").Trim());
		}

		private Button Browse(ComboBox target, int x, int y)
		{
			Button b = new Button { Text = "...", Location = new Point(x, y), Size = new Size(30, 23) };
			b.Click += (s, e) =>
			{
				using (FolderBrowserDialog d = new FolderBrowserDialog { SelectedPath = target.Text, ShowNewFolderButton = true })
				{
					if (d.ShowDialog(this) == DialogResult.OK) target.Text = d.SelectedPath;
				}
			};
			return b;
		}
	}
}
