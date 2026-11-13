using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using MediaPortal.Configuration;

namespace Sohvaperuna.MyTorrents
{
	/// <summary>
	/// 2026 (1.3.49): the check-up ("how will I know that this is all working properly and that the
	/// files are in the right places, renamed correctly... I wouldn't know where to look"). Once a day by itself (and
	/// whenever asked) MyTorrents checks its own work of the last 7 days - read from its log, so it covers what was
	/// done before this version too - and writes a short list in plain words: MyTorrents.checkup.txt. Shown on the
	/// Check-up tab of MyTorrents Configuration, in MediaPortal (Auto grabber menu -> Check-up) and on the web page.
	/// No pop-ups from the background; things you say "ignore" to stay quiet (MyTorrents.checkup-ignore.txt).
	/// </summary>
	public static class CheckUp
	{
		public sealed class Finding
		{
			/// "ok", "note" (worth knowing) or "fix" (needs you)
			public string Level = "ok";
			public string Area = "", Text = "", File = "", Action = "", Key = "";
			/// for "better": the movie
			public string Title = "";
			public int Year;
		}

		public const int Days = 7;

		private static string ResultFile => Config.GetFile(Config.Dir.Config, "MyTorrents.checkup.txt");
		private static string IgnoreFile => Config.GetFile(Config.Dir.Config, "MyTorrents.checkup-ignore.txt");
		private static readonly object Lock = new object();
		private static int _running;

		public static DateTime LastRun
		{
			get
			{
				try
				{
					return System.IO.File.Exists(ResultFile) ? System.IO.File.GetLastWriteTime(ResultFile) : DateTime.MinValue;
				}
				catch
				{
					return DateTime.MinValue;
				}
			}
		}

		// ------------------------------------------------------------------ results file

		public static List<Finding> Read()
		{
			List<Finding> list = new List<Finding>();
			lock (Lock)
			{
				try
				{
					if (!System.IO.File.Exists(ResultFile)) return list;
					foreach (string line in System.IO.File.ReadAllLines(ResultFile))
					{
						string[] p = line.Split('\t');
						if (p.Length < 7) continue;
						int y;
						int.TryParse(p.Length > 7 ? p[7] : "0", out y);
						list.Add(new Finding { Level = p[0], Area = p[1], Text = p[2], File = p[3], Action = p[4], Key = p[5], Title = p[6], Year = y });
					}
				}
				catch
				{
				}
			}
			return list;
		}

		private static string Clean(string s) => (s ?? "").Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");

		private static void Write(List<Finding> list)
		{
			lock (Lock)
			{
				try
				{
					System.IO.File.WriteAllLines(ResultFile, list.Select(f => string.Join("\t", f.Level, Clean(f.Area), Clean(f.Text), Clean(f.File), f.Action, Clean(f.Key), Clean(f.Title), f.Year)));
				}
				catch (Exception ex)
				{
					Log.Instance().Print("Check-up: could not save: " + ex.Message);
				}
			}
		}

		private static HashSet<string> Ignored()
		{
			try
			{
				return System.IO.File.Exists(IgnoreFile) ? new HashSet<string>(System.IO.File.ReadAllLines(IgnoreFile).Where(l => l.Length > 0), StringComparer.OrdinalIgnoreCase) : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			}
			catch
			{
				return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			}
		}

		public static void Ignore(Finding f)
		{
			try
			{
				System.IO.File.AppendAllText(IgnoreFile, f.Key + Environment.NewLine);
				List<Finding> list = Read();
				list.RemoveAll(x => x.Key == f.Key);
				Write(list);
			}
			catch
			{
			}
		}

		/// Things that need you, for a one-line summary ("Check-up: 3 things need you").
		public static int NeedsYou() => Read().Count(f => f.Level == "fix");

		public static string Summary()
		{
			List<Finding> list = Read();
			if (list.Count == 0) return "Check-up: not run yet";
			int fix = list.Count(f => f.Level == "fix");
			return fix == 0 ? "Check-up: all good (" + LastRun.ToString("ddd h:mm tt") + ")" : "Check-up: " + fix + (fix == 1 ? " thing needs" : " things need") + " you";
		}

		// ------------------------------------------------------------------ running it

		/// Once a day by itself (from the Auto grabber's regular round), in the background, at low priority.
		public static void Daily()
		{
			if ((DateTime.Now - LastRun).TotalHours < 24) return;
			if (System.Threading.Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;
			System.Threading.Thread t = new System.Threading.Thread(() =>
			{
				try
				{
					Run();
				}
				catch (Exception ex)
				{
					Log.Instance().Print("Check-up: " + ex.Message);
				}
				finally
				{
					_running = 0;
				}
			}) { IsBackground = true, Priority = System.Threading.ThreadPriority.Lowest, Name = "MyTorrents check-up" };
			t.Start();
		}

		/// Runs all the checks now (a minute or so on a NAS) and saves the results.
		public static List<Finding> Run()
		{
			HashSet<string> ignored = Ignored();
			List<Finding> list = new List<Finding>();
			List<Tuple<DateTime, string, string>> copies = Copies(); // when, what was copied, where to

			// ---- TV
			Dictionary<string, string[]> tvByFile = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
			Dictionary<string, List<string>> tvByEpisode = new Dictionary<string, List<string>>();
			foreach (string row in LibraryFolders.Read("TVSeriesDatabase4.db3", "select ifnull(EpisodeFilename,'') || char(9) || SeriesID || char(9) || SeasonIndex || char(9) || EpisodeIndex from local_episodes"))
			{
				string[] p = row.Split('\t');
				if (p.Length < 4 || p[0].Length == 0) continue;
				tvByFile[p[0]] = p;
				string k = p[1] + "|" + p[2] + "|" + p[3];
				if (!tvByEpisode.TryGetValue(k, out List<string> l)) tvByEpisode[k] = l = new List<string>();
				l.Add(p[0]);
			}
			List<string> tvRoots = LibraryFolders.TV().Select(r => r.TrimEnd('\\') + "\\").ToList();
			HashSet<string> movieFiles = new HashSet<string>(LibraryFolders.Read("movingpictures.db3", "select fullpath from local_media"), StringComparer.OrdinalIgnoreCase);
			int tvOk = 0, movieOk = 0;
			Dictionary<string, List<HashSet<string>>> movieWords = MovieWordIndex(movieFiles);
			HashSet<string> tvNames = new HashSet<string>(tvByFile.Keys.Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase);
			foreach (var c in copies.GroupBy(x => x.Item3, StringComparer.OrdinalIgnoreCase).Select(g => g.Last()))
			{
				string file = c.Item3;
				bool tv = tvRoots.Any(r => file.StartsWith(r, StringComparison.OrdinalIgnoreCase))
					|| Regex.IsMatch(Path.GetFileName(file) ?? "", @"\bS\d{1,2}E\d{1,3}\b", RegexOptions.IgnoreCase); // 2026 (1.3.57): an episode in a folder MP-TVSeries doesn't scan ("The Haunting S02E01 isn't in Moving Pictures")
				string what = Path.GetFileNameWithoutExtension(file);
				bool exists;
				try
				{
					exists = System.IO.File.Exists(file);
				}
				catch
				{
					exists = false;
				}
				if (!exists)
				{
					// 2026 (1.3.56): moved or renamed since (by you, or by the movie renamer) but still in the library is fine
					if (!tv && SameFilmInLibrary(file, movieWords)) { movieOk++; continue; }
					if (tv && tvNames.Contains(Path.GetFileName(file))) { tvOk++; continue; }
					// moved or renamed by hand afterwards is fine for movies filed into Movies New - only say so once
					Add(list, ignored, "note", tv ? "TV" : "Movies", what + " isn't where MyTorrents put it any more (moved, renamed or deleted since).", file, "", "gone|" + file);
					continue;
				}
				if (tv)
				{
					if (tvByFile.TryGetValue(file, out string[] ep))
					{
						List<string> same = tvByEpisode[ep[1] + "|" + ep[2] + "|" + ep[3]].Where(x => !string.Equals(x, file, StringComparison.OrdinalIgnoreCase) && SafeExists(x)).ToList();
						if (same.Count > 0)
							Add(list, ignored, "fix", "TV", what + " is there twice - you also have " + Path.GetFileName(same[0]) + ". This one was added by MyTorrents.", file, "dupe", "dupe|" + file);
						else tvOk++;
					}
					else if (DateTime.Now - c.Item1 > TimeSpan.FromHours(2))
						Add(list, ignored, "fix", "TV", what + " isn't in MP-TVSeries yet (copied " + c.Item1.ToString("ddd d MMM h:mm tt") + ").", file, "import", "import|" + file);
				}
				else
				{
					bool inLibrary = movieFiles.Contains(file);
					if (!inLibrary && DateTime.Now - c.Item1 > TimeSpan.FromHours(6))
						Add(list, ignored, "note", "Movies", what + " isn't in Moving Pictures yet - is its folder one Moving Pictures scans?", file, "open", "mp|" + file);
					string title;
					int year;
					long size = SafeSize(file);
					string quality = Naming.QualityOf(Path.GetFileName(file) + " " + c.Item2);
					string why = PoorCopies.Why(size, quality.Length > 0 ? quality : Films.Quality, Films.IsHevc(Path.GetFileName(file) + " " + c.Item2), 0);
					if (why.Length > 0 && why.StartsWith("only") && MediaNames.TryMovie(Path.GetFileName(file), out title, out year))
						Add(list, ignored, "fix", "Movies", what + " is a poor copy (" + why + ").", file, "better", "poor|" + file, title, year);
					else if (inLibrary) movieOk++;
				}
			}
			if (tvOk > 0) list.Insert(0, new Finding { Level = "ok", Area = "TV", Text = tvOk + " episodes filed in the last " + Days + " days are in place and in MP-TVSeries." });
			if (movieOk > 0) list.Insert(0, new Finding { Level = "ok", Area = "Movies", Text = movieOk + " movies filed in the last " + Days + " days are in place and in Moving Pictures." });
			if (copies.Count == 0) list.Add(new Finding { Level = "ok", Area = "Downloads", Text = "Nothing was filed in the last " + Days + " days." });

			// ---- 2026 (1.3.52): sample clips and the sites' advert videos left in the library folders
			try
			{
				foreach (string row in LibraryFolders.Read("movingpictures.db3", "select ifnull(fullpath,'') || char(9) || ifnull(filesize,0) from local_media")
					.Select(r => "Movies\t" + r)
					.Concat(LibraryFolders.Read("TVSeriesDatabase4.db3", "select ifnull(EpisodeFilename,'') || char(9) || '-1' from local_episodes").Select(r => "TV\t" + r)))
				{
					string[] p = row.Split('\t');
					if (p.Length < 3 || p[1].Length == 0) continue;
					long size;
					long.TryParse(p[2], out size);
					if (size < 0)
					{
						// MP-TVSeries doesn't keep sizes - only look at the file when the name already fits
						if (!IsLeftover(p[1], 1)) continue;
						size = SafeSize(p[1]);
					}
					if (!IsLeftover(p[1], size)) continue;
					Add(list, ignored, "fix", p[0], Path.GetFileName(p[1]) + " is a sample / advert video (" + Math.Max(1, size / 1024 / 1024) + " MB) - not part of the " + (p[0] == "TV" ? "episode" : "movie") + ".", p[1], "leftover", "left|" + p[1]);
				}
			}
			catch
			{
			}

			// ---- the drop folder: things that couldn't be filed
			foreach (var kv in DropFolder.Left())
				Add(list, ignored, "fix", "Drop folder", Path.GetFileName(kv.Key) + " is still in the drop folder - " + kv.Value + ".", kv.Key,
					kv.Value.StartsWith(DropFolder.HaveItAll) ? "dropdelete" : "open", "drop|" + kv.Key); // 1.3.62

			// ---- movie names (only when you've chosen a naming style)
			try
			{
				if ((Configuration.GetExtra("Films.NamePattern", "") ?? "").Length > 0)
				{
					int n = MovieRenamer.Proposals().Count;
					if (n > 0) Add(list, ignored, "note", "Movies", n + " movies aren't named the way you chose.", "", "rename", "rename|" + n);
				}
			}
			catch
			{
			}

			// ---- can't find
			try
			{
				int n = CantFind.All().Count(x => x.State != "skip");
				if (n > 0) list.Add(new Finding { Level = "note", Area = "Downloads", Text = n + " episodes / movies couldn't be found for " + CantFind.Days + "+ days (see Can't find).", Key = "cantfind" });
			}
			catch
			{
			}

			// ---- search sites that keep timing out
			try
			{
				foreach (var s in WebServer.SiteStats().Where(x => x.Value[2] >= 20 && x.Value[1] > x.Value[2] / 2))
					Add(list, ignored, "note", "Search sites", s.Key + " timed out in " + s.Value[1] + " of its last " + s.Value[2] + " searches - it may be worth removing (Search sites tab).", "", "", "site|" + s.Key);
			}
			catch
			{
			}

			// ---- the NAS / library drives filling up
			foreach (string root in tvRoots.Concat(LibraryFolders.Movies().Select(r => r.TrimEnd('\\') + "\\")).Distinct(StringComparer.OrdinalIgnoreCase))
			{
				ulong free, total, dummy;
				if (!GetDiskFreeSpaceEx(root, out free, out total, out dummy) || total == 0) continue;
				double pct = 100.0 * free / total;
				if (pct < 5) Add(list, ignored, "fix", "NAS", root + " is nearly full - " + (free / 1024 / 1024 / 1024) + " GB left.", root, "open", "full|" + root + "|" + DateTime.Now.ToString("yyyy-MM-dd"));
			}

			// ---- errors in the log today
			try
			{
				int errors = LogLines(DateTime.Today).Count(l => Regex.IsMatch(l, @"\b(could not|couldn't|failed|error)\b", RegexOptions.IgnoreCase));
				if (errors > 20) list.Add(new Finding { Level = "note", Area = "Log", Text = errors + " problems written to MyTorrents.log today - send the log to Claude if something seems wrong.", Key = "log" });
			}
			catch
			{
			}

			if (!list.Any(f => f.Level == "fix")) list.Insert(0, new Finding { Level = "ok", Area = "", Text = "Nothing needs you." });
			Write(list);
			Log.Instance().Print("Check-up: " + list.Count(f => f.Level == "fix") + " things need you, " + list.Count(f => f.Level == "note") + " notes");
			return list;
		}

		private static void Add(List<Finding> list, HashSet<string> ignored, string level, string area, string text, string file, string action, string key, string title = "", int year = 0)
		{
			if (ignored.Contains(key)) return;
			list.Add(new Finding { Level = level, Area = area, Text = text, File = file, Action = action, Key = key, Title = title, Year = year });
		}

		// 2026 (1.3.57): the movie renamer's names differ more than MovieKey allowed for - "48 Hrs. I (1982) [...]",
		// "Sinbad I, 7th Voyage of (1958)", "Alone in the Dark II (2008)" (was "Alone In The Dark 2") - so films are
		// matched by year and by most of their title words, in any order, leaving out "the", "a", numbers 1-9 and I-X
		private static readonly HashSet<string> SkipWords = new HashSet<string> { "the", "a", "an", "i", "ii", "iii", "iv", "v", "vi", "vii", "viii", "ix", "x", "1", "2", "3", "4", "5", "6", "7", "8", "9" };

		internal static bool TitleWords(string path, out string year, out HashSet<string> words)
		{
			year = null;
			words = null;
			if (string.IsNullOrEmpty(path)) return false;
			foreach (string part in new[] { Path.GetFileNameWithoutExtension(path), Path.GetFileName(Path.GetDirectoryName(path) ?? "") })
			{
				Match m = Regex.Match(part ?? "", @"^(?:\[\d+\]\s*)?(.+?)\s*\((\d{4})\)");
				if (!m.Success) continue;
				string t = Regex.Replace(m.Groups[1].Value.ToLowerInvariant(), @"['\u2019]", "").Replace("&", " and ");
				HashSet<string> w = new HashSet<string>(Regex.Split(t, @"[^a-z0-9]+").Where(x => x.Length > 0 && !SkipWords.Contains(x)));
				if (w.Count == 0) continue;
				year = m.Groups[2].Value;
				words = w;
				return true;
			}
			return false;
		}

		private static Dictionary<string, List<HashSet<string>>> MovieWordIndex(IEnumerable<string> files)
		{
			Dictionary<string, List<HashSet<string>>> index = new Dictionary<string, List<HashSet<string>>>();
			foreach (string f in files)
			{
				string y;
				HashSet<string> w;
				if (!TitleWords(f, out y, out w)) continue;
				if (!index.ContainsKey(y)) index[y] = new List<HashSet<string>>();
				index[y].Add(w);
			}
			return index;
		}

		private static bool SameFilmInLibrary(string file, Dictionary<string, List<HashSet<string>>> index)
		{
			string y;
			HashSet<string> w;
			if (!TitleWords(file, out y, out w)) return false;
			// 1.3.59: a year either side too - Media Center Master often renames with the local release year
			// ("City of God (2002)" -> "City of God (2003)")
			int year;
			IEnumerable<string> years = int.TryParse(y, out year) ? new[] { y, (year - 1).ToString(), (year + 1).ToString() } : new[] { y };
			foreach (string yy in years)
			{
				if (!index.TryGetValue(yy, out List<HashSet<string>> same)) continue;
				foreach (HashSet<string> other in same)
				{
					int small = Math.Min(w.Count, other.Count), both = w.Count(other.Contains);
					if (small == 1 ? (both == 1 && w.Count == other.Count) : both >= Math.Ceiling(small * 0.75)) return true;
				}
			}
			return false;
		}

		/// 2026 (1.3.56): "black shield of falworth|1954" from a movie's file or folder name - the same film whether it is
		/// "The Black Shield of Falworth (1954).mkv" or "Black Shield of Falworth, The (1954) [6.4][...]\..."
		internal static string MovieKey(string path)
		{
			if (string.IsNullOrEmpty(path)) return "";
			foreach (string part in new[] { Path.GetFileNameWithoutExtension(path), Path.GetFileName(Path.GetDirectoryName(path) ?? "") })
			{
				Match m = Regex.Match(part ?? "", @"^(?:\[\d+\]\s*)?(.+?)\s*\((\d{4})\)");
				if (!m.Success) continue;
				string t = m.Groups[1].Value.ToLowerInvariant();
				t = Regex.Replace(t, @",\s*(the|a|an)$", "");
				t = Regex.Replace(t, @"^(the|a|an)\s+", "");
				t = Regex.Replace(t, @"[^a-z0-9]+", "");
				if (t.Length > 0) return t + "|" + m.Groups[2].Value;
			}
			return "";
		}

		private static bool SafeExists(string f)
		{
			try
			{
				return System.IO.File.Exists(f);
			}
			catch
			{
				return false;
			}
		}

		private static long SafeSize(string f)
		{
			try
			{
				return new FileInfo(f).Length;
			}
			catch
			{
				return 0;
			}
		}

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern bool GetDiskFreeSpaceEx(string directory, out ulong freeForUser, out ulong total, out ulong totalFree);

		// ------------------------------------------------------------------ the log

		private static readonly Regex LineDate = new Regex(@"^\[([^\]]+?)\s+([0-9]{1,4}[./-][0-9]{1,2}[./-][0-9]{1,4})\]\s");

		private static IEnumerable<string> LogLines(DateTime since)
		{
			foreach (string log in Log.Files()) // 2026 (1.3.55): one file per day now
			{
			if (System.IO.File.GetLastWriteTime(log) < since) continue;
			using (FileStream fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			{
				if (fs.Length > 8 * 1024 * 1024) fs.Seek(-8 * 1024 * 1024, SeekOrigin.End);
				using (StreamReader sr = new StreamReader(fs))
				{
					string line;
					while ((line = sr.ReadLine()) != null)
					{
						DateTime when;
						if (When(line, out when) && when >= since) yield return line;
					}
				}
			}
			}
		}

		private static bool When(string line, out DateTime when)
		{
			when = DateTime.MinValue;
			Match m = LineDate.Match(line ?? "");
			return m.Success && DateTime.TryParse(m.Groups[2].Value + " " + m.Groups[1].Value, CultureInfo.CurrentCulture, DateTimeStyles.None, out when);
		}

		private static readonly Regex CopyLine = new Regex(@"Finished downloads: copying (.+?) \([\d.,]+ [KMGT]?B\) to (.+)$");

		/// What MyTorrents copied into the library in the last few days (when, copy's name, where to).
		private static List<Tuple<DateTime, string, string>> Copies()
		{
			List<Tuple<DateTime, string, string>> list = new List<Tuple<DateTime, string, string>>();
			foreach (string line in LogLines(DateTime.Now.AddDays(-Days)))
			{
				Match m = CopyLine.Match(line);
				if (!m.Success) continue;
				string dest = m.Groups[2].Value.Trim();
				if (dest.EndsWith(".srt", StringComparison.OrdinalIgnoreCase) || dest.EndsWith(".sub", StringComparison.OrdinalIgnoreCase) || dest.EndsWith(".idx", StringComparison.OrdinalIgnoreCase)) continue;
				DateTime when;
				When(line, out when);
				list.Add(Tuple.Create(when, m.Groups[1].Value, dest));
			}
			return list;
		}

		// ------------------------------------------------------------------ fixing

		/// Deletes the extra copy MyTorrents added (and its subtitles with the same name). "" = done.
		public static string DeleteExtra(Finding f)
		{
			try
			{
				if (f.Action != "dupe" || !System.IO.File.Exists(f.File)) return "";
				string dir = Path.GetDirectoryName(f.File), b = Path.GetFileNameWithoutExtension(f.File);
				foreach (string side in Directory.GetFiles(dir, b + ".*")) System.IO.File.Delete(side);
				Log.Instance().Print("Check-up: deleted the extra copy " + f.File);
				Done(f);
				return "";
			}
			catch (Exception ex)
			{
				return ex.Message;
			}
		}

		// (not "trailer" - trailers kept on purpose in an extras folder are left alone; not words that turn up in
		// episode titles like "The Visit" or "stage3.torrent")
		private static readonly Regex Leftover = new Regex(@"(^|[\W_])(sample|promo)([\W_]|$)|rarbg|\byts\b|yify|\bwww\.|\.com\b|\bcom$|\betrg\b|\bettv\b|\beztv\b|downloaded from", RegexOptions.IgnoreCase | RegexOptions.Compiled);

		/// A sample clip or a site's advert video: such a name AND small (under 100 MB) - a real film or episode never is both.
		public static bool IsLeftover(string file, long size)
		{
			string n = Path.GetFileNameWithoutExtension(file ?? "");
			string ext = Path.GetExtension(file ?? "").ToLowerInvariant();
			if (ext != ".mkv" && ext != ".mp4" && ext != ".avi" && ext != ".m4v" && ext != ".wmv" && ext != ".ts") return false;
			if (size <= 0 || size >= 100L * 1024 * 1024) return false;
			// the part of the name after the film's own name (e.g. "Film (1954) [...] - RARBG COM") or the whole name
			// an episode (S01E02 in the name) only when it says "sample" - its title could be anything
			string sh, cd;
			int se;
			if (MediaNames.TryEpisode(n, out sh, out se, out cd)) return Regex.IsMatch(n, @"(^|[\W_])sample([\W_]|$)", RegexOptions.IgnoreCase);
			int dash = n.LastIndexOf(" - ", StringComparison.Ordinal);
			string tail = dash > 0 ? n.Substring(dash + 3) : n;
			return Leftover.IsMatch(tail) || Regex.IsMatch(n, @"^sample$|-sample$|\bsample\b", RegexOptions.IgnoreCase) ||
				(file ?? "").IndexOf("\\sample\\", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		/// Deletes a sample / advert video found by the check-up (only if it still is one). "" = done.
		public static string DeleteLeftover(Finding f)
		{
			try
			{
				if (f.Action != "leftover" || !System.IO.File.Exists(f.File)) { Done(f); return ""; }
				long size = new FileInfo(f.File).Length;
				if (!IsLeftover(f.File, size)) return Path.GetFileName(f.File) + " doesn't look like a sample any more - left alone";
				System.IO.File.Delete(f.File);
				Log.Instance().Print("Check-up: deleted the sample / advert video " + f.File);
				Done(f);
				return "";
			}
			catch (Exception ex)
			{
				return ex.Message;
			}
		}

		/// Asks MP-TVSeries to import the episode (inside MediaPortal straight away; from Configuration at MediaPortal's next round).
		public static void ImportAgain(Finding f)
		{
			if (MyTorrents.InMediaPortal) MovieFiling.ImportTv(new List<string> { f.File });
			else GrabRequests.AddImport(f.File);
			Done(f);
		}

		/// Taken off the current list (the next check-up looks again).
		public static void Done(Finding f)
		{
			List<Finding> list = Read();
			list.RemoveAll(x => x.Key == f.Key);
			Write(list);
		}
	}
}
