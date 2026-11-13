using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Sohvaperuna.MyTorrents
{
	/// <summary>2026 (1.3.41): MyTorrents Configuration's "Wanted films" window - the same list MediaPortal uses.</summary>
	public static class WantedFilmsDialog
	{
		public static void Show(IWin32Window owner)
		{
			using (Form f = new Form())
			{
				f.Text = "Wanted movies";
				f.StartPosition = FormStartPosition.CenterParent;
				f.ClientSize = new Size(700, 400);
				f.MinimizeBox = false;
				f.ShowInTaskbar = false;
				f.Font = new Font("Microsoft Sans Serif", 8.25f);
				Label l = new Label
				{
					Text = "Movies you want. MediaPortal looks for each one every " + WantedFilms.LookEveryHours + " hours and downloads the first good copy (" +
						Films.Quality + ", up to " + Films.SizeText(Films.MaxSizeMB) + "). Movies you already have are ticked off by themselves.",
					Location = new Point(10, 10), Size = new Size(540, 30), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
				};
				ListView lv = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = true, Location = new Point(10, 44), Size = new Size(540, 270), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
				LookUp.AddTo(lv, it => { WantedFilms.Film x = it.Tag as WantedFilms.Film; int y; return x == null ? null : LookUp.What.Movie(x.Title, int.TryParse(x.Year, out y) ? y : 0); }); // 1.3.52
				lv.Columns.Add("Movie", 230);
				lv.Columns.Add("State", 190);
				lv.Columns.Add("Year", 45);
				lv.Columns.Add("Added", 75);
				ListSorter sorter = new ListSorter(lv, "Wanted") // 1.3.60: click a heading to sort
					.Key(0, i => MediaNames.Key((i.Tag as WantedFilms.Film)?.Title ?? ""))
					.Key(2, i => { int y; int.TryParse((i.Tag as WantedFilms.Film)?.Year, out y); return y; })
					.Key(3, i => (i.Tag as WantedFilms.Film)?.Added ?? DateTime.MinValue);
				Label tl = new Label { Text = "Title", Location = new Point(10, 327), AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
				TextBox title = new TextBox { Location = new Point(45, 324), Width = 250, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
				Label yl = new Label { Text = "Year", Location = new Point(305, 327), AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
				TextBox year = new TextBox { Location = new Point(338, 324), Width = 50, MaxLength = 4, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
				Button add = new Button { Text = "Add", Location = new Point(398, 322), Width = 70, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
				Button look = new Button { Text = "Look again now", Location = new Point(10, 362), Width = 110, Enabled = false, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
				Button remove = new Button { Text = "Remove", Location = new Point(126, 362), Width = 80, Enabled = false, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
				Button close = new Button { Text = "Close", DialogResult = DialogResult.Cancel, Location = new Point(470, 362), Width = 80, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
				// 2026 (1.3.43): after you've moved movies from Movies New into Movies / Collections
				Button reread = new Button { Text = "Read my movies again", Location = new Point(320, 362), Width = 140, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
				reread.Click += (s, e) =>
				{
					try
					{
						GrabRequests.AddRereadMovies();
						MessageBox.Show(f, "Sent to MediaPortal - it reads your movies folders again in the background.", "Wanted movies", MessageBoxButtons.OK, MessageBoxIcon.Information);
					}
					catch (Exception ex)
					{
						MessageBox.Show(f, "Could not hand it to MediaPortal: " + ex.Message, "MyTorrents", MessageBoxButtons.OK, MessageBoxIcon.Warning);
					}
				};
				Label hint = new Label { Text = "The year helps find the right movie.", Location = new Point(478, 327), AutoSize = true, ForeColor = Color.DimGray, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };

				Action fill = () =>
				{
					lv.BeginUpdate();
					lv.Items.Clear();
					foreach (WantedFilms.Film x in WantedFilms.Read()) lv.Items.Add(new ListViewItem(new[] { x.Display, WantedFilms.StateText(x), x.Year, x.Added.ToString("d MMM yyyy") }) { Tag = x });
					lv.EndUpdate();
					sorter.Apply();
				};
				lv.SelectedIndexChanged += (s, e) => look.Enabled = remove.Enabled = lv.SelectedItems.Count > 0;
				add.Click += (s, e) =>
				{
					string t = (title.Text ?? "").Trim(), y = (year.Text ?? "").Trim();
					if (t.Length == 0) return;
					int n;
					if (y.Length > 0 && !(int.TryParse(y, out n) && n > 1880 && n < 2100))
					{
						MessageBox.Show(f, "The year should be like 2024 - or leave it empty.", "Wanted movies", MessageBoxButtons.OK, MessageBoxIcon.Information);
						return;
					}
					if (!WantedFilms.Add(t, y)) MessageBox.Show(f, t + " is already on the list.", "Wanted movies", MessageBoxButtons.OK, MessageBoxIcon.Information);
					title.Text = year.Text = "";
					fill();
				};
				look.Click += (s, e) =>
				{
					foreach (ListViewItem i in lv.SelectedItems) { WantedFilms.Film x = (WantedFilms.Film)i.Tag; WantedFilms.LookAgain(x.Title, x.Year); }
					fill();
					MessageBox.Show(f, "MediaPortal looks for them within about 5 minutes (while it runs).", "Wanted movies", MessageBoxButtons.OK, MessageBoxIcon.Information);
				};
				remove.Click += (s, e) =>
				{
					foreach (ListViewItem i in lv.SelectedItems) { WantedFilms.Film x = (WantedFilms.Film)i.Tag; WantedFilms.Remove(x.Title, x.Year); }
					fill();
				};
				f.AcceptButton = add;
				f.Controls.AddRange(new Control[] { l, lv, tl, title, yl, year, add, look, remove, hint, reread, close });
				f.CancelButton = close;
				fill();
				f.ShowDialog(owner);
			}
		}
	}
}
