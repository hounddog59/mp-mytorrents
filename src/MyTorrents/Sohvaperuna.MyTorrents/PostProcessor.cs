using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Xml;
using MediaPortal.Configuration;
using MediaPortal.Dialogs;
using MediaPortal.GUI.Library;
using Sohvaperuna.TorrentEngine;
using Sohvaperuna.TorrentSearch;

namespace Sohvaperuna.MyTorrents
{
	/// <summary>
	/// 2026: when a download finishes, copies its video files (and matching subtitles) into the library,
	/// renamed and sorted:
	///   TV    -> [TV folder]\Show\Season 02\Show - S02E17.mkv     (an existing show / season folder is used)
	///   Films -> [Film folder]\Title (Year)\Title (Year).mkv
	/// The torrent keeps seeding. It is tagged "MyTorrents-copied" in the torrent program and listed in
	/// MyTorrents.finished.xml, so nothing is copied twice. Torrents that were already finished when this
	/// was first switched on are left alone.
	/// </summary>
	public static class PostProcessor
	{
		public const string CopiedTag = "MyTorrents-copied";

		private static readonly string[] VideoExt = { ".mkv", ".mp4", ".avi", ".m4v", ".ts", ".m2ts", ".wmv", ".mov", ".mpg", ".mpeg" };
		private static readonly string[] SubtitleExt = { ".srt", ".sub", ".idx", ".ass", ".ssa" };
		private static readonly object _lock = new object();
		private static readonly HashSet<string> _triedThisSession = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private static bool _busy;

		/// 2026 (1.3.53): downloads to use before they reach 100% (key = hash, value = how much of each file must be there)
		private static readonly Dictionary<string, double> _early = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
		/// ones that were tried early and did not look whole - wait for them to finish properly
		private static readonly HashSet<string> _earlyFailed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		/// 2026 (1.3.53): a download stuck at 99% or more with no seeds for this many hours is checked and, if the
		/// video looks whole, copied to the library as if it had finished ("Finished.NearlyDoneHours", 6; 0 = off)
		public static int NearlyDoneHours
		{
			get
			{
				int h;
				return int.TryParse(Configuration.GetExtra("Finished.NearlyDoneHours", "6"), out h) && h >= 0 ? h : 6;
			}
		}

		public static readonly int[] NearlyDoneChoices = { 0, 3, 6, 12, 24 };

		public static string NearlyDoneText(int h) => h <= 0 ? "Off - wait for 100%" : "After " + h + " hours stuck at 99%+";

		/// 2026 (1.3.53): "Use it now" from the Downloads list - for a download 95% or more done. Null when it is
		/// started, otherwise why not.
		public static string UseNow(ITorrentClient session, Torrent t)
		{
			if (session == null || t == null || string.IsNullOrEmpty(t.Hash)) return "nothing picked";
			if (t.Progress >= 100.0) return "it is already finished";
			if (t.Progress < 95.0) return "it is only " + t.Progress.ToString("0.#") + "% done - at least 95% is needed";
			if (WasCopied(t.Hash))
			{
				// 1.3.63: copied earlier, but that copy isn't there any more (moved, renamed or deleted since) - copy it again
				if (!CopiedFileGone(t.Hash)) return "it has already been copied";
				Log.Instance().Print("Finished downloads: [" + t.Name + "] the copy made earlier isn't there any more - copying it again");
			}
			lock (_lock)
			{
				_early[t.Hash] = 0.95;
				_earlyFailed.Remove(t.Hash);
				_triedThisSession.Add(t.Hash);
			}
			ThreadPool.QueueUserWorkItem(_ => Process(session, t));
			return null;
		}
		private static bool _started;

		private static string StoreFile => Config.GetFile(Config.Dir.Config, "MyTorrents.finished.xml");

		// ---------------------------------------------------------------- settings

		public static bool Enabled => Configuration.GetExtra("Finished.Copy", "true") != "false";

		public static bool Sort => Configuration.GetExtra("Finished.Sort", "true") != "false";

		/// 2026 (1.3.38): remove copied torrents (and their leftover download) once they have stopped seeding.
		public static bool CleanUp => Configuration.GetExtra("Finished.CleanUp", "true") != "false";

		private static DateTime _lastCleanUp = DateTime.MinValue;

		/// 2026 (1.3.38): a copied torrent that has stopped seeding (reached the ratio / time limit) is removed from the
		/// torrent program. Its files are deleted too - but only when they are outside every library folder (TV, films,
		/// MP-TVSeries and Moving Pictures import folders); a torrent saved straight into the library keeps its files.
		private static void CleanUpFinished(ITorrentClient session, TorrentList all)
		{
			if (!CleanUp || DateTime.Now - _lastCleanUp < TimeSpan.FromMinutes(10)) return;
			_lastCleanUp = DateTime.Now;
			List<Torrent> done = all.Where(t => !string.IsNullOrEmpty(t.Hash)
				&& (t.Tags ?? "").IndexOf(CopiedTag, StringComparison.OrdinalIgnoreCase) >= 0
				&& t.Progress >= 100.0
				&& !t.HasStatus(ClientSupport.Started) && !t.HasStatus(ClientSupport.Error) && !t.HasStatus(ClientSupport.Checking)).ToList();
			if (done.Count == 0) return;
			List<string> library = new List<string> { TVFolder, MovieFolder };
			library.AddRange(LibraryFolders.TV());
			library.AddRange(LibraryFolders.Movies());
			library = library.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim().Replace('/', '\\').TrimEnd('\\') + "\\").ToList();
			foreach (Torrent t in done)
			{
				try
				{
					string path = (t.TorrentSavePath ?? "").Replace('/', '\\').TrimEnd('\\') + "\\";
					bool inLibrary = path.Length <= 1 || library.Any(l => path.StartsWith(l, StringComparison.OrdinalIgnoreCase) || l.StartsWith(path, StringComparison.OrdinalIgnoreCase));
					bool ok = session.Remove(t.Hash, !inLibrary);
					Log.Instance().Print("Clean up: " + t.Name + (ok ? (inLibrary ? " - removed from the torrent program (its files are in your library, kept)" : " - finished seeding, removed with its leftover download") : " - could not be removed"));
				}
				catch (Exception ex)
				{
					Log.Instance().Print("Clean up: " + t.Name + ": " + ex.Message);
				}
			}
		}

		public static string TVFolder
		{
			get
			{
				string f = Configuration.GetExtra("Finished.TVFolder", "");
				return f.Length > 0 ? f : LibraryFolders.DefaultTV();
			}
		}

		public static string MovieFolder
		{
			get
			{
				string f = Configuration.GetExtra("Finished.MovieFolder", "");
				return f.Length > 0 ? f : LibraryFolders.DefaultMovies();
			}
		}

		// ---------------------------------------------------------------- start / remember

		public static void Start()
		{
			if (_started) return;
			_started = true;
			TorrentEngine.Instance().TorrentsUpdated += Check;
			Log.Instance().Print($"Finished downloads: {(Enabled ? "copy to library" : "copying is off")} (TV: {TVFolder}, movies: {MovieFolder})");
		}

		/// Called when a download is started from a search, so we know whether it is TV or a film.
		public static void Remember(string hash, string kind, string searchText)
		{
			Remember(hash, kind, searchText, null);
		}

		private static readonly Dictionary<string, string> Titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		/// 2026: while qBittorrent is still fetching a magnet link's details it only knows the torrent's ID code -
		/// show the name from the search instead.
		public static string DisplayName(Torrent t)
		{
			if (t == null) return "";
			string name = t.Name ?? "";
			if (name.Length > 0 && !name.Equals(t.Hash ?? "", StringComparison.OrdinalIgnoreCase)) return name;
			string title;
			lock (Titles)
			{
				if (Titles.Count == 0) LoadTitles();
				if (Titles.TryGetValue(t.Hash ?? "", out title) && title.Length > 0) return title + "  (getting details...)";
			}
			return name.Length > 0 ? name : (t.Hash ?? "");
		}

		private static void LoadTitles()
		{
			try
			{
				lock (_lock)
				{
					foreach (XmlElement e in LoadStore().DocumentElement.ChildNodes.OfType<XmlElement>())
					{
						string h = e.GetAttribute("hash"), ti = e.GetAttribute("title");
						if (h.Length > 0 && ti.Length > 0) Titles[h] = ti;
					}
				}
			}
			catch
			{
			}
			if (Titles.Count == 0) Titles[""] = ""; // do not read the file again every refresh
		}

		public static void Remember(string hash, string kind, string searchText, string title)
		{
			if (!string.IsNullOrEmpty(hash) && !string.IsNullOrEmpty(title))
			{
				lock (Titles)
				{
					Titles[hash] = title;
				}
			}
			if (string.IsNullOrEmpty(hash)) return;
			try
			{
				string show = "";
				if (kind == SearchContext.TV)
				{
					string s, code;
					int season;
					show = MediaNames.TryEpisode(searchText, out s, out season, out code) ? s : MediaNames.Clean(searchText ?? "");
				}
				lock (_lock)
				{
					XmlDocument doc = LoadStore();
					XmlElement t = Find(doc, hash) ?? Add(doc, hash);
					t.SetAttribute("kind", kind ?? "");
					if (show.Length > 0) t.SetAttribute("show", show);
					if (!string.IsNullOrEmpty(title)) t.SetAttribute("title", title);
					if (t.GetAttribute("state") == "") t.SetAttribute("state", "downloading");
					doc.Save(StoreFile);
				}
			}
			catch (Exception ex)
			{
				Log.Instance().Print("Finished downloads: could not remember " + hash + ": " + ex.Message);
			}
		}

		// ---------------------------------------------------------------- checking

		private static void Check()
		{
			if (WatchList.Paused) return; // 2026 (1.3.45): the helper leaves it to MediaPortal while that runs
			if (!Enabled) return;
			DropFolder.Check(); // 2026 (1.3.50): movies / episodes you put in the drop folder
			if (_busy) return;
			try
			{
				ITorrentClient session = TorrentEngine.Instance().TorrentSession;
				TorrentList all = session?.TorrentsAll;
				if (all == null) return;
				try
				{
					CleanUpFinished(session, all);
				}
				catch (Exception ex)
				{
					Log.Instance().Print("Clean up: " + ex.Message);
				}
				List<Torrent> todo = new List<Torrent>();
				lock (_lock)
				{
					XmlDocument doc = LoadStore();
					XmlElement root = doc.DocumentElement;
					if (root.GetAttribute("baseline") != "done")
					{
						// first time: everything already finished counts as done
						foreach (Torrent t in all.Where(x => x.Progress >= 100.0))
						{
							XmlElement e = Find(doc, t.Hash) ?? Add(doc, t.Hash);
							if (e.GetAttribute("state") == "" || e.GetAttribute("state") == "downloading") e.SetAttribute("state", "before");
						}
						root.SetAttribute("baseline", "done");
						doc.Save(StoreFile);
						Log.Instance().Print("Finished downloads: " + all.Count(x => x.Progress >= 100.0) + " torrent(s) were already finished - left as they are");
						return;
					}
					foreach (Torrent t in all)
					{
						if (string.IsNullOrEmpty(t.Hash) || _triedThisSession.Contains(t.Hash)) continue;
						if (t.Progress < 100.0)
						{
							// 2026 (1.3.53): stuck at 99%+ with no seeds for hours - it will usually play fine, use it
							if (NearlyDoneHours <= 0 || t.Progress < 99.0 || _earlyFailed.Contains(t.Hash) || t.SeedsConnected > 0 || t.DownloadSpeed > 0
								|| Grabbed.HoursSinceData(t) < NearlyDoneHours) continue;
							_early[t.Hash] = 0.99;
						}
						if ((t.Tags ?? "").IndexOf(CopiedTag, StringComparison.OrdinalIgnoreCase) >= 0) continue;
						string state = Find(doc, t.Hash)?.GetAttribute("state") ?? "";
						if (state == "copied" || state == "skipped" || state == "before") continue;
						todo.Add(t);
					}
				}
				if (todo.Count == 0) return;
				_busy = true;
				foreach (Torrent t in todo) _triedThisSession.Add(t.Hash);
				ThreadPool.QueueUserWorkItem(_ =>
				{
					try
					{
						foreach (Torrent t in todo) Process(session, t);
					}
					finally
					{
						_busy = false;
					}
				});
			}
			catch (Exception ex)
			{
				Log.Instance().Print("Finished downloads: check failed: " + ex.Message);
			}
		}

		// ---------------------------------------------------------------- copying

		private static void Process(ITorrentClient session, Torrent torrent)
		{
			string kind = "", show = "";
			lock (_lock)
			{
				XmlElement e = Find(LoadStore(), torrent.Hash);
				if (e != null)
				{
					kind = e.GetAttribute("kind");
					show = e.GetAttribute("show");
				}
			}
			try
			{
				double early;
				lock (_lock) { if (!_early.TryGetValue(torrent.Hash ?? "", out early)) early = 0; }
				List<string> videos = VideoFiles(session, torrent, early);
				if (early > 0)
				{
					// 2026 (1.3.53): not finished - only use it if the video looks whole (start and end are there)
					string why = null;
					List<string> whole = videos.Where(v => { string w = WhyNotWhole(v); if (w != null && why == null) why = Path.GetFileName(v) + ": " + w; return w == null; }).ToList();
					if (whole.Count == 0 || whole.Count < videos.Count)
					{
						lock (_lock)
						{
							_early.Remove(torrent.Hash);
							_earlyFailed.Add(torrent.Hash);
							_triedThisSession.Remove(torrent.Hash); // so it is still copied if it does finish
						}
						Log.Instance().Print("Finished downloads: [" + torrent.Name + "] is " + torrent.Progress.ToString("0.0") + "% done but could not be used yet - "
							+ (why ?? "no video file far enough along") + ". It will be copied if it finishes.");
						Popup("Not usable yet", torrent.Name + " - " + (why ?? "the video is not far enough along"));
						return;
					}
				}
				if (videos.Count == 0)
				{
					Done(torrent, "skipped", "no video files in it", null);
					return;
				}
				if (kind != SearchContext.TV && kind != SearchContext.Movie)
				{
					kind = Guess(torrent.Name, videos);
				}
				if (kind == "")
				{
					Done(torrent, "skipped", "could not tell whether it is TV or a movie - copy it yourself", null);
					return;
				}
				string root = kind == SearchContext.TV ? TVFolder : MovieFolder;
				if (string.IsNullOrEmpty(root))
				{
					Done(torrent, "failed", "no " + (kind == SearchContext.TV ? "TV" : "movie") + " folder set (MyTorrents Configuration > When finished)", null);
					return;
				}
				if (!Directory.Exists(root))
				{
					Done(torrent, "failed", "cannot reach " + root + " - is the NAS on? Tried again next time MediaPortal starts", null);
					return;
				}
				List<string> copied = FileVideos(root, kind, show, torrent.Name, videos, false);
				if (copied.Count == 0)
				{
					Done(torrent, "skipped", "could not work out the names - copy it yourself", null);
					return;
				}
				try
				{
					(session as ITorrentTagging)?.AddTag(torrent.Hash, CopiedTag);
				}
				catch (Exception ex)
				{
					Log.Instance().Print("Finished downloads: could not tag the torrent: " + ex.Message);
				}
				Done(torrent, "copied", (early > 0 ? "used at " + torrent.Progress.ToString("0.0") + "% (no seeds for the last bit, the video looked whole) - " : "")
					+ "copied to " + string.Join(", ", copied), copied[0]);
				if (kind == SearchContext.Movie) RescanMovingPictures();
				else Grabbed.ReleaseMissing(torrent.Hash, copied); // 2026 (1.3.66): a "season pack" that was short of episodes
				Popup(early > 0 ? "Nearly finished - copied anyway" : "Copied to your library", Path.GetFileNameWithoutExtension(copied[0]) + (copied.Count > 1 ? $" (+{copied.Count - 1} more)" : ""));
			}
			catch (Exception ex)
			{
				Done(torrent, "failed", ex.Message + " - tried again next time MediaPortal starts", null);
				Popup("Could not copy download", torrent.Name);
			}
		}

		/// 2026 (1.3.50): puts the videos of one download (or one thing in the drop folder) into the library - named,
		/// sorted, .nfo, MP-TVSeries told. move = take them out of where they are (the drop folder). The files now in
		/// the library (or already there).
		internal static List<string> FileVideos(string root, string kind, string show, string name, List<string> videos, bool move)
		{
				List<string> copied = new List<string>();
			List<string> newTv = new List<string>(); // 2026 (1.3.44): for MP-TVSeries' importer
			// 2026 (1.3.44): a movie - look it up once (TMDb through MP-TVSeries, or Trakt) for its ids and collection
			MovieFiling.Info movieInfo = null;
			if (kind == SearchContext.Movie && (MovieFiling.FileForMe || MovieFiling.AskFirst || MovieFiling.WriteNfo))
			{
				string mt = "";
				int my = 0;
				bool ok = MediaNames.TryMovie(name, out mt, out my);
				if (!ok) foreach (string v in videos) if (MediaNames.TryMovie(Path.GetFileName(v), out mt, out my)) { ok = true; break; }
				if (ok)
				{
					movieInfo = MovieFiling.Lookup(mt, my);
					Log.Instance().Print("Finished downloads: " + mt + " (" + my + ") - " + (movieInfo == null ? "not found on TMDb / Trakt" :
						"IMDb " + (movieInfo.ImdbId.Length > 0 ? movieInfo.ImdbId : "?") + (movieInfo.Collection.Length > 0 ? ", part of " + movieInfo.Collection : "")));
				}
			}
			foreach (string video in videos)
			{
				if (video.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
				{
					copied.Add(video); // downloaded straight into the library (Auto grabber folder) - nothing to copy
					continue;
				}
				// 2026 (1.3.41): a whole season - episodes you already have are left out
				// 2026 (1.3.56): the drop folder too, even for one episode - it stays there and the Check-up says so
				if (kind == SearchContext.TV && (videos.Count > 1 || move))
				{
					string vShow, vCode;
					int vSeason;
					if (MediaNames.TryEpisode(Path.GetFileName(video), out vShow, out vSeason, out vCode))
					{
						System.Text.RegularExpressions.Match vm = System.Text.RegularExpressions.Regex.Match(vCode ?? "", @"[Ee](\d+)");
						int vEp;
						if (vm.Success && int.TryParse(vm.Groups[1].Value, out vEp) && LibraryFolders.HaveEpisode(string.IsNullOrEmpty(show) ? vShow : show, vSeason, vEp))
						{
							Log.Instance().Print($"Finished downloads: {Path.GetFileName(video)} - you already have this episode, not copied");
							if (move) SkipNotes?.Add(Path.GetFileName(video) + ": you already have this episode");
							copied.Add(video);
							continue;
						}
					}
				}
				// 2026 (1.3.56): a film dropped in the drop folder that you already have stays there (no second copy)
				if (move && kind == SearchContext.Movie)
				{
					string ft;
					int fy;
					if ((MediaNames.TryMovie(name, out ft, out fy) || MediaNames.TryMovie(Path.GetFileName(video), out ft, out fy)) && LibraryFolders.HaveFilm(ft, fy))
					{
						Log.Instance().Print($"Drop folder: {Path.GetFileName(video)} - you already have {ft} ({fy}), left in the drop folder");
						SkipNotes?.Add(Path.GetFileName(video) + ": you already have " + ft + " (" + fy + ") - delete it, or move it in yourself if this copy is better");
						continue;
					}
				}
				Naming.CurrentInfo = kind == SearchContext.Movie ? movieInfo : null; // 2026 (1.3.49): rating / genres for the name
				string dest = Destination(root, kind, show, name, video, videos.Count);
				Naming.CurrentInfo = null;
				if (kind == SearchContext.Movie) dest = MovieFiling.Place(dest, movieInfo); // 2026 (1.3.44): Movies / Collections
				if (dest == null)
				{
					Log.Instance().Print($"Finished downloads: could not work out a name for {Path.GetFileName(video)} - skipped");
					continue;
				}
				if (move) { if (!MoveFile(video, dest)) continue; } else CopyFile(video, dest);
				copied.Add(dest);
				CopySubtitles(video, dest, move);
				if (kind == SearchContext.TV) newTv.Add(dest);
				else if (kind == SearchContext.Movie)
				{
					MovieFiling.Nfo(dest, movieInfo);
					LibraryFolders.NoteMovieFile(dest);
					if (MovieFiling.AskFirst) MovieFiling.AddWaiting(dest, movieInfo); // 2026 (1.3.45): waits for your OK
				}
			}
			if (kind == SearchContext.TV) MovieFiling.ImportTv(newTv); // 2026 (1.3.44): shows up in TV Series straight away
			return copied;
		}

		/// 2026 (1.3.56): [ThreadStatic] notes on drop-folder files that stayed, and why (DropFolder reads them)
		[ThreadStatic] internal static List<string> SkipNotes;

		/// false when it was left where it is (a different file of that name is already in the library)
		private static bool MoveFile(string source, string dest)
		{
			FileInfo src = new FileInfo(source);
			Directory.CreateDirectory(Path.GetDirectoryName(dest));
			if (System.IO.File.Exists(dest))
			{
				if (new FileInfo(dest).Length == src.Length)
				{
					Log.Instance().Print("Drop folder: already there - " + dest + " (the copy in the drop folder is removed)");
					src.Delete();
					return true;
				}
				// 2026 (1.3.56): never replace what you have with a dropped file - it used to delete the one in the library
				long have = new FileInfo(dest).Length;
				Log.Instance().Print($"Drop folder: {src.Name} - you already have {Path.GetFileName(dest)} ({UnitConvert.SizeToString(have)}, this one {UnitConvert.SizeToString(src.Length)}), left in the drop folder");
				SkipNotes?.Add(src.Name + ": you already have it (" + UnitConvert.SizeToString(have) + ", this one " + UnitConvert.SizeToString(src.Length) + ") - delete it, or swap them yourself if this copy is better");
				return false;
			}
			Log.Instance().Print($"Finished downloads: copying {src.Name} ({UnitConvert.SizeToString(src.Length)}) to {dest}");
			try
			{
				System.IO.File.Move(source, dest); // same drive: instant
			}
			catch (IOException)
			{
				string part = dest + ".mytorrents-part";
				System.IO.File.Copy(source, part, true);
				System.IO.File.Move(part, dest);
				System.IO.File.Delete(source);
			}
			return true;
		}

		private static List<string> VideoFiles(ITorrentClient session, Torrent torrent, double early = 0)
		{
			string save = torrent.TorrentSavePath ?? "";
			List<string> paths = new List<string>();
			List<Sohvaperuna.TorrentEngine.File> files = null;
			try
			{
				files = session.GetFiles(torrent.Hash);
			}
			catch
			{
			}
			if (files != null && files.Count > 0)
			{
				paths.AddRange(files.Where(f => f.Filesize <= 0 || f.Downloaded >= f.Filesize || (early > 0 && f.Downloaded >= f.Filesize * early)).Select(f => Path.Combine(save, (f.Filename ?? "").Replace('/', Path.DirectorySeparatorChar))));
			}
			else
			{
				paths.Add(Path.Combine(save, torrent.Name ?? ""));
			}
			List<string> videos = paths.Where(p => VideoExt.Contains(Path.GetExtension(p).ToLowerInvariant()) && System.IO.File.Exists(p)).ToList();
			// leave out sample clips
			long biggest = videos.Count == 0 ? 0 : videos.Max(p => new FileInfo(p).Length);
			return videos.Where(p =>
			{
				string n = Path.GetFileNameWithoutExtension(p).ToLowerInvariant();
				long size = new FileInfo(p).Length;
				bool sample = System.Text.RegularExpressions.Regex.IsMatch(n, @"(^|[\W_])sample([\W_]|$)") || p.IndexOf("\\sample\\", StringComparison.OrdinalIgnoreCase) >= 0;
				// 2026 (1.3.52): the sites' little advert videos ("RARBG COM.mp4", "www.YTS...") that come with a film -
				// they made a second "part" with a name too long for Windows (The Black Shield of Falworth, 5 Nov)
				bool advert = size < biggest && size < 100L * 1024 * 1024 &&
					(System.Text.RegularExpressions.Regex.IsMatch(n, @"rarbg|\byts\b|yify|www\.|\.com\b|\bcom$|ettv|eztv|torrent|promo|visit|downloaded from") || (biggest > 300L * 1024 * 1024 && size < 30L * 1024 * 1024));
				return !(sample && size < biggest) && !advert;
			}).ToList();
		}

		/// 2026 (1.3.53): quick check that an unfinished video will play - the file is there under its real name,
		/// full length, and its start and end are not empty (missing pieces are left as zeros). Null = looks fine.
		internal static string WhyNotWhole(string path)
		{
			try
			{
				if (!System.IO.File.Exists(path))
					return System.IO.File.Exists(path + ".!qB") ? "qBittorrent is adding .!qB to unfinished files (turn that off in qBittorrent > Downloads)" : "the file is not there";
				using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
				{
					long len = fs.Length;
					if (len < 10L * 1024 * 1024) return "the file is too small";
					byte[] head = new byte[64 * 1024];
					int n = fs.Read(head, 0, head.Length);
					if (n < 16 || head.Take(n).All(b => b == 0)) return "the start of the file is missing";
					string ext = Path.GetExtension(path).ToLowerInvariant();
					if (ext == ".mkv" && !(head[0] == 0x1A && head[1] == 0x45 && head[2] == 0xDF && head[3] == 0xA3)) return "the start of the file is missing";
					if ((ext == ".mp4" || ext == ".m4v") && System.Text.Encoding.ASCII.GetString(head, 4, 4) != "ftyp") return "the start of the file is missing";
					if (ext == ".avi" && System.Text.Encoding.ASCII.GetString(head, 0, 4) != "RIFF") return "the start of the file is missing";
					int tail = (int)Math.Min(1024 * 1024, len);
					byte[] end = new byte[tail];
					fs.Seek(len - tail, SeekOrigin.Begin);
					int got = 0;
					while (got < tail) { int r = fs.Read(end, got, tail - got); if (r <= 0) break; got += r; }
					if (got < tail || end.All(b => b == 0)) return "the end of the file is missing";
				}
				return null;
			}
			catch (Exception ex)
			{
				return "could not read it (" + ex.Message + ")";
			}
		}

		private static string Guess(string torrentName, List<string> videos)
		{
			string s, code;
			int n;
			if (MediaNames.TryEpisode(torrentName, out s, out n, out code) || videos.Any(v => MediaNames.TryEpisode(Path.GetFileName(v), out s, out n, out code)))
				return SearchContext.TV;
			string title;
			if (MediaNames.TryMovie(torrentName, out title, out n) || videos.Any(v => MediaNames.TryMovie(Path.GetFileName(v), out title, out n)))
				return SearchContext.Movie;
			return "";
		}

		/// Where one video file goes. Null when no sensible name can be found.
		public static string Destination(string root, string kind, string knownShow, string torrentName, string video, int videoCount)
		{
			return Destination(root, kind, knownShow, torrentName, video, videoCount, Sort);
		}

		public static string Destination(string root, string kind, string knownShow, string torrentName, string video, int videoCount, bool sort)
		{
			string ext = Path.GetExtension(video).ToLowerInvariant();
			string file = Path.GetFileName(video);
			if (kind == SearchContext.TV)
			{
				string show, code;
				int season;
				if (!MediaNames.TryEpisode(file, out show, out season, out code) && !(videoCount == 1 && MediaNames.TryEpisode(torrentName, out show, out season, out code)))
				{
					return null;
				}
				if (!string.IsNullOrEmpty(knownShow)) show = knownShow;
				if (show.Length == 0 && MediaNames.TryEpisode(torrentName, out string s2, out int n2, out string c2)) show = s2;
				if (show.Length == 0) return null;
				if (!sort)
				{
					string flat = Naming.FileName(Naming.Describe(show, show, season, code, file, torrentName)); // 2026 (1.3.39)
					return Path.Combine(root, (flat ?? MediaNames.SafeName(show + " - " + code)) + ext);
				}
				// 2026 (1.3.38): use the folder your other episodes of this show are already in (MP-TVSeries knows,
				// whichever library folder it is in and whatever the folder is called) - only a show you have no
				// episodes of yet gets a new folder in the TV folder
				string showDir, seasonDir;
				string existing = LibraryFolders.ExistingShowFolder(show, season, out string existingSeason);
				if (existing != null)
				{
					showDir = existing;
					seasonDir = existingSeason;
				}
				else
				{
					showDir = MediaNames.ShowFolder(root, show);
					seasonDir = Naming.SeasonFolderFor(showDir, MediaNames.LibraryShowName(showDir, show), season);
				}
				// name it like the episodes already there ("Apocalypse (2026) S01E04"), otherwise "Show - S01E04"
				// 2026 (1.3.39): your naming choice (When finished tab); "like the episodes already there" = as before
				string named = Naming.FileName(Naming.Describe(show, MediaNames.LibraryShowName(showDir, show), season, code, file, torrentName));
				if (named != null) return Path.Combine(seasonDir, Fit(seasonDir, named, ext) + ext);
				string prefix = MediaNames.EpisodePrefix(seasonDir);
				string fileName = prefix != null ? prefix + code : MediaNames.LibraryShowName(showDir, show) + " - " + code;
				return Path.Combine(seasonDir, Fit(seasonDir, MediaNames.SafeName(fileName), ext) + ext);
			}
			string title;
			int year;
			if (!MediaNames.TryMovie(torrentName, out title, out year) && !MediaNames.TryMovie(file, out title, out year))
			{
				return null;
			}
			// 2026 (1.3.49): your movie naming choice (When finished tab) - "Title (Year)" unless changed
			string name, folderName;
			Naming.MovieNames(title, year, file, torrentName, out name, out folderName);
			if (videoCount > 1)
			{
				string part = MediaNames.SafeName(MediaNames.Clean(Path.GetFileNameWithoutExtension(file))); // e.g. extras, CD1/CD2
				System.Text.RegularExpressions.Match cd = System.Text.RegularExpressions.Regex.Match(part, @"\b(cd|disc|disk|part|pt)\s*\d+\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
				name += " - " + (cd.Success ? cd.Value : part.Length > 40 ? part.Substring(0, 40).Trim() : part);
			}
			string dir = sort ? MediaNames.MovieFolder(root, title, year, folderName) : root;
			// 2026 (1.3.52): Windows can't use a path longer than about 260 letters (with ".mytorrents-part" added
			// while copying) - long names are shortened to fit
			int room = 240 - dir.Length - ext.Length - 1;
			if (name.Length > room && room > 20) name = name.Substring(0, room).TrimEnd(' ', '-', '.', '[', '(');
			return Path.Combine(dir, name + ext);
		}

		/// 2026 (1.3.52): a name short enough for Windows' path limit (about 260 letters, with ".mytorrents-part").
		private static string Fit(string dir, string name, string ext)
		{
			int room = 240 - dir.Length - ext.Length - 1;
			return name.Length > room && room > 20 ? name.Substring(0, room).TrimEnd(' ', '-', '.', '[', '(') : name;
		}

		private static void CopyFile(string source, string dest)
		{
			FileInfo src = new FileInfo(source);
			if (System.IO.File.Exists(dest) && new FileInfo(dest).Length == src.Length)
			{
				Log.Instance().Print("Finished downloads: already there - " + dest);
				return;
			}
			Directory.CreateDirectory(Path.GetDirectoryName(dest));
			string part = dest + ".mytorrents-part";
			Log.Instance().Print($"Finished downloads: copying {src.Name} ({UnitConvert.SizeToString(src.Length)}) to {dest}");
			System.IO.File.Copy(source, part, true);
			if (System.IO.File.Exists(dest)) System.IO.File.Delete(dest);
			System.IO.File.Move(part, dest);
		}

		private static void CopySubtitles(string video, string dest) => CopySubtitles(video, dest, false);

		private static void CopySubtitles(string video, string dest, bool move)
		{
			try
			{
				string dir = Path.GetDirectoryName(video);
				string baseName = Path.GetFileNameWithoutExtension(video);
				string destBase = Path.Combine(Path.GetDirectoryName(dest), Path.GetFileNameWithoutExtension(dest));
				foreach (string sub in Directory.GetFiles(dir).Where(f => SubtitleExt.Contains(Path.GetExtension(f).ToLowerInvariant())))
				{
					string n = Path.GetFileNameWithoutExtension(sub);
					if (!n.StartsWith(baseName, StringComparison.OrdinalIgnoreCase)) continue;
					string rest = n.Substring(baseName.Length); // e.g. ".en"
					if (move) MoveFile(sub, destBase + rest + Path.GetExtension(sub).ToLowerInvariant());
					else CopyFile(sub, destBase + rest + Path.GetExtension(sub).ToLowerInvariant());
				}
			}
			catch (Exception ex)
			{
				Log.Instance().Print("Finished downloads: subtitles not copied: " + ex.Message);
			}
		}

		/// Asks Moving Pictures to look for new films now (if it is installed).
		private static void RescanMovingPictures()
		{
			try
			{
				Type core = AppDomain.CurrentDomain.GetAssemblies()
					.Select(a => a.GetType("MediaPortal.Plugins.MovingPictures.MovingPicturesCore", false))
					.FirstOrDefault(t => t != null);
				object importer = core?.GetProperty("Importer", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
				importer?.GetType().GetMethod("RestartScanner", Type.EmptyTypes)?.Invoke(importer, null);
				LibraryFolders.MoviesChanged();
				if (importer != null) Log.Instance().Print("Finished downloads: asked Moving Pictures to scan for new movies");
			}
			catch (Exception ex)
			{
				Log.Instance().Print("Finished downloads: Moving Pictures scan not started: " + ex.Message);
			}
		}

		private static void Done(Torrent torrent, string state, string message, string dest)
		{
			Log.Instance().Print($"Finished downloads: [{torrent.Name}] {state}: {message}");
			try
			{
				lock (_lock)
				{
					XmlDocument doc = LoadStore();
					XmlElement e = Find(doc, torrent.Hash) ?? Add(doc, torrent.Hash);
					e.SetAttribute("name", torrent.Name ?? "");
					e.SetAttribute("state", state);
					e.SetAttribute("note", message ?? "");
					e.SetAttribute("at", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
					if (dest != null) e.SetAttribute("dest", dest);
					doc.Save(StoreFile);
				}
			}
			catch (Exception ex)
			{
				Log.Instance().Print("Finished downloads: could not save MyTorrents.finished.xml: " + ex.Message);
			}
		}

		private static void Popup(string heading, string text)
		{
			if (!MyTorrents.InMediaPortal) return; // 2026 (1.3.45): the background helper has no screen
			try
			{
				GUIWindowManager.SendThreadCallback((p1, p2, data) =>
				{
					if (GUIGraphicsContext.IsFullScreenVideo) return 0;
					GUIDialogNotify dlg = (GUIDialogNotify)GUIWindowManager.GetWindow(2016);
					if (dlg == null) return 0;
					dlg.Reset();
					dlg.SetHeading(heading);
					dlg.SetText(text);
					dlg.TimeOut = 10;
					dlg.DoModal(GUIWindowManager.ActiveWindow);
					Menus.Check(dlg);
					return 0;
				}, 0, 0, null);
			}
			catch
			{
			}
		}

		// ---------------------------------------------------------------- store

		private static XmlDocument LoadStore()
		{
			XmlDocument doc = new XmlDocument();
			try
			{
				if (System.IO.File.Exists(StoreFile)) doc.Load(StoreFile);
			}
			catch
			{
				doc = new XmlDocument();
			}
			if (doc.DocumentElement == null) doc.AppendChild(doc.CreateElement("finished"));
			return doc;
		}

		/// 2026 (1.3.51): the download started for this copy's name (movies grabbed before 1.3.50 didn't keep it).
		internal static string HashForTitle(string kind, string title)
		{
			if (string.IsNullOrEmpty(title)) return "";
			lock (_lock)
			{
				foreach (XmlElement t in LoadStore().DocumentElement?.ChildNodes.OfType<XmlElement>() ?? Enumerable.Empty<XmlElement>())
					if (t.GetAttribute("kind") == kind && string.Equals(t.GetAttribute("title"), title, StringComparison.OrdinalIgnoreCase))
						return t.GetAttribute("hash");
			}
			return "";
		}

		/// 1.3.63: it was copied, but the copied file isn't where it was put - forgets it was copied (so it can be copied again)
		private static bool CopiedFileGone(string hash)
		{
			lock (_lock)
			{
				try
				{
					XmlDocument doc = LoadStore();
					XmlElement e = Find(doc, hash);
					string dest = e?.GetAttribute("dest") ?? "";
					if (e == null || e.GetAttribute("state") != "copied" || dest.Length == 0 || System.IO.File.Exists(dest)) return false;
					e.SetAttribute("state", "downloading");
					e.SetAttribute("note", "the copy made earlier wasn't there any more (" + dest + ") - copied again");
					doc.Save(StoreFile);
					return true;
				}
				catch
				{
					return false;
				}
			}
		}

		/// 2026 (1.3.51): true when this download finished (copied into the library, or dealt with after finishing).
		internal static bool WasCopied(string hash)
		{
			if (string.IsNullOrEmpty(hash)) return false;
			lock (_lock)
			{
				// finished = copied, or looked at after finishing (skipped / failed / before) - only "downloading" isn't
				string state = Find(LoadStore(), hash)?.GetAttribute("state") ?? "";
				return state.Length > 0 && state != "downloading";
			}
		}

		private static XmlElement Find(XmlDocument doc, string hash) =>
			doc.DocumentElement.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => string.Equals(e.GetAttribute("hash"), hash, StringComparison.OrdinalIgnoreCase));

		private static XmlElement Add(XmlDocument doc, string hash)
		{
			XmlElement e = doc.CreateElement("torrent");
			e.SetAttribute("hash", hash.ToUpperInvariant());
			doc.DocumentElement.AppendChild(e);
			return e;
		}
	}

	/// <summary>The folders MP-TVSeries and Moving Pictures already import from (read from their databases).</summary>
	public static class LibraryFolders
	{
		public static List<string> TV() => Read("TVSeriesDatabase4.db3", "select Path from ImportPathes where enabled = 1");

		public static List<string> Movies() => Read("movingpictures.db3", "select path from import_path where replaced = 0");

		private static readonly System.Text.RegularExpressions.Regex SeasonDirName =
			new System.Text.RegularExpressions.Regex(@"(^|\s)(season|series|staffel|s)\s*\d+(\s*\[[^\]]*\])*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

		/// 2026 (1.3.38): the show folder (and the season folder for this season) where MP-TVSeries already has
		/// episodes of this show. Null when the show isn't in the library yet (or the name fits more than one show).
		public static string ExistingShowFolder(string show, int season, out string seasonFolder)
		{
			seasonFolder = null;
			try
			{
				string[] found = FindShow(show);
				if (found == null) return null;
				int id;
				if (!int.TryParse(found[0], out id)) return null;
				return FolderOf(id, show, season, out seasonFolder);
			}
			catch (Exception ex)
			{
				Log.Instance().Print("Finished downloads: could not look up " + show + " in MP-TVSeries: " + ex.Message);
			}
			return null;
		}

		/// 2026 (1.3.39): the MP-TVSeries show (id, name, original name) a name means - null when none or more than one.
		public static string[] FindShow(string show)
		{
			{
				string key = MediaNames.Key(show);
				if (key.Length == 0) return null;
				List<string[]> shows = Read("TVSeriesDatabase4.db3", "select ID || '|' || ifnull(Pretty_Name,'') || '|' || ifnull(origName,'') from online_series")
					.Select(r => r.Split('|'))
					.Where(p => p.Length >= 2 && (MediaNames.Key(p[1]) == key || (p.Length > 2 && p[2].Length > 0 && MediaNames.Key(p[2]) == key)))
					.ToList();
				if (shows.Count > 1)
				{
					// same name, different shows (Dark Matter 2015 / 2024): the year decides, then the exact name
					System.Text.RegularExpressions.Match year = System.Text.RegularExpressions.Regex.Match(show, @"\b(19|20)\d{2}\b");
					List<string[]> narrowed = year.Success ? shows.Where(p => p[1].Contains(year.Value)).ToList() : shows.Where(p => !System.Text.RegularExpressions.Regex.IsMatch(p[1], @"\((19|20)\d{2}\)")).ToList();
					if (narrowed.Count == 0) narrowed = shows.Where(p => string.Equals(p[1].Trim(), show.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
					shows = narrowed;
				}
				return shows.Count == 1 ? shows[0] : null;
			}
		}

		/// 2026 (1.3.41): true when MP-TVSeries already has this episode on disk.
		public static bool HaveEpisode(string show, int season, int episode)
		{
			try
			{
				string[] found = FindShow(show);
				int id;
				if (found == null || !int.TryParse(found[0], out id)) return false;
				return Read("TVSeriesDatabase4.db3", "select EpisodeFilename from local_episodes where SeriesID = " + id + " and SeasonIndex = " + season +
					" and (EpisodeIndex = " + episode + " or EpisodeIndex2 = " + episode + ")").Count > 0;
			}
			catch
			{
				return false;
			}
		}

		/// 2026 (1.3.41): true when Moving Pictures already has this film (title, and the year when given).
		public static bool HaveFilm(string title, int year)
		{
			try
			{
				string key = MediaNames.Key(title);
				if (key.Length == 0) return false;
				foreach (string row in Read("movingpictures.db3", "select ifnull(title,'') || '|' || ifnull(year,0) from movie_info"))
				{
					string[] p = row.Split('|');
					int y;
					if (MediaNames.Key(p[0]) != key) continue;
					if (year <= 0 || p.Length < 2 || !int.TryParse(p[1], out y) || y <= 0 || Math.Abs(y - year) <= 1) return true;
				}
				// 2026 (1.3.42): also the movies folder itself - movies Moving Pictures hasn't scanned yet
				if (MoviesOnDisk().Any(m => m.Key == key && YearOk(m.Year, year))) return true;
			}
			catch
			{
			}
			return false;
		}

		private sealed class DiskMovie
		{
			public string Key;
			public int Year;
			public string File; // null for a folder
		}

		// 2026 (1.3.43): two lists (new movies land in Movies New; he files them into Movies / Collections himself)
		//  - the new-movies folder (MyTorrents' movies folder, e.g. Movies New): small, read again every 30 minutes
		//  - the rest of the library (Moving Pictures' other import folders - Movies, Collections...): big, read once
		//    when MediaPortal starts and again only when you ask ("Read my movies again"); anything you move there is
		//    in Moving Pictures' own list as well, which is always checked
		private static List<DiskMovie> _incoming, _library;
		private static DateTime _incomingRead = DateTime.MinValue;
		private static bool _libraryStale = true;
		private static readonly object DiskLock = new object();
		private static readonly System.Text.RegularExpressions.Regex TitleYear = new System.Text.RegularExpressions.Regex(@"^(.*?)[\s._\-\(\[]+((?:19|20)\d{2})(?!\d)");
		private static readonly string[] VideoTypes = { ".mkv", ".mp4", ".avi", ".m4v", ".ts", ".wmv", ".mov", ".iso", ".m2ts" };

		private static string IncomingRoot
		{
			get
			{
				try
				{
					return (PostProcessor.MovieFolder ?? "").TrimEnd('\\', '/');
				}
				catch
				{
					return "";
				}
			}
		}

		private static List<DiskMovie> MoviesOnDisk()
		{
			lock (DiskLock)
			{
				string incoming = IncomingRoot;
				bool readIncoming = _incoming == null || DateTime.Now - _incomingRead > TimeSpan.FromMinutes(30);
				if (!readIncoming && !_libraryStale) return _incoming.Concat(_library).ToList();
				ThreadPriority was = Thread.CurrentThread.Priority;
				try
				{
					Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
				}
				catch
				{
				}
				if (readIncoming)
				{
					List<DiskMovie> list = new List<DiskMovie>();
					if (incoming.Length > 0) Walk(incoming, 0, list);
					_incoming = list;
					_incomingRead = DateTime.Now;
				}
				if (_libraryStale)
				{
					List<DiskMovie> list = new List<DiskMovie>();
					List<string> roots = new List<string>();
					try
					{
						roots.AddRange(Movies().Where(x => !string.IsNullOrWhiteSpace(x)));
					}
					catch
					{
					}
					foreach (string root in roots.Select(r => r.TrimEnd('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase)
						.Where(r => !r.Equals(incoming, StringComparison.OrdinalIgnoreCase)))
					{
						Walk(root, 0, list);
					}
					_library = list;
					_libraryStale = false;
					Log.Instance().Print("Movies: read the library folders - " + list.Count(m => m.File != null) + " video files");
				}
				try
				{
					Thread.CurrentThread.Priority = was;
				}
				catch
				{
				}
				return _incoming.Concat(_library).ToList();
			}
		}

		/// "Read my movies again" - the whole library is read at the next look (in the background).
		public static void RereadLibrary()
		{
			_libraryStale = true;
			System.Threading.ThreadPool.QueueUserWorkItem(_ => WarmMovies());
		}

		private static void Walk(string dir, int depth, List<DiskMovie> list)
		{
			if (depth > 6) return;
			try
			{
				if (!Directory.Exists(dir)) return;
				string folderName = Path.GetFileName(dir);
				foreach (string f in Directory.GetFiles(dir))
				{
					if (!VideoTypes.Contains(Path.GetExtension(f).ToLowerInvariant())) continue;
					Add(list, Path.GetFileNameWithoutExtension(f), f);
					if (depth > 0) Add(list, folderName, f); // "Alien (1979)\movie.mkv" counts as Alien too
				}
				foreach (string d in Directory.GetDirectories(dir))
				{
					if (depth >= 0) Add(list, Path.GetFileName(d), null);
					Walk(d, depth + 1, list);
				}
			}
			catch
			{
			}
		}

		private static void Add(List<DiskMovie> list, string name, string file)
		{
			System.Text.RegularExpressions.Match m = TitleYear.Match(name ?? "");
			string title = m.Success && m.Groups[1].Value.Trim().Length > 0 ? m.Groups[1].Value : name;
			int y = 0;
			if (m.Success) int.TryParse(m.Groups[2].Value, out y);
			string key = MediaNames.Key(title);
			if (key.Length > 0) list.Add(new DiskMovie { Key = key, Year = y, File = file });
		}

		private static bool YearOk(int a, int b) => a <= 0 || b <= 0 || Math.Abs(a - b) <= 1;

		/// 2026 (1.3.42): the movie is safely in place - Moving Pictures has it, or a whole video file is in the movies
		/// folder(s) (100 MB or more, not written to for 10 minutes). Used before taking it off your Trakt list.
		public static bool HaveFilmSafely(string title, int year)
		{
			try
			{
				string key = MediaNames.Key(title);
				if (key.Length == 0) return false;
				foreach (string row in Read("movingpictures.db3", "select ifnull(title,'') || '|' || ifnull(year,0) from movie_info"))
				{
					string[] p = row.Split('|');
					int y;
					if (MediaNames.Key(p[0]) != key) continue;
					if (year <= 0 || p.Length < 2 || !int.TryParse(p[1], out y) || y <= 0 || Math.Abs(y - year) <= 1) return true;
				}
				foreach (DiskMovie m in MoviesOnDisk().Where(m => m.File != null && m.Key == key && YearOk(m.Year, year)))
				{
					FileInfo info = new FileInfo(m.File);
					if (info.Exists && info.Length >= 100L * 1024 * 1024 && DateTime.Now - info.LastWriteTime > TimeSpan.FromMinutes(10)) return true;
				}
			}
			catch
			{
			}
			return false;
		}

		/// 2026 (1.3.46): a copy of the film other than "notThis", put in place after "since", whole (100 MB or more,
		/// not written to for 10 minutes) - the finished 5.1 copy of a sound upgrade. Null when there is none yet.
		public static string NewCopy(string title, int year, string notThis, DateTime since) => NewCopy(title, year, notThis, since, true);

		/// noStereo false = 2026 (1.3.49) a "better copy" - any sound.
		public static string NewCopy(string title, int year, string notThis, DateTime since, bool noStereo)
		{
			try
			{
				string key = MediaNames.Key(title);
				foreach (DiskMovie m in MoviesOnDisk().Where(x => x.File != null && x.Key == key && YearOk(x.Year, year)))
				{
					if (string.Equals(m.File, notThis, StringComparison.OrdinalIgnoreCase)) continue;
					FileInfo info = new FileInfo(m.File);
					if (!info.Exists || info.Length < 100L * 1024 * 1024) continue;
					if (info.CreationTime < since || DateTime.Now - info.LastWriteTime < TimeSpan.FromMinutes(10)) continue;
					if (noStereo && Sound.Of(Path.GetFileName(m.File)) == "stereo") continue;
					return m.File;
				}
			}
			catch
			{
			}
			return null;
		}

		/// Background: keep the list of movies on disk fresh, so screens never wait for the NAS.
		public static void WarmMovies()
		{
			try
			{
				MoviesOnDisk();
			}
			catch
			{
			}
		}

		/// 2026 (1.3.44): a movie MyTorrents just filed - added to the remembered list straight away (no re-read).
		public static void NoteMovieFile(string file)
		{
			lock (DiskLock)
			{
				if (_library == null) return;
				Add(_library, Path.GetFileNameWithoutExtension(file), file);
				Add(_library, Path.GetFileName(Path.GetDirectoryName(file)), file);
			}
		}

		/// After a movie is copied in, read the folder again next time.
		public static void MoviesChanged() => _incomingRead = DateTime.MinValue;

		/// 2026 (1.3.39): an episode's name and air date from MP-TVSeries (false when it doesn't know the episode).
		public static bool EpisodeDetails(string showId, int season, int episode, out string title, out string aired)
		{
			title = aired = "";
			int id;
			if (!int.TryParse(showId, out id)) return false;
			List<string> r = Read("TVSeriesDatabase4.db3", "select ifnull(EpisodeName,'') || '|' || ifnull(FirstAired,'') from online_episodes where SeriesID = " + id +
				" and SeasonIndex = " + season + " and EpisodeIndex = " + episode);
			if (r.Count == 0) return false;
			string[] p = r[0].Split('|');
			title = p[0].Trim();
			aired = p.Length > 1 ? p[1].Trim() : "";
			return title.Length > 0 || aired.Length > 0;
		}

		private static string FolderOf(int id, string show, int season, out string seasonFolder)
		{
			seasonFolder = null;
			{
				// a file of this season tells us both folders; otherwise any episode of the show
				List<string> files = Read("TVSeriesDatabase4.db3", "select EpisodeFilename from local_episodes where SeriesID = " + id + " and SeasonIndex = " + season);
				bool sameSeason = files.Count > 0;
				if (!sameSeason) files = Read("TVSeriesDatabase4.db3", "select EpisodeFilename from local_episodes where SeriesID = " + id);
				foreach (string f in files)
				{
					string dir = Path.GetDirectoryName(f);
					if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
					bool isSeasonDir = SeasonDirName.IsMatch(Path.GetFileName(dir).Trim());
					string showFolder = isSeasonDir ? Path.GetDirectoryName(dir) : dir;
					if (sameSeason) seasonFolder = dir;
					else seasonFolder = isSeasonDir ? Naming.SeasonFolderFor(showFolder, MediaNames.LibraryShowName(showFolder, show), season) : showFolder;
					Log.Instance().Print("Finished downloads: " + show + " goes in " + seasonFolder + " (where your other episodes are)");
					return showFolder;
				}
			}
			return null;
		}

		/// A "continuing" folder if there is one, otherwise the first.
		public static string DefaultTV()
		{
			List<string> l = TV();
			return l.FirstOrDefault(p => p.IndexOf("continu", StringComparison.OrdinalIgnoreCase) >= 0) ?? l.FirstOrDefault() ?? "";
		}

		/// A "new" folder if there is one, otherwise the first network/non-root folder, otherwise the first.
		public static string DefaultMovies()
		{
			List<string> l = Movies();
			return l.FirstOrDefault(p => System.Text.RegularExpressions.Regex.IsMatch(p, @"new", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
				?? l.FirstOrDefault(p => p.TrimEnd('\\').Length > 3) ?? l.FirstOrDefault() ?? "";
		}

		/// How MP-TVSeries notices new files, from its own settings, in plain words.
		public static string TVSeriesScanning()
		{
			List<string> watch = Read("TVSeriesDatabase4.db3", "select value from options where property = 'doFolderWatch'");
			List<string> remote = Read("TVSeriesDatabase4.db3", "select value from options where property = 'scanRemoteShare'");
			List<string> lapse = Read("TVSeriesDatabase4.db3", "select value from options where property = 'AutoScanLocalFilesLapse'");
			if (watch.Count == 0) return "";
			bool on = watch[0] == "1";
			string minutes = lapse.Count > 0 ? lapse[0] : "?";
			if (on && remote.Count > 0 && remote[0] == "1")
				return "MP-TVSeries watches its folders (network folders every " + minutes + " minutes), so new episodes show up by themselves.";
			if (on)
				return "MP-TVSeries watches its local folders. For NAS folders, tick \"scan remote shares\" in MP-TVSeries configuration > Import.";
			return "MP-TVSeries is not watching its folders - turn on folder watching in MP-TVSeries configuration > Import, or new episodes appear only after a manual scan.";
		}

		internal static List<string> Read(string db, string sql)
		{
			List<string> result = new List<string>();
			try
			{
				string file = Path.Combine(Config.GetFolder(Config.Dir.Database), db);
				if (!System.IO.File.Exists(file)) return result;
				using (SQLite.NET.SQLiteClient c = new SQLite.NET.SQLiteClient(file))
				{
					ArrayList col = c.GetColumn(sql);
					foreach (object o in col)
					{
						string p = Convert.ToString(o)?.Trim();
						if (!string.IsNullOrEmpty(p) && !result.Contains(p, StringComparer.OrdinalIgnoreCase)) result.Add(p);
					}
				}
			}
			catch (Exception ex)
			{
				Log.Instance().Print("Could not read the library folders from " + db + ": " + ex.Message);
			}
			return result;
		}
	}
}
