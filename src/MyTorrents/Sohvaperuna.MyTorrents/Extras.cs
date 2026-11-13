using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MediaPortal.Configuration;
using MediaPortal.Dialogs;
using MediaPortal.GUI.Library;
using Sohvaperuna.TorrentSearch;
using TraktAPI.DataModels;

namespace Sohvaperuna.MyTorrents
{
	/// <summary>
	/// 2026 (1.3.41): what's missing (one show, and the whole library), Wanted films and the Trakt lists -
	/// in MediaPortal. MyTorrents Configuration shows the same lists from the files written here.
	/// </summary>
	public static class Extras
	{
		// ---------------------------------------------------------------- what's missing

		/// "Season 2: missing E03, E05" lines for a show, plus what's coming.
		public static List<string> MissingLines(Series s)
		{
			List<string> lines = new List<string>();
			if (s == null) return lines;
			List<string> aired = (s.AiredMissing ?? new List<string>()).Select(x => x.Split('|')[0]).Where(x => x.Length > 0).ToList();
			foreach (IGrouping<string, string> g in aired.GroupBy(x => x.Substring(0, Math.Min(3, x.Length))).OrderBy(g => g.Key))
			{
				lines.Add("Season " + int.Parse(g.Key.Substring(1)) + ": " + g.Count() + " missing - " + string.Join(", ", g.Select(x => x.Substring(3))));
			}
			if (lines.Count == 0) lines.Add("Up to date - nothing aired is missing");
			List<string> coming = (s.AllUnlocal ?? "").Split('|').Where(x => x.Length > 0 && !aired.Contains(x)).Take(3).ToList();
			foreach (string c in coming)
			{
				DateTime d = s.AirsOf(c);
				lines.Add("Coming: " + c + (d != DateTime.MinValue ? " - " + d.ToLocalTime().ToString("ddd d MMM", CultureInfo.InvariantCulture) : ""));
			}
			return lines;
		}

		public static void WhatsMissing(SeriesItem item)
		{
			if (item?.show == null) return;
			GUIDialogMenu menu = (GUIDialogMenu)GUIWindowManager.GetWindow(2012);
			if (menu == null) return;
			menu.Reset();
			int count = item.show.AiredMissing?.Count ?? 0;
			menu.SetHeading(item.show.Name + (count == 0 ? " - up to date" : " - " + count + " missing"));
			foreach (string l in MissingLines(item.show)) menu.Add(l);
			if (count > 0) menu.Add("Grab the missing ones");
			menu.DoModal(GUIWindowManager.ActiveWindow);
			Menus.Check(menu);
			if ((menu.SelectedLabelText ?? "") == "Grab the missing ones") GrabMissing(item.show.ID, item.show.Name);
		}

		/// Gets a show's missing aired episodes: in the Auto grabber it switches to "missing episodes" (new ones are
		/// still got as they come out); not in it yet, it is added that way. Checks straight away.
		public static void GrabMissing(string showId, string name)
		{
			WatchList wl = MyTorrents.WL;
			SeriesItem item = wl.GetSeriesList().FirstOrDefault(x => x.show != null && x.show.ID == showId);
			if (item != null) item.type = "Missing episodes";
			else
			{
				Series s = wl.allseries.FirstOrDefault(x => x != null && x.ID == showId);
				if (s == null)
				{
					Log.Instance().Print("Missing episodes: " + name + " is not in MP-TVSeries");
					return;
				}
				WatchListView.AddToAutoGrabber(s, "Missing episodes");
			}
			wl.Save();
			wl.CheckNow(showId, "grab the missing ones");
		}

		private static string ReportFile => Config.GetFile(Config.Dir.Config, "MyTorrents.missing.txt");

		/// Written when MediaPortal reads your shows: every show with aired episodes missing (for Configuration).
		public static void WriteReport(IEnumerable<Series> shows)
		{
			try
			{
				System.IO.File.WriteAllLines(ReportFile, shows.Where(s => s != null && (s.AiredMissing?.Count ?? 0) > 0)
					.OrderByDescending(s => s.AiredMissing.Count)
					.Select(s => string.Join("\t", s.ID, (s.Name ?? "").Replace("\t", " "), s.AiredMissing.Count.ToString(), string.Join(",", s.AiredMissing.Select(x => x.Split('|')[0])))));
			}
			catch
			{
			}
		}

		/// (id, name, count, codes) from the last report - for MyTorrents Configuration.
		public static List<string[]> ReadReport()
		{
			try
			{
				if (!System.IO.File.Exists(ReportFile)) return new List<string[]>();
				return System.IO.File.ReadAllLines(ReportFile).Select(l => l.Split('\t')).Where(p => p.Length >= 4).ToList();
			}
			catch
			{
				return new List<string[]>();
			}
		}

		public static void MissingReport()
		{
			WatchList wl = MyTorrents.WL;
			List<Series> shows = wl.allseries.Where(s => s != null && (s.AiredMissing?.Count ?? 0) > 0).OrderByDescending(s => s.AiredMissing.Count).ThenBy(s => s.Name).ToList();
			GUIDialogMenu menu = (GUIDialogMenu)GUIWindowManager.GetWindow(2012);
			if (menu == null) return;
			menu.Reset();
			menu.SetHeading(shows.Count == 0 ? "Missing episodes - your library is up to date" : "Missing episodes - " + shows.Count + " shows");
			HashSet<string> inGrabber = new HashSet<string>(wl.GetSeriesList().Where(x => x.show != null).Select(x => x.show.ID));
			foreach (Series s in shows) menu.Add(s.Name + ": " + s.AiredMissing.Count + " missing" + (inGrabber.Contains(s.ID) ? "  (Auto grabber)" : ""));
			menu.DoModal(GUIWindowManager.ActiveWindow);
			Menus.Check(menu);
			if (menu.SelectedId < 1 || menu.SelectedId > shows.Count) return;
			Series pick = shows[menu.SelectedId - 1];
			menu.Reset();
			menu.SetHeading(pick.Name);
			foreach (string l in MissingLines(pick)) menu.Add(l);
			menu.Add("Grab the missing ones");
			menu.Add("Search all torrents");
			menu.DoModal(GUIWindowManager.ActiveWindow);
			Menus.Check(menu);
			string c = menu.SelectedLabelText ?? "";
			if (c == "Grab the missing ones") GrabMissing(pick.ID, pick.Name);
			else if (c == "Search all torrents") MyTorrents.Instance().SearchFor(pick.Name, SearchContext.TV);
		}

		// ---------------------------------------------------------------- 2026 (1.3.42): Trakt plugin screens

		private static string Prop(string name)
		{
			string v = GUIPropertyManager.GetProperty(name) ?? "";
			return v.StartsWith("#") ? "" : v.Trim();
		}

		/// "trakt-movie" / "trakt-show" / "trakt-list": the highlighted title on the Trakt screen you came from.
		/// Returns true when a search was started (so MyTorrents stays open), false to go back.
		public static bool FromTraktScreen(string what)
		{
			bool movie = what == "trakt-movie" || (what == "trakt-list" && Prop("#Trakt.List.ItemType") == "movie");
			bool show = what == "trakt-show" || (what == "trakt-list" && (Prop("#Trakt.List.ItemType") == "show" || Prop("#Trakt.List.ItemType") == "season" || Prop("#Trakt.List.ItemType") == "episode"));
			string title = movie ? Prop("#Trakt.Movie.Title") : show ? Prop("#Trakt.Show.Title") : "";
			string yearText = movie ? Prop("#Trakt.Movie.Year") : Prop("#Trakt.Show.Year");
			int year;
			int.TryParse(yearText, out year);
			if (title.Length == 0)
			{
				Notify("Torrent Search", "Highlight a movie or show first.");
				return false;
			}
			string name = title + (year > 0 ? " (" + year + ")" : "");
			GUIDialogMenu menu = (GUIDialogMenu)GUIWindowManager.GetWindow(2012);
			if (menu == null) return false;
			menu.Reset();
			if (movie)
			{
				bool have = LibraryFolders.HaveFilm(title, year);
				menu.SetHeading(name + (have ? " - you have it" : ""));
				menu.Add("Download now (search)");
				menu.Add("Add to Wanted movies");
				menu.DoModal(GUIWindowManager.ActiveWindow);
				Menus.Check(menu);
				if (menu.SelectedId == 1)
				{
					return DownloadNow(title, year);
				}
				if (menu.SelectedId == 2) AddWanted(title, year > 0 ? year.ToString() : "");
				return false;
			}
			bool inLibrary = LibraryFolders.FindShow(title) != null;
			menu.SetHeading(name + (inLibrary ? " - in your library" : ""));
			menu.Add("Search now");
			menu.Add("Add to the Auto grabber...");
			menu.DoModal(GUIWindowManager.ActiveWindow);
			Menus.Check(menu);
			if (menu.SelectedId == 1)
			{
				MyTorrents.Instance().SearchFor(title, SearchContext.TV);
				return true;
			}
			if (menu.SelectedId != 2) return false;
			WatchList wl = MyTorrents.WL;
			Series mine = wl.allseries.FirstOrDefault(x => x != null && MediaNames.Key(x.Name) == MediaNames.Key(title));
			if (mine != null)
			{
				// you have some of it - missing episodes, and new ones as they come
				GrabMissing(mine.ID, mine.Name);
				Notify("Auto grabber", mine.Name + " is in the Auto grabber - missing episodes and new ones.");
				return false;
			}
			TraktShowSummary found = null;
			try
			{
				found = (global::TraktAPI.TraktAPI.SearchShows(title) ?? Enumerable.Empty<TraktSearchResult>()).Where(r => r.Show != null).Select(r => r.Show)
					.OrderByDescending(s => year > 0 && s.Year == year).FirstOrDefault();
			}
			catch (Exception ex)
			{
				Log.Instance().Print("Trakt screen: show lookup failed: " + ex.Message);
			}
			if (found == null)
			{
				Notify("Auto grabber", "Could not find " + name + " on Trakt.");
				return false;
			}
			MyTorrents.ListType = "WatchList";
			MyTorrents.Instance().ShowTorrentWatch();
			MyTorrents.Instance().ViewAutoGrabber.AddNewSeries(found);
			return true;
		}

		// ---------------------------------------------------------------- 2026 (1.3.42): Can't find list

		public static void CantFindMenu()
		{
			while (true)
			{
				List<CantFind.Item> items = CantFind.All();
				GUIDialogMenu menu = (GUIDialogMenu)GUIWindowManager.GetWindow(2012);
				if (menu == null) return;
				menu.Reset();
				menu.SetHeading(items.Count == 0 ? "Can't find - nothing (all found within " + CantFind.Days + " days)" : "Can't find - looked for " + CantFind.Days + "+ days");
				if (items.Count == 0)
				{
					menu.Add("OK");
					menu.DoModal(GUIWindowManager.ActiveWindow);
					Menus.Check(menu);
					return;
				}
				foreach (CantFind.Item x in items) menu.Add((x.State == "skip" ? "(stopped) " : "") + x.What + "  -  " + x.Since + ", " + x.Why);
				menu.DoModal(GUIWindowManager.ActiveWindow);
				Menus.Check(menu);
				if (menu.SelectedId < 1) return;
				CantFind.Item pick = items[menu.SelectedId - 1];
				menu.Reset();
				menu.SetHeading(pick.What);
				menu.Add("Look for it now");
				menu.Add("Search all torrents (any size)");
				menu.Add(pick.State == "skip" ? "Start looking again" : "Stop looking for it");
				menu.DoModal(GUIWindowManager.ActiveWindow);
				Menus.Check(menu);
				int c = menu.SelectedId;
				if (c == 1)
				{
					if (pick.IsMovie)
					{
						WantedFilms.LookAgain(pick.Name, pick.Year);
						System.Threading.ThreadPool.QueueUserWorkItem(_ => { try { WantedFilms.Check(true); } catch { } });
					}
					else
					{
						CantFind.LookNow(pick.Key);
						MyTorrents.WL.CheckNow(pick.ShowId, "can't find - look now");
					}
					Notify("Can't find", "Looking for " + pick.What + " now.");
				}
				else if (c == 2)
				{
					MyTorrents.Instance().SearchFor(pick.IsMovie ? (pick.Name + " " + pick.Year).Trim() : pick.Name + " " + pick.Episode, pick.IsMovie ? SearchContext.Movie : SearchContext.TV);
					return;
				}
				else if (c == 3)
				{
					if (pick.State == "skip") CantFind.LookNow(pick.Key);
					else if (pick.IsMovie) WantedFilms.Remove(pick.Name, pick.Year);
					else CantFind.StopLooking(pick.Key);
				}
			}
		}

		// ---------------------------------------------------------------- 2026 (1.3.42): search again

		/// A show: look for its episodes now, or try a different copy of an episode it got this week.
		public static void SearchAgain(SeriesItem item)
		{
			if (item?.show == null) return;
			List<Tuple<string, string, string, DateTime>> got = Grabbed.ForShow(item.show.ID);
			GUIDialogMenu menu = (GUIDialogMenu)GUIWindowManager.GetWindow(2012);
			if (menu == null) return;
			menu.Reset();
			menu.SetHeading("Search again - " + item.show.Name);
			menu.Add("Look for its episodes now");
			foreach (Tuple<string, string, string, DateTime> g in got) menu.Add("Try a different copy of " + g.Item1 + "  (got " + g.Item4.ToString("ddd d MMM") + ")");
			menu.DoModal(GUIWindowManager.ActiveWindow);
			Menus.Check(menu);
			if (menu.SelectedId < 1) return;
			WatchList wl = MyTorrents.WL;
			if (menu.SelectedId == 1)
			{
				wl.CheckNow(item.show.ID, "search again");
				Notify("Search again", "Looking for " + item.show.Name + " now.");
				return;
			}
			Tuple<string, string, string, DateTime> pick = got[menu.SelectedId - 2];
			menu.Reset();
			menu.SetHeading(pick.Item1 + ": " + pick.Item2);
			menu.Add("Try a different copy - delete this one");
			menu.Add("Try a different copy - keep this one's files");
			menu.DoModal(GUIWindowManager.ActiveWindow);
			Menus.Check(menu);
			if (menu.SelectedId < 1) return;
			bool delete = menu.SelectedId == 1;
			System.Threading.ThreadPool.QueueUserWorkItem(_ =>
			{
				try
				{
					Grabbed.TryDifferentCopy(pick.Item3, delete);
					wl.CheckNow(item.show.ID, "try a different copy of " + pick.Item1);
				}
				catch (Exception ex)
				{
					Log.Instance().Print("Try a different copy: " + ex.Message);
				}
			});
			Notify("Search again", "Looking for a different copy of " + item.show.Name + " " + pick.Item1 + ".");
		}

		// ---------------------------------------------------------------- Wanted films

		public static void WantedFilmsMenu()
		{
			while (true)
			{
				List<WantedFilms.Film> films = WantedFilms.Read();
				GUIDialogMenu menu = (GUIDialogMenu)GUIWindowManager.GetWindow(2012);
				if (menu == null) return;
				menu.Reset();
				menu.SetHeading("Wanted movies (" + Films.Quality + ", up to " + Films.SizeText(Films.MaxSizeMB) + ")");
				menu.Add("Add a movie...");
				menu.Add("Read my movies folders again"); // 2026 (1.3.43): after you've moved movies into Movies / Collections
				int waiting = MovieFiling.Waiting().Count;
				menu.Add("New movies waiting for your OK (" + waiting + ")..."); // 2026 (1.3.45)
				menu.Add("Collections you could make..."); // 2026 (1.3.46)
				menu.Add("Movies to improve - better copy / 5.1..."); // 2026 (1.3.46 / 1.3.49; one list since 1.3.65)
				foreach (WantedFilms.Film f in films) menu.Add(f.Display + "  -  " + WantedFilms.StateText(f));
				menu.DoModal(GUIWindowManager.ActiveWindow);
				Menus.Check(menu);
				int pick = menu.SelectedId;
				if (pick < 1) return;
				if (pick == 1)
				{
					AddFilm(null);
					continue;
				}
				if (pick == 2)
				{
					LibraryFolders.RereadLibrary();
					Notify("Wanted movies", "Reading your movies folders again in the background.");
					continue;
				}
				if (pick == 3)
				{
					WaitingMenu();
					continue;
				}
				if (pick == 4)
				{
					CollectionsMenu();
					continue;
				}
				if (pick == 5)
				{
					ImproveMenu();
					continue;
				}
				WantedFilms.Film film = films[pick - 6];
				menu.Reset();
				menu.SetHeading(film.Display);
				menu.Add("Look for it now");
				menu.Add("Search all torrents");
				menu.Add("Remove from Wanted movies");
				menu.DoModal(GUIWindowManager.ActiveWindow);
				Menus.Check(menu);
				string c = menu.SelectedLabelText ?? "";
				if (c == "Look for it now")
				{
					WantedFilms.LookAgain(film.Title, film.Year);
					System.Threading.ThreadPool.QueueUserWorkItem(_ => { try { WantedFilms.Check(); } catch (Exception ex) { Log.Instance().Print("Wanted movies: " + ex.Message); } });
					Notify("Wanted movies", "Looking for " + film.Display + " now.");
				}
				else if (c == "Search all torrents")
				{
					MyTorrents.Instance().SearchFor((film.Title + " " + film.Year).Trim(), SearchContext.Movie);
					return;
				}
				else if (c.StartsWith("Remove")) WantedFilms.Remove(film.Title, film.Year);
			}
		}

		/// 2026 (1.3.46): films that belong together in a collection - move them in, see what's missing, or say no.
		public static void CollectionsMenu()
		{
			while (true)
			{
				List<Collections.Suggestion> list = Collections.Suggestions();
				GUIDialogMenu menu = (GUIDialogMenu)GUIWindowManager.GetWindow(2012);
				if (menu == null) return;
				menu.Reset();
				menu.SetHeading(list.Count == 0 ? "No collections to suggest" : "Collections you could make");
				if (list.Count == 0)
				{
					menu.Add("OK");
					menu.DoModal(GUIWindowManager.ActiveWindow);
					Menus.Check(menu);
					return;
				}
				foreach (Collections.Suggestion x in list) menu.Add(x.Name + "  -  " + (x.FolderExists ? "add " + x.Films.Count + " to yours" : x.Films.Count + " movies"));
				menu.DoModal(GUIWindowManager.ActiveWindow);
				Menus.Check(menu);
				if (menu.SelectedId < 1) return;
				Collections.Suggestion pick = list[menu.SelectedId - 1];
				menu.Reset();
				menu.SetHeading(pick.Name);
				menu.Add("Move them into Collections\\" + System.IO.Path.GetFileName(pick.Folder));
				menu.Add("What's missing from it? (add to Wanted movies)");
				menu.Add("Not this one");
				foreach (Collections.Film f in pick.Films) menu.Add("   " + f.Display);
				menu.DoModal(GUIWindowManager.ActiveWindow);
				Menus.Check(menu);
				int c = menu.SelectedId;
				if (c == 1)
				{
					Notify("Collections", "Moving " + pick.Films.Count + " movies into " + pick.Name + " in the background.");
					System.Threading.ThreadPool.QueueUserWorkItem(_ =>
					{
						try
						{
							string r = Collections.Make(pick);
							Log.Instance().Print("Collections: " + pick.Name + " - " + (r.Length == 0 ? "done" : r));
						}
						catch (Exception ex)
						{
							Log.Instance().Print("Collections: " + ex.Message);
						}
					});
					return;
				}
				if (c == 2)
				{
					List<Collections.Film> gone = Collections.Missing(pick);
					int n = gone.Count(g => WantedFilms.Add(g.Title, g.Year > 0 ? g.Year.ToString() : ""));
					Notify("Collections", gone.Count == 0 ? "Nothing missing from " + pick.Name + "." : n + " added to Wanted movies: " + string.Join(", ", gone.Select(g => g.Display)));
				}
				else if (c == 3) Collections.Ignore(pick);
			}
		}

		/// 2026 (1.3.65): Movies to improve - poor copies and stereo-only movies in one list, by title (or by year).
		public static void ImproveMenu()
		{
			bool byYear = false;
			while (true)
			{
				List<Improve.Item> list = Improve.List();
				if (byYear) list = list.OrderBy(m => m.Year).ThenBy(m => m.Title).ToList();
				GUIDialogMenu menu = (GUIDialogMenu)GUIWindowManager.GetWindow(2012);
				if (menu == null) return;
				menu.Reset();
				menu.SetHeading(list.Count == 0 ? "No movies to improve" : list.Count + " movies to improve");
				if (list.Count == 0)
				{
					menu.Add("OK");
					menu.DoModal(GUIWindowManager.ActiveWindow);
					Menus.Check(menu);
					return;
				}
				menu.Add(byYear ? "Sort by title" : "Sort by year");
				menu.Add("Hidden - you said Not this one (" + Improve.Hidden().Count + ")..."); // 2026 (1.3.65)
				foreach (Improve.Item m in list) menu.Add(m.Display + "  -  " + m.Problem);
				menu.DoModal(GUIWindowManager.ActiveWindow);
				Menus.Check(menu);
				if (menu.SelectedId < 1) return;
				if (menu.SelectedId == 1)
				{
					byYear = !byYear;
					continue;
				}
				if (menu.SelectedId == 2)
				{
					HiddenMenu();
					continue;
				}
				Improve.Item pick = list[menu.SelectedId - 3];
				menu.Reset();
				menu.SetHeading(pick.Display);
				menu.Add("Look for " + pick.LooksFor + (SoundUpgrades.DeleteOld ? " (the old one goes once it's in)" : " (keep both)"));
				menu.Add("Not this one");
				menu.DoModal(GUIWindowManager.ActiveWindow);
				Menus.Check(menu);
				if (menu.SelectedId == 1)
				{
					Improve.Look(pick);
					Notify("Movies to improve", Improve.Added(pick) + ".");
				}
				else if (menu.SelectedId == 2) Improve.Ignore(pick);
			}
		}

		/// 2026 (1.3.65): movies hidden from Movies to improve - pick one to put it back.
		private static void HiddenMenu()
		{
			while (true)
			{
				List<Improve.Item> list = Improve.Hidden();
				GUIDialogMenu menu = (GUIDialogMenu)GUIWindowManager.GetWindow(2012);
				if (menu == null) return;
				menu.Reset();
				menu.SetHeading(list.Count == 0 ? "Nothing hidden" : list.Count + " hidden - pick one to put back");
				if (list.Count == 0)
				{
					menu.Add("OK");
					menu.DoModal(GUIWindowManager.ActiveWindow);
					Menus.Check(menu);
					return;
				}
				menu.Add("Put them all back");
				foreach (Improve.Item m in list) menu.Add(m.Display);
				menu.DoModal(GUIWindowManager.ActiveWindow);
				Menus.Check(menu);
				if (menu.SelectedId < 1) return;
				if (menu.SelectedId == 1)
				{
					foreach (Improve.Item m in list) Improve.PutBack(m.File);
					Notify("Movies to improve", "All put back.");
					return;
				}
				Improve.PutBack(list[menu.SelectedId - 2].File);
			}
		}

		/// 2026 (1.3.49): the check-up - what needs you, and putting it right.
		public static void CheckUpMenu()
		{
			while (true)
			{
				List<CheckUp.Finding> list = CheckUp.Read().OrderBy(x => x.Level == "fix" ? 0 : x.Level == "note" ? 1 : 2).ToList();
				GUIDialogMenu menu = (GUIDialogMenu)GUIWindowManager.GetWindow(2012);
				if (menu == null) return;
				menu.Reset();
				menu.SetHeading(CheckUp.Summary());
				menu.Add("Check now");
				foreach (CheckUp.Finding f in list) menu.Add((f.Level == "fix" ? "! " : f.Level == "note" ? "- " : "OK ") + f.Text);
				menu.DoModal(GUIWindowManager.ActiveWindow);
				Menus.Check(menu);
				if (menu.SelectedId < 1) return;
				if (menu.SelectedId == 1)
				{
					System.Threading.ThreadPool.QueueUserWorkItem(_ => { try { CheckUp.Run(); } catch (Exception ex) { Log.Instance().Print("Check-up: " + ex.Message); } });
					Notify("Check-up", "Checking in the background - open Check-up again in a minute or two.");
					return;
				}
				CheckUp.Finding pick = list[menu.SelectedId - 2];
				if (pick.Level == "ok") continue;
				menu.Reset();
				menu.SetHeading(pick.Area);
				if (pick.Action == "import") menu.Add("Add it to MP-TVSeries now");
				if (pick.Action == "dupe") menu.Add("Delete this copy (the other one stays)");
				if (pick.Action == "better") menu.Add("Look for a better copy");
				if (pick.Action == "leftover") menu.Add("Delete this sample / advert video");
				menu.Add("Ignore this");
				menu.DoModal(GUIWindowManager.ActiveWindow);
				Menus.Check(menu);
				string c = menu.SelectedLabelText ?? "";
				if (c.StartsWith("Add it")) { CheckUp.ImportAgain(pick); Notify("Check-up", "MP-TVSeries is adding it."); }
				else if (c.StartsWith("Delete")) { string r = pick.Action == "leftover" ? CheckUp.DeleteLeftover(pick) : CheckUp.DeleteExtra(pick); Notify("Check-up", r.Length == 0 ? "Deleted." : r); }
				else if (c.StartsWith("Look")) { PoorCopies.Look(pick.Title, pick.Year, pick.File); CheckUp.Done(pick); Notify("Check-up", pick.Title + " is on Wanted movies to replace."); }
				else if (c.StartsWith("Ignore")) CheckUp.Ignore(pick);
			}
		}

		/// 2026 (1.3.45): new movies waiting for your OK - file one, all, or leave one where it is.
		public static void WaitingMenu()
		{
			while (true)
			{
				List<MovieFiling.Pending> list = MovieFiling.Waiting();
				GUIDialogMenu menu = (GUIDialogMenu)GUIWindowManager.GetWindow(2012);
				if (menu == null) return;
				menu.Reset();
				menu.SetHeading(list.Count == 0 ? "No new movies waiting" : "New movies waiting for your OK");
				if (list.Count == 0)
				{
					menu.Add("OK");
					menu.DoModal(GUIWindowManager.ActiveWindow);
					Menus.Check(menu);
					return;
				}
				menu.Add("File them all");
				foreach (MovieFiling.Pending p in list) menu.Add(p.Title + "  ->  " + p.To);
				menu.DoModal(GUIWindowManager.ActiveWindow);
				Menus.Check(menu);
				if (menu.SelectedId < 1) return;
				if (menu.SelectedId == 1)
				{
					List<string> problems = list.Select(MovieFiling.Approve).Where(r => r.Length > 0).ToList();
					Notify("New movies", problems.Count == 0 ? "All filed." : problems.Count + " could not be filed - see the log.");
					return;
				}
				MovieFiling.Pending pick = list[menu.SelectedId - 2];
				menu.Reset();
				menu.SetHeading(pick.Title);
				menu.Add("File it there");
				menu.Add("Leave it in the new-movies folder");
				menu.DoModal(GUIWindowManager.ActiveWindow);
				Menus.Check(menu);
				if (menu.SelectedId == 1)
				{
					string r = MovieFiling.Approve(pick);
					Notify("New movies", r.Length == 0 ? pick.Title + " filed." : r);
				}
				else if (menu.SelectedId == 2) MovieFiling.Forget(pick);
			}
		}

		/// Type a title (or give one), pick the right film from Trakt, add it to Wanted films.
		public static void AddFilm(string title)
		{
			if (string.IsNullOrEmpty(title))
			{
				VirtualKeyboard keyboard = (VirtualKeyboard)GUIWindowManager.GetWindow(1002);
				if (keyboard == null) return;
				keyboard.Reset();
				keyboard.Text = "";
				keyboard.DoModal(GUIWindowManager.ActiveWindow);
				Menus.Check(keyboard);
				if (!keyboard.IsConfirmed || string.IsNullOrWhiteSpace(keyboard.Text)) return;
				title = keyboard.Text.Trim();
			}
			List<TraktMovie> found = new List<TraktMovie>();
			try
			{
				found = (global::TraktAPI.TraktAPI.SearchMovies(title) ?? Enumerable.Empty<TraktSearchResult>()).Where(r => r.Movie != null).Select(r => (TraktMovie)r.Movie).Take(15).ToList();
			}
			catch (Exception ex)
			{
				Log.Instance().Print("Wanted movies: Trakt search failed: " + ex.Message);
			}
			string t = title, y = "";
			if (found.Count > 0)
			{
				GUIDialogMenu menu = (GUIDialogMenu)GUIWindowManager.GetWindow(2012);
				if (menu == null) return;
				menu.Reset();
				menu.SetHeading("Which movie?");
				foreach (TraktMovie m in found) menu.Add(m.Title + (m.Year.HasValue ? " (" + m.Year + ")" : "") + (LibraryFolders.HaveFilm(m.Title, m.Year ?? 0) ? "  - you have it" : ""));
				menu.Add("Just \"" + title + "\"");
				menu.DoModal(GUIWindowManager.ActiveWindow);
				Menus.Check(menu);
				if (menu.SelectedId < 1) return;
				if (menu.SelectedId <= found.Count)
				{
					t = found[menu.SelectedId - 1].Title;
					y = found[menu.SelectedId - 1].Year?.ToString() ?? "";
				}
			}
			AddWanted(t, y);
		}

		private static void AddWanted(string title, string year)
		{
			int yy;
			int.TryParse(year, out yy);
			if (LibraryFolders.HaveFilm(title, yy))
			{
				Notify("Wanted movies", title + " is already in your movies.");
				return;
			}
			bool added = WantedFilms.Add(title, year);
			Notify("Wanted movies", added ? title + (year.Length > 0 ? " (" + year + ")" : "") + " added - it is downloaded when a good copy turns up." : title + " is already on the list.");
			if (added) System.Threading.ThreadPool.QueueUserWorkItem(_ => { try { WantedFilms.Check(); } catch { } });
		}

		// ---------------------------------------------------------------- Trakt lists

		/// Trakt's lists for you: recommended, watchlist, similar to the highlighted film / show, trending, popular,
		/// most anticipated. Pick one to download it or keep it for later.
		public static void TraktLists(string highlightedFilm, string highlightedShow)
		{
			GUIDialogMenu menu = (GUIDialogMenu)GUIWindowManager.GetWindow(2012);
			if (menu == null) return;
			List<KeyValuePair<string, Func<List<object>>>> lists = new List<KeyValuePair<string, Func<List<object>>>>();
			if (!string.IsNullOrEmpty(highlightedFilm)) lists.Add(Item("Movies like " + highlightedFilm, () => Related(highlightedFilm, true)));
			if (!string.IsNullOrEmpty(highlightedShow)) lists.Add(Item("Shows like " + highlightedShow, () => Related(highlightedShow, false)));
			lists.Add(Item("Movies recommended for you", () => FilmList(global::TraktAPI.TraktAPI.GetRecommendedMovies("full"))));
			lists.Add(Item("Shows recommended for you", () => Shows(global::TraktAPI.TraktAPI.GetRecommendedShows())));
			lists.Add(Item("My watchlist - movies", () => FilmList(global::TraktAPI.TraktAPI.GetWatchListMovies("me", "full", 1, 100)?.Items?.Select(i => i.Movie))));
			lists.Add(Item("My watchlist - shows", () => Shows(global::TraktAPI.TraktAPI.GetWatchListShows("me", "full", 1, 100)?.Items?.Select(i => i.Show))));
			lists.Add(Item("Trending movies", () => FilmList(global::TraktAPI.TraktAPI.GetTrendingMovies(1, 40)?.Movies?.Select(i => i.Movie))));
			lists.Add(Item("Popular movies", () => FilmList(global::TraktAPI.TraktAPI.GetPopularMovies(1, 40)?.Movies)));
			lists.Add(Item("Most anticipated movies", () => FilmList(global::TraktAPI.TraktAPI.GetAnticipatedMovies(1, 40)?.Movies?.Select(i => i.Movie))));
			lists.Add(Item("Trending shows", () => Shows(global::TraktAPI.TraktAPI.GetTrendingShows(1, 40)?.Shows?.Select(i => i.Show))));
			lists.Add(Item("Popular shows", () => Shows(global::TraktAPI.TraktAPI.GetPopularShows(1, 40)?.Shows)));
			lists.Add(Item("Most anticipated shows", () => Shows(global::TraktAPI.TraktAPI.GetAnticipatedShows(1, 40)?.Shows?.Select(i => i.Show))));
			menu.Reset();
			menu.SetHeading("Trakt");
			foreach (KeyValuePair<string, Func<List<object>>> l in lists) menu.Add(l.Key);
			menu.DoModal(GUIWindowManager.ActiveWindow);
			Menus.Check(menu);
			if (menu.SelectedId < 1) return;
			KeyValuePair<string, Func<List<object>>> chosen = lists[menu.SelectedId - 1];
			List<object> items;
			try
			{
				items = chosen.Value() ?? new List<object>();
			}
			catch (Exception ex)
			{
				Log.Instance().Print("Trakt: " + chosen.Key + ": " + ex.Message);
				Notify("Trakt", "Could not get the list from Trakt - is the Trakt plugin signed in?");
				return;
			}
			if (items.Count == 0)
			{
				Notify("Trakt", "Nothing in \"" + chosen.Key + "\" right now.");
				return;
			}
			menu.Reset();
			menu.SetHeading(chosen.Key);
			foreach (object o in items) menu.Add(Label(o));
			menu.DoModal(GUIWindowManager.ActiveWindow);
			Menus.Check(menu);
			if (menu.SelectedId < 1) return;
			Picked(items[menu.SelectedId - 1]);
		}

		private static KeyValuePair<string, Func<List<object>>> Item(string name, Func<List<object>> get) => new KeyValuePair<string, Func<List<object>>>(name, get);

		private static List<object> FilmList(IEnumerable<TraktMovieSummary> list) => (list ?? Enumerable.Empty<TraktMovieSummary>()).Where(m => m != null).Cast<object>().ToList();

		private static List<object> Shows(IEnumerable<TraktShowSummary> list) => (list ?? Enumerable.Empty<TraktShowSummary>()).Where(s => s != null).Cast<object>().ToList();

		private static List<object> Related(string title, bool film)
		{
			// find it on Trakt by name (and year), then its related titles
			string name = System.Text.RegularExpressions.Regex.Replace(title, @"\s*\(?\b(19|20)\d{2}\)?\s*$", "").Trim();
			if (film)
			{
				TraktMovie m = (global::TraktAPI.TraktAPI.SearchMovies(name) ?? Enumerable.Empty<TraktSearchResult>()).Where(r => r.Movie != null).Select(r => (TraktMovie)r.Movie).FirstOrDefault();
				string id = m?.Ids?.Slug ?? m?.Ids?.Trakt?.ToString();
				return id == null ? new List<object>() : FilmList(global::TraktAPI.TraktAPI.GetRelatedMovies(id, 20));
			}
			TraktShowSummary s = (global::TraktAPI.TraktAPI.SearchShows(name) ?? Enumerable.Empty<TraktSearchResult>()).Where(r => r.Show != null).Select(r => r.Show).FirstOrDefault();
			string sid = s?.Ids?.Slug ?? s?.Ids?.Trakt?.ToString();
			return sid == null ? new List<object>() : Shows(global::TraktAPI.TraktAPI.GetRelatedShows(sid, 20));
		}

		private static string Label(object o)
		{
			if (o is TraktMovieSummary m)
				return m.Title + (m.Year.HasValue ? " (" + m.Year + ")" : "") + (LibraryFolders.HaveFilm(m.Title, m.Year ?? 0) ? "  - you have it" : "");
			if (o is TraktShowSummary s)
				return s.Title + (s.Year.HasValue ? " (" + s.Year + ")" : "") + (LibraryFolders.FindShow(s.Title) != null ? "  - in your library" : "");
			return o.ToString();
		}

		private static void Picked(object o)
		{
			GUIDialogMenu menu = (GUIDialogMenu)GUIWindowManager.GetWindow(2012);
			if (menu == null) return;
			menu.Reset();
			if (o is TraktMovieSummary m)
			{
				string name = m.Title + (m.Year.HasValue ? " (" + m.Year + ")" : "");
				menu.SetHeading(name);
				menu.Add("Download now (search)");
				menu.Add("Add to Wanted movies");
				menu.DoModal(GUIWindowManager.ActiveWindow);
				Menus.Check(menu);
				if (menu.SelectedId == 1) DownloadNow(m.Title, m.Year ?? 0);
				else if (menu.SelectedId == 2) AddWanted(m.Title, m.Year?.ToString() ?? "");
				return;
			}
			if (o is TraktShowSummary s)
			{
				menu.SetHeading(s.Title + (s.Year.HasValue ? " (" + s.Year + ")" : ""));
				menu.Add("Search now");
				menu.Add("Add to the Auto grabber...");
				menu.DoModal(GUIWindowManager.ActiveWindow);
				Menus.Check(menu);
				if (menu.SelectedId == 1) MyTorrents.Instance().SearchFor(s.Title, SearchContext.TV);
				else if (menu.SelectedId == 2) { MyTorrents.ListType = "WatchList"; MyTorrents.Instance().ShowTorrentWatch(); MyTorrents.Instance().ViewAutoGrabber.AddNewSeries(s); }
			}
		}

		/// 2026 (1.3.42): search for a movie to download now - asks first when you already have it.
		public static bool DownloadNow(string title, int year)
		{
			if (LibraryFolders.HaveFilm(title, year))
			{
				GUIDialogYesNo ask = (GUIDialogYesNo)GUIWindowManager.GetWindow(100);
				if (ask != null)
				{
					ask.Reset();
					ask.SetHeading(title + (year > 0 ? " (" + year + ")" : ""));
					ask.SetLine(1, "You already have this movie.");
					ask.SetLine(2, "Search for it anyway?");
					ask.SetDefaultToYes(false);
					ask.DoModal(GUIWindowManager.ActiveWindow);
					Menus.Check(ask);
					if (!ask.IsConfirmed) return false;
				}
			}
			MyTorrents.Instance().SearchFor((title + " " + (year > 0 ? year.ToString() : "")).Trim(), SearchContext.Movie);
			return true;
		}

		private static void Notify(string heading, string text)
		{
			try
			{
				GUIDialogNotify n = (GUIDialogNotify)GUIWindowManager.GetWindow(2016);
				if (n == null) return;
				n.Reset();
				n.SetHeading(heading);
				n.SetText(text);
				n.TimeOut = 6;
				n.DoModal(GUIWindowManager.ActiveWindow);
				Menus.Check(n);
			}
			catch
			{
			}
		}
	}
}
