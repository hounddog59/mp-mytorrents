using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using MediaPortal.Configuration;
using Sohvaperuna.TorrentSearch;

namespace Sohvaperuna.MyTorrents
{
	/// <summary>
	/// 2026 (1.3.41): "Wanted films" - films you want but don't have. MediaPortal looks for each one every few hours
	/// and downloads the first good copy (film quality and size, enough seeds); finished films are filed in your
	/// film folder as "Title (Year)". The list is a small file both MediaPortal and MyTorrents Configuration use.
	/// </summary>
	public static class WantedFilms
	{
		public sealed class Film
		{
			public string Title = "", Year = "", State = "wanted", Note = "";
			/// 2026 (1.3.46): a sound upgrade - the stereo copy this 5.1 one replaces ("" = an ordinary wanted movie)
			public string Replace = "";
			/// 2026 (1.3.49): what kind of upgrade: "" / "5.1" = a 5.1 copy, "better" = a better copy (too small / poor)
			public string Why = "";
			/// 2026 (1.3.50): the download it started (so a copy that never gets going is swapped for another)
			public string Hash = "", Link = "";
			public DateTime Started = DateTime.MinValue;
			public string DeadKey => MediaNames.Key(Title) + Year;
			public bool Better => Replace.Length > 0 && (Why == "better" || Why == "best");
			/// 2026 (1.3.65): "best" = a poor copy that is also stereo - a better copy, 5.1 when there is one
			public bool Best => Replace.Length > 0 && Why == "best";
			public DateTime Added = DateTime.Now, LastLook = DateTime.MinValue;
			public string Display => Title + (Year.Length > 0 ? " (" + Year + ")" : "") + (Replace.Length > 0 ? (Best ? " - better copy (5.1 if there is one)" : Better ? " - better copy" : " - 5.1 upgrade") : "");
		}

		private static readonly object Lock = new object();
		private static string File => Config.GetFile(Config.Dir.Config, "MyTorrents.wanted-films.txt");
		private static string Clean(string s) => (s ?? "").Replace("\t", " ").Replace("\r", " ").Replace("\n", " ").Trim();

		/// Hours between looks for the same film (setting "Films.LookEveryHours", 3).
		public static int LookEveryHours
		{
			get
			{
				int h;
				return int.TryParse(Configuration.GetExtra("Films.LookEveryHours", "3"), out h) && h > 0 ? h : 3;
			}
		}

		public static List<Film> Read()
		{
			List<Film> list = new List<Film>();
			lock (Lock)
			{
				try
				{
					if (!System.IO.File.Exists(File)) return list;
					foreach (string line in System.IO.File.ReadAllLines(File))
					{
						string[] f = line.Split('\t');
						if (f.Length < 1 || f[0].Trim().Length == 0) continue;
						Film x = new Film { Title = f[0].Trim(), Year = f.Length > 1 ? f[1].Trim() : "", State = f.Length > 2 && f[2].Trim().Length > 0 ? f[2].Trim() : "wanted", Note = f.Length > 5 ? f[5] : "" };
						DateTime d;
						if (f.Length > 3 && DateTime.TryParseExact(f[3], "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) x.Added = d;
						if (f.Length > 4 && DateTime.TryParseExact(f[4], "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) x.LastLook = d;
						if (f.Length > 6) x.Replace = f[6];
						if (f.Length > 7) x.Why = f[7].Trim();
						if (f.Length > 8) x.Hash = f[8].Trim();
						if (f.Length > 9) x.Link = f[9].Trim();
						if (f.Length > 10 && DateTime.TryParseExact(f[10], "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) x.Started = d;
						list.Add(x);
					}
				}
				catch
				{
				}
			}
			return list;
		}

		private static void Write(List<Film> list)
		{
			lock (Lock)
			{
				try
				{
					System.IO.File.WriteAllLines(File, list.Select(x => string.Join("\t", Clean(x.Title), Clean(x.Year), Clean(x.State),
						x.Added.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
						x.LastLook == DateTime.MinValue ? "" : x.LastLook.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), Clean(x.Note), Clean(x.Replace), Clean(x.Why), Clean(x.Hash), Clean(x.Link),
						x.Started == DateTime.MinValue ? "" : x.Started.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))));
				}
				catch (Exception ex)
				{
					Log.Instance().Print("Wanted movies: could not save the list: " + ex.Message);
				}
			}
		}

		/// Adds a film (false when it is already on the list).
		/// 2026 (1.3.46): a 5.1 copy to replace the stereo one at "replace".
		public static bool AddUpgrade(string title, string year, string replace) => AddUpgrade(title, year, replace, "");

		/// why: "" = a 5.1 copy, "better" = 2026 (1.3.49) a better copy of a movie you have (too small / poor),
		/// "best" = 2026 (1.3.65) both: any better copy, a 5.1 one first.
		public static bool AddUpgrade(string title, string year, string replace, string why)
		{
			title = Clean(title);
			year = Clean(year);
			if (title.Length == 0) return false;
			List<Film> list = Read();
			if (list.Any(x => x.Replace == replace && x.State != "have")) return false;
			list.Add(new Film { Title = title, Year = year, Replace = replace, Why = why ?? "" });
			Write(list);
			Log.Instance().Print("Wanted movies: looking for a " + (why == "best" ? "better (5.1 if there is one)" : why == "better" ? "better" : "5.1") + " copy of " + title + (year.Length > 0 ? " (" + year + ")" : ""));
			return true;
		}

		/// Saves the list (1.3.46: used by the sound upgrades).
		public static void Save(List<Film> list) => Write(list);

		public static bool Add(string title, string year)
		{
			title = Clean(title);
			year = Clean(year);
			if (title.Length == 0) return false;
			List<Film> list = Read();
			if (list.Any(x => MediaNames.Key(x.Title) == MediaNames.Key(title) && (x.Year == year || x.Year.Length == 0 || year.Length == 0))) return false;
			list.Add(new Film { Title = title, Year = year });
			Write(list);
			Log.Instance().Print("Wanted movies: added " + title + (year.Length > 0 ? " (" + year + ")" : ""));
			return true;
		}

		public static void Remove(string title, string year)
		{
			List<Film> list = Read();
			list.RemoveAll(x => x.Title == title && x.Year == year);
			Write(list);
		}

		/// Look again for this film at the next check.
		public static void LookAgain(string title, string year)
		{
			List<Film> list = Read();
			foreach (Film x in list.Where(x => x.Title == title && x.Year == year))
			{
				x.LastLook = DateTime.MinValue;
				if (x.State != "have") x.State = "wanted";
			}
			Write(list);
		}

		public static string StateText(Film x)
		{
			switch (x.State)
			{
			case "grabbed": return "downloading / got it" + (x.Note.Length > 0 ? " - " + x.Note : "");
			case "have": return x.Replace.Length > 0 ? (x.Better ? "better copy in place" : "upgraded to 5.1") : "already in your movies";
			default: return x.LastLook == DateTime.MinValue ? "not looked for yet" : "looked " + x.LastLook.ToString("ddd h:mm tt") + (x.Note.Length > 0 ? " - " + x.Note : "");
			}
		}

		/// MediaPortal: looks for the wanted films that are due (called from the Auto grabber's checks).
		private static int _busy;

		public static void Check(bool now = false)
		{
			if (System.Threading.Interlocked.Exchange(ref _busy, 1) == 1) return; // one look at a time
			try
			{
				CheckDue(now);
			}
			finally
			{
				System.Threading.Interlocked.Exchange(ref _busy, 0);
			}
		}

		/// 2026 (1.3.50): a wanted movie's copy that never got going - no details after 2 hours (a dead magnet: the
		/// "Fetching details" ones), nothing downloaded after 6, or stuck with no seeds for the "Stuck downloads"
		/// hours - is removed, remembered as dead, and the movie is looked for again (another copy).
		public static void CheckDead()
		{
			List<Film> list = Read().Where(f => f.State == "grabbed").ToList();
			// 2026 (1.3.51): ones grabbed before 1.3.50 didn't keep their download - find it by the copy's name
			foreach (Film f in list.Where(x => x.Hash.Length == 0))
			{
				int cut = f.Note.LastIndexOf(" (", StringComparison.Ordinal);
				string copy = cut > 0 ? f.Note.Substring(0, cut) : f.Note;
				string h = PostProcessor.HashForTitle(SearchContext.Movie, copy);
				if (h.Length == 0) continue;
				f.Hash = h;
				if (f.Started == DateTime.MinValue) f.Started = f.LastLook > DateTime.MinValue ? f.LastLook : DateTime.Now.AddDays(-1);
			}
			list = list.Where(f => f.Hash.Length > 0 && f.Started > DateTime.MinValue).ToList();
			if (list.Count == 0) return;
			Sohvaperuna.TorrentEngine.ITorrentClient session;
			List<Sohvaperuna.TorrentEngine.Torrent> all;
			try
			{
				session = TorrentEngine.Instance().TorrentSession;
				all = TorrentEngine.Instance().TorrentsAll;
			}
			catch
			{
				return;
			}
			if (session == null || all == null) return;
			List<Film> gaveUp = new List<Film>();
			foreach (Film f in list)
			{
				Sohvaperuna.TorrentEngine.Torrent t = all.FirstOrDefault(x => string.Equals(x.Hash, f.Hash, StringComparison.OrdinalIgnoreCase));
				TimeSpan age = DateTime.Now - f.Started;
				if (t == null)
				{
					// 2026 (1.3.51): removed by hand before it finished - that copy counts as bad, look for another
					if (all.Count > 0 && PostProcessor.Enabled && age > TimeSpan.FromMinutes(10) && !PostProcessor.WasCopied(f.Hash))
					{
						Grabbed.MarkMovieDead(f.DeadKey, f.Link);
						Log.Instance().Print("Wanted movies: " + f.Display + " - its download was removed before it finished - another copy is looked for");
						gaveUp.Add(f);
					}
					continue;
				}
				if (t.Progress >= 100 || PostProcessor.WasCopied(t.Hash)) continue;
				bool noDetails = t.Size <= 0 || string.IsNullOrEmpty(t.Name) || t.Name.Equals(t.Hash, StringComparison.OrdinalIgnoreCase);
				bool dead = (noDetails && age > Grabbed.NoDetailsLimit) || (!noDetails && t.Progress <= 0 && t.Downloaded <= 0 && age > Grabbed.NoProgressLimit) ||
					(!noDetails && !t.HasStatus(Sohvaperuna.TorrentEngine.ClientSupport.Paused) && Grabbed.HasStalled(t, Grabbed.StuckHours > 0 ? Math.Max(Grabbed.StuckHours, 12) : 0));
				if (!dead) continue;
				Grabbed.ForgetStalled(t.Hash);
				try
				{
					session.Remove(t.Hash, true);
				}
				catch (Exception ex)
				{
					Log.Instance().Print("Wanted movies: could not remove " + f.Display + ": " + ex.Message);
					continue;
				}
				Grabbed.MarkMovieDead(f.DeadKey, f.Link);
				Log.Instance().Print("Wanted movies: " + f.Display + " - " + (noDetails ? "never got its details" : "never got going") + " - removed, another copy is looked for");
				gaveUp.Add(f);
			}
			if (gaveUp.Count == 0) return;
			List<Film> latest = Read();
			foreach (Film g in gaveUp)
			{
				Film x = latest.FirstOrDefault(y => y.Title == g.Title && y.Year == g.Year && y.State == "grabbed" && (y.Hash.Length == 0 || string.Equals(y.Hash, g.Hash, StringComparison.OrdinalIgnoreCase)));
				if (x == null) continue;
				x.State = "wanted";
				x.Hash = "";
				x.Link = "";
				x.LastLook = DateTime.MinValue;
				x.Note = "the last copy never got going - looking for another";
			}
			Write(latest);
		}

		private static void CheckDue(bool now)
		{
			List<Film> list = Read();
			SoundUpgrades.Finish(list); // 2026 (1.3.46): 5.1 copies that are now in place
			if (!now && Busy.TooBusy()) return; // 2026 (1.3.50): too many downloads going already
			List<Film> due = list.Where(x => x.State == "wanted" && (now || (DateTime.Now - x.LastLook > TimeSpan.FromHours(LookEveryHours) && !CantFind.MovieResting(x)))).Take(3).ToList();
			if (due.Count == 0) return;
			TorrentSearchEngine engines;
			try
			{
				engines = new TorrentSearchEngine(Configuration.Instance().Settings["TorrentSearch.Config"] as string);
			}
			catch (Exception ex)
			{
				Log.Instance().Print("Wanted movies: no search sites: " + ex.Message);
				return;
			}
			ITorrentSearch search = engines.Engines.OfType<TorrentSearch_Combiner>().FirstOrDefault(c => c.Name == TorrentSearchEngine.TVSeriesEngineName && c.Engines.Count > 0);
			if (search == null) return;
			foreach (Film film in due)
			{
				try
				{
					int year;
					int.TryParse(film.Year, out year);
					if (film.Replace.Length == 0 && LibraryFolders.HaveFilm(film.Title, year))
					{
						film.State = "have";
						film.Note = "";
						Log.Instance().Print("Wanted movies: " + film.Display + " is already in your movies");
						continue;
					}
					film.LastLook = DateTime.Now;
					string text = Regex.Replace(film.Title, "[^0-9a-zA-Z ]+", " ").Trim() + (film.Year.Length > 0 ? " " + film.Year : "");
					SearchContext.Kind = SearchContext.Movie;
					TorrentMatchList found = SearchContext.Filter(search.Search(text, "Default", null) ?? new TorrentMatchList(null));
					TorrentMatch best = Best(found, film);
					if (best == null)
					{
						film.Note = (found?.Count ?? 0) == 0 ? "nothing found yet" : (found.Count + " found, none fit " + Films.Quality + ", " + Films.SizeText(Films.MinSizeMB) + " to " + Films.SizeText(Films.MaxSizeMB));
						Log.Instance().Print("Wanted movies: " + film.Display + " - " + film.Note);
						continue;
					}
					string hash;
					string link = string.IsNullOrEmpty(best.Url) ? best.magnet : best.Url;
					bool ok = TorrentEngine.Instance().StartDownloadingToFolder(out hash, link, "", force: true, best.cookie, best.Id, "", "", "", string.IsNullOrEmpty(best.Url) ? "magnet" : "torrent");
					if (ok)
					{
						PostProcessor.Remember(hash, SearchContext.Movie, film.Display, best.Title);
						film.State = "grabbed";
						film.Hash = hash ?? "";
						film.Link = link ?? "";
						film.Started = DateTime.Now;
						film.Note = best.Title + " (" + (best.Size > 0 ? best.Size / 1024 / 1024 + " MB" : "size?") + ")";
					}
					else film.Note = "the download did not start - tried again later";
					Log.Instance().Print("Wanted movies: " + film.Display + " - " + (ok ? "downloading " : "could not start ") + best.Title);
				}
				catch (Exception ex)
				{
					Log.Instance().Print("Wanted movies: " + film.Display + ": " + ex.Message);
				}
			}
			// save what changed, keeping anything added meanwhile
			List<Film> latest = Read();
			foreach (Film d in due)
			{
				Film x = latest.FirstOrDefault(y => y.Title == d.Title && y.Year == d.Year);
				if (x == null) continue;
				x.State = d.State;
				x.Note = d.Note;
				x.LastLook = d.LastLook;
				x.Replace = d.Replace;
				x.Hash = d.Hash;
				x.Link = d.Link;
				x.Started = d.Started;
			}
			Write(latest);
		}

		/// The best copy: the right film (title words, year), film quality and size, enough seeds; favourites first,
		/// then the preferred video type, then the most seeds.
		private static TorrentMatch Best(TorrentMatchList found, Film film)
		{
			if (found == null || found.Count == 0) return null;
			string key = MediaNames.Key(film.Title);
			string q = Films.Quality;
			long max = Films.MaxSizeMB * 1024L * 1024L;
			long min = 0; // 2026 (1.3.47): too small = poor copy or fake - 1.3.49: per copy (x265 smaller), below
			int minSeeds = Math.Max(1, SeriesItem.MinSeeders);
			string[] favs = Films.Favourites;
			string sound = film.Replace.Length > 0 && !film.Better ? "only-surround" : Sound.Movies; // 2026 (1.3.45); 1.3.46: upgrades need 5.1
			if (film.Best) sound = "prefer-surround"; // 2026 (1.3.65): any better copy counts, a 5.1 one first
			// 2026 (1.3.49): a better copy must be clearly bigger than the one you have (a quarter more at least)
			if (film.Better)
			{
				try
				{
					long old = new System.IO.FileInfo(film.Replace).Length;
					min = Math.Max(min, old + old / 4);
				}
				catch
				{
				}
			}
			List<TorrentMatch> ok = found.Where(m =>
			{
				string t = m.Title ?? "";
				if (!MediaNames.Key(t).StartsWith(key)) return false;
				if (film.Year.Length == 4 && Regex.IsMatch(t, @"\b(19|20)\d{2}\b") && !t.Contains(film.Year)) return false;
				if (q != "Any" && !Naming.QualityOf(t).Equals(q, StringComparison.OrdinalIgnoreCase)) return false;
				if (max > 0 && m.Size > max) return false;
				if (m.Size > 0 && m.Size < Math.Max(min, Films.MinBytesFor(m))) return false;
				if (m.Seed < minSeeds) return false;
				if (Grabbed.IsMovieDead(film.DeadKey, string.IsNullOrEmpty(m.Url) ? m.magnet : m.Url)) return false; // 1.3.50: tried, never got going
				if (!Sound.Allowed(m, sound)) return false;
				return !Regex.IsMatch(t, @"\b(cam|hdcam|ts|telesync|hdts|screener)\b", RegexOptions.IgnoreCase);
			}).ToList();
			if (ok.Count == 0) return null;
			bool x265 = SeriesItem.Codec != null && SeriesItem.Codec.IndexOf("265", StringComparison.OrdinalIgnoreCase) >= 0;
			return ok.OrderByDescending(m => film.Best ? Sound.Rank(m, sound) : 0) // 2026 (1.3.65): best = 5.1 first of all
				.ThenByDescending(m => Films.IsFavourite(m, favs))
				.ThenByDescending(m => x265 && Regex.IsMatch(m.Title ?? "", @"x265|hevc|h\.?265", RegexOptions.IgnoreCase))
				.ThenByDescending(m => Sound.Rank(m, sound))
				.ThenByDescending(m => m.Seed).First();
		}
	}
}
