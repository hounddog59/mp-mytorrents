using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Sohvaperuna.MyTorrents
{
	/// <summary>2026 (1.3.49): "Check-up" tab - what the daily check-up found, with a button to put each thing right.</summary>
	public class CheckUpPage : TabPage
	{
		private readonly Label _summary = new Label { AutoSize = false, Font = new Font("Microsoft Sans Serif", 9f, FontStyle.Bold) };
		private readonly Button _now = new Button { Text = "Check now", AutoSize = true };
		private readonly Button _dups = new Button { Text = "Find duplicates...", AutoSize = true }; // 1.3.59
		private ListSorter _sorter; // 1.3.60: click a heading to sort, like every other list
		private readonly ListView _list = new ListView { View = View.Details, FullRowSelect = true, MultiSelect = true }; // 1.3.60: several at once (Ctrl / Shift)
		private readonly Button _fix = new Button { Text = "Fix it", AutoSize = true, Enabled = false };
		private readonly Button _open = new Button { Text = "Open folder", AutoSize = true, Enabled = false };
		private readonly Button _ignore = new Button { Text = "Ignore", AutoSize = true, Enabled = false };
		private readonly Button _all = new Button { Text = "Delete all the samples / adverts", AutoSize = true, Visible = false }; // 1.3.52
		private readonly Label _note = new Label { AutoSize = true, ForeColor = Color.DimGray };
		private readonly Label _help = new Label
		{
			AutoSize = false, ForeColor = Color.DimGray,
			Text = "Once a day MyTorrents checks what it did in the last " + CheckUp.Days + " days: every episode and movie is where it put it, in MP-TVSeries / Moving Pictures, " +
				"not there twice, not a poor copy - and that the NAS isn't filling up and the search sites answer. \"Fix it\" puts a thing right; \"Ignore\" stops it being mentioned. Hold Ctrl (or Shift) to pick several lines at once."
		};

		public CheckUpPage()
		{
			Text = "Check-up";
			Padding = new Padding(3);
			UseVisualStyleBackColor = true;
			Font = new Font("Microsoft Sans Serif", 8.25f);
			_list.Columns.Add("", 60);
			_list.Columns.Add("What", 90);
			_list.Columns.Add("", 600);
			_list.ShowItemToolTips = true;
			_sorter = new ListSorter(_list, "CheckUp").Key(0, i => { string l = (i.Tag as CheckUp.Finding)?.Level; return l == "fix" ? 0 : l == "note" ? 1 : 2; });
			Controls.AddRange(new Control[] { _summary, _now, _dups, _help, _list, _fix, _open, _ignore, _all, _note });
			_all.Click += (s, e) =>
			{
				List<CheckUp.Finding> left = _list.Items.Cast<ListViewItem>().Select(i => i.Tag as CheckUp.Finding).Where(x => x != null && x.Action == "leftover").ToList();
				if (left.Count == 0 || MessageBox.Show(this, "Delete these " + left.Count + " sample / advert videos?\r\n\r\nOnly small files with names like sample, RARBG, YTS... - never a movie or an episode.", "Check-up", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
				int bad = left.Count(x => CheckUp.DeleteLeftover(x).Length > 0);
				_note.Text = (left.Count - bad) + " deleted" + (bad > 0 ? ", " + bad + " not" : "") + ".";
				Fill();
			};
			_now.Click += (s, e) => RunNow();
			_dups.Click += (s, e) => { using (DuplicatesForm f = new DuplicatesForm()) f.ShowDialog(FindForm()); };
			_list.SelectedIndexChanged += (s, e) => UpdateButtons();
			LibraryDialogs.AddOpen(_list, i => (i.Tag as CheckUp.Finding)?.File); // 1.3.50: right-click / double-click opens the folder
			LookUp.AddTo(_list, i => { CheckUp.Finding f = i.Tag as CheckUp.Finding; return f == null || f.File.Length == 0 ? null : f.Title.Length > 0 ? LookUp.What.Movie(f.Title, f.Year) : LookUp.What.FromName(System.IO.Path.GetFileName(f.File)); }); // 1.3.52
			_fix.Click += (s, e) => Fix();
			_open.Click += (s, e) => OpenFolder();
			_ignore.Click += (s, e) =>
			{
				List<CheckUp.Finding> sel = SelectedAll().Where(f => f.Key.Length > 0 && f.Level != "ok").ToList();
				if (sel.Count == 0) return;
				foreach (CheckUp.Finding f in sel) CheckUp.Ignore(f);
				_note.Text = sel.Count + " ignored.";
				Fill();
			};
			Resize += (s, e) => LayOut();
			VisibleChanged += (s, e) => { if (Visible) Fill(); };
			LayOut();
		}

		private void LayOut()
		{
			int w = Math.Max(400, ClientSize.Width - 20);
			_summary.SetBounds(10, 12, w - 240, 20);
			_now.Location = new Point(10 + w - _now.PreferredSize.Width, 8);
			_dups.Location = new Point(_now.Left - 6 - _dups.PreferredSize.Width, 8);
			// 1.3.59: the help text gets the height it needs (it was cut off), and the Fix it row sits under it, above the list
			int helpHeight = TextRenderer.MeasureText(_help.Text, _help.Font, new Size(w, 0), TextFormatFlags.WordBreak).Height + 2;
			_help.SetBounds(10, 36, w, helpHeight);
			int row = _help.Bottom + 4;
			_fix.Location = new Point(10, row);
			_open.Location = new Point(_fix.Right + 6, row);
			_ignore.Location = new Point(_open.Right + 6, row);
			_all.Location = new Point(_ignore.Right + 12, row);
			_note.Location = new Point((_all.Visible ? _all.Right : _ignore.Right) + 12, row + 5);
			int top = _fix.Bottom + 6;
			_list.SetBounds(10, top, w, Math.Max(100, ClientSize.Height - 8 - top));
			_list.Columns[2].Width = Math.Max(300, w - 160);
		}

		private void Fill()
		{
			_list.Items.Clear();
			List<CheckUp.Finding> found = CheckUp.Read();
			_summary.Text = CheckUp.Summary() + (CheckUp.LastRun > DateTime.MinValue ? "   -   checked " + CheckUp.LastRun.ToString("ddd d MMM h:mm tt") : "");
			foreach (CheckUp.Finding f in found.OrderBy(x => x.Level == "fix" ? 0 : x.Level == "note" ? 1 : 2))
			{
				string mark = f.Level == "fix" ? "Needs you" : f.Level == "note" ? "Note" : "OK";
				ListViewItem i = new ListViewItem(new[] { mark, f.Area, f.Text }) { Tag = f, ToolTipText = f.File };
				i.ForeColor = f.Level == "fix" ? Color.Firebrick : f.Level == "note" ? Color.DarkGoldenrod : Color.DarkGreen;
				_list.Items.Add(i);
			}
			_sorter?.Apply();
			UpdateButtons();
		}

		private CheckUp.Finding Selected() => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as CheckUp.Finding : null;

		/// 1.3.60: every line picked (Ctrl / Shift click)
		private List<CheckUp.Finding> SelectedAll() => _list.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag as CheckUp.Finding).Where(x => x != null).ToList();

		private static readonly string[] Fixable = { "import", "dupe", "better", "rename", "leftover", "dropdelete" };

		private void UpdateButtons()
		{
			List<CheckUp.Finding> sel = SelectedAll();
			CheckUp.Finding f = sel.FirstOrDefault();
			List<string> kinds = sel.Select(x => x.Action).Distinct().ToList();
			bool same = kinds.Count == 1 && Fixable.Contains(kinds[0]);
			int n = sel.Count;
			_fix.Enabled = same;
			if (f == null) _fix.Text = "Fix it";
			else if (!same) _fix.Text = n > 1 && kinds.Count > 1 ? "Fix it (pick ones of the same kind)" : "Fix it";
			else if (n == 1) _fix.Text = f.Action == "import" ? "Add to MP-TVSeries" : f.Action == "dupe" ? "Delete this copy" : f.Action == "better" ? "Look for a better copy" : f.Action == "rename" ? "Rename..." : f.Action == "dropdelete" ? "Delete it - you have it all" : "Delete it";
			else _fix.Text = f.Action == "import" ? "Add these " + n + " to MP-TVSeries" : f.Action == "dupe" ? "Delete these " + n + " copies" : f.Action == "better" ? "Look for better copies (" + n + ")" : f.Action == "rename" ? "Rename..." : "Delete these " + n;
			_all.Visible = _list.Items.Cast<ListViewItem>().Count(i => (i.Tag as CheckUp.Finding)?.Action == "leftover") > 1;
			_open.Enabled = n == 1 && f.File.Length > 0;
			int ign = sel.Count(x => x.Key.Length > 0 && x.Level != "ok");
			_ignore.Enabled = ign > 0;
			_ignore.Text = ign > 1 ? "Ignore these " + ign : "Ignore";
			LayOut();
		}

		private void RunNow()
		{
			_now.Enabled = false;
			_note.Text = "Checking - on a NAS this takes a minute...";
			BackgroundWorker w = new BackgroundWorker();
			w.DoWork += (s, e) => CheckUp.Run();
			w.RunWorkerCompleted += (s, e) =>
			{
				_now.Enabled = true;
				_note.Text = e.Error != null ? "Could not check: " + e.Error.Message : "";
				Fill();
			};
			w.RunWorkerAsync();
		}

		/// 1.3.60: works on every line picked, as long as they need the same kind of fix
		private void Fix()
		{
			List<CheckUp.Finding> sel = SelectedAll();
			if (sel.Count == 0 || sel.Select(x => x.Action).Distinct().Count() != 1) return;
			string action = sel[0].Action;
			int n = sel.Count, bad = 0;
			switch (action)
			{
			case "import":
				foreach (CheckUp.Finding f in sel) CheckUp.ImportAgain(f);
				_note.Text = "MP-TVSeries adds " + (n == 1 ? "it " : "them ") + (MyTorrents.InMediaPortal ? "now." : "when MediaPortal next runs.");
				break;
			case "dupe":
				if (MessageBox.Show(this, (n == 1 ? "Delete this copy?\r\n\r\n" + sel[0].File : "Delete these " + n + " copies?\r\n\r\n" + string.Join("\r\n", sel.Take(10).Select(f => Path.GetFileName(f.File)).ToArray()) + (n > 10 ? "\r\n..." : "")) +
					"\r\n\r\nThe other copy of each stays.", "Check-up", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
				bad = sel.Count(f => CheckUp.DeleteExtra(f).Length > 0);
				_note.Text = (n - bad) + " deleted" + (bad > 0 ? ", " + bad + " couldn't be" : "") + ".";
				break;
			case "better":
				foreach (CheckUp.Finding f in sel)
				{
					PoorCopies.Look(f.Title, f.Year, f.File);
					CheckUp.Done(f);
				}
				_note.Text = (n == 1 ? sel[0].Title + " is" : n + " movies are") + " on Wanted movies to replace.";
				break;
			case "rename":
				LibraryDialogs.RenameWindow(this);
				break;
			case "dropdelete":
				if (MessageBox.Show(this, (n == 1 ? "Delete \"" + Path.GetFileName(sel[0].File) + "\" from the drop folder?" : "Delete these " + n + " from the drop folder?\r\n\r\n" + string.Join("\r\n", sel.Take(10).Select(f => Path.GetFileName(f.File)).ToArray())) +
					"\r\n\r\nYou already have everything in " + (n == 1 ? "it" : "them") + " in your library, so nothing is lost.", "Check-up", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
				bad = sel.Count(f => DropFolder.Delete(f.File).Length > 0);
				_note.Text = (n - bad) + " deleted" + (bad > 0 ? ", " + bad + " couldn't be" : "") + ".";
				break;
			case "leftover":
				if (n > 1 && MessageBox.Show(this, "Delete these " + n + " sample / advert videos?", "Check-up", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
				bad = sel.Count(f => CheckUp.DeleteLeftover(f).Length > 0);
				_note.Text = (n - bad) + " deleted" + (bad > 0 ? ", " + bad + " couldn't be" : "") + ".";
				break;
			}
			Fill();
		}

		private void OpenFolder()
		{
			CheckUp.Finding f = Selected();
			if (f == null || f.File.Length == 0) return;
			try
			{
				if (File.Exists(f.File)) Process.Start("explorer.exe", "/select,\"" + f.File + "\"");
				else
				{
					string dir = Directory.Exists(f.File) ? f.File : Path.GetDirectoryName(f.File);
					if (Directory.Exists(dir)) Process.Start("explorer.exe", "\"" + dir + "\"");
					else _note.Text = "That folder isn't there any more.";
				}
			}
			catch (Exception ex)
			{
				_note.Text = ex.Message;
			}
		}
	}
}
