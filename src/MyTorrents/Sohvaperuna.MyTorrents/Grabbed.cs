using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaPortal.Configuration;
using MediaPortal.GUI.Library;
using MediaPortal.Dialogs;
using Sohvaperuna.TorrentEngine;

namespace Sohvaperuna.MyTorrents
{
	/// <summary>
	/// 2026: episodes the Auto grabber has started downloading (MyTorrents.grabbed.txt next to MediaPortal.xml).
	/// MP-TVSeries only knows an episode is there once it is in the library and scanned, so without this the same
	/// episode was downloaded again every hour while the first copy was still on its way.
	/// It also spots copies that never start (no one actually has them): those are removed, remembered as dead
	/// (MyTorrents.deadcopies.txt) and the next best copy is tried at the next check.
	/// </summary>
	public static class Grabbed
	{
		/// How long an episode counts as "on its way" (then it may be looked for again)
		public static readonly TimeSpan Keep = TimeSpan.FromDays(7);

		/// A copy that has not even got its details (name / size) after this long is dead
		public static readonly TimeSpan NoDetailsLimit = TimeSpan.FromHours(2);

		/// A copy that has details but has not downloaded anything after this long is dead
		public static readonly TimeSpan NoProgressLimit = TimeSpan.FromHours(6);

		/// 2026 (1.3.42): a download stuck with no seeds and nothing coming in for this many hours is replaced by
		/// another copy (setting "AutoGrabber.StuckHours", 6; 0 = off)
		public static int StuckHours
		{
			get
			{
				int h;
				return int.TryParse(Configuration.GetExtra("AutoGrabber.StuckHours", "6"), out h) && h >= 0 ? h : 6;
			}
		}

		public static readonly int[] StuckChoices = { 0, 3, 6, 12, 24 };

		public static string StuckText(int h) => h <= 0 ? "Off - leave them" : "After " + h + " hours with no seeds";

		// hash -> (bytes downloaded, when that last changed)
		/// 2026 (1.3.53): a download this far along is never removed as stuck - the last bits often turn up later
		public const double NearlyDone = 90.0;

		/// 2026 (1.3.53): true when a download has had no seeds and nothing new coming in for the given hours
		/// (counted from the last time any data arrived, not from when it was added). Nearly finished ones never count.
		internal static bool HasStalled(Sohvaperuna.TorrentEngine.Torrent t, double hours)
		{
			if (hours <= 0 || t.Progress >= NearlyDone) return false;
			return t.SeedsConnected == 0 && t.DownloadSpeed <= 0 && HoursSinceData(t) > hours;
		}

		/// 2026 (1.3.53): how many hours since any new data came in for this download (0 the first time it is seen)
		internal static double HoursSinceData(Sohvaperuna.TorrentEngine.Torrent t)
		{
			lock (_lastMoved)
			{
				LoadStalled();
				KeyValuePair<long, DateTime> last;
				double hours = 0;
				if (!_lastMoved.TryGetValue(t.Hash, out last) || last.Key != t.Downloaded)
				{
					_lastMoved[t.Hash] = new KeyValuePair<long, DateTime>(t.Downloaded, DateTime.Now);
					_stalledChanged = true;
				}
				else hours = (DateTime.Now - last.Value).TotalHours;
				SaveStalled();
				return hours;
			}
		}

		internal static void ForgetStalled(string hash) { if (hash != null) lock (_lastMoved) { _lastMoved.Remove(hash); _stalledChanged = true; } }

		// 2026 (1.3.56): the "when did data last come in" times are kept in a small file, so the clock doesn't start
		// again every time MediaPortal or the background helper restarts (it often never reached 6 hours)
		private static bool _stalledLoaded, _stalledChanged;
		private static DateTime _stalledSaved = DateTime.MinValue;
		private static string StalledFile => Config.GetFile(Config.Dir.Config, "MyTorrents.stalled.txt");

		private static void LoadStalled()
		{
			if (_stalledLoaded) return;
			_stalledLoaded = true;
			try
			{
				if (!System.IO.File.Exists(StalledFile)) return;
				foreach (string line in System.IO.File.ReadAllLines(StalledFile))
				{
					string[] p = line.Split('\t');
					long got, ticks;
					if (p.Length == 3 && long.TryParse(p[1], out got) && long.TryParse(p[2], out ticks) && !_lastMoved.ContainsKey(p[0]))
						_lastMoved[p[0]] = new KeyValuePair<long, DateTime>(got, new DateTime(ticks));
				}
			}
			catch
			{
			}
		}

		private static void SaveStalled()
		{
			if (!_stalledChanged || DateTime.Now - _stalledSaved < TimeSpan.FromMinutes(10)) return;
			_stalledSaved = DateTime.Now;
			_stalledChanged = false;
			try
			{
				// only ones seen in the last 2 weeks
				System.IO.File.WriteAllLines(StalledFile, _lastMoved.Where(kv => DateTime.Now - kv.Value.Value < TimeSpan.FromDays(14))
					.Select(kv => kv.Key + "\t" + kv.Value.Key + "\t" + kv.Value.Value.Ticks).ToArray());
			}
			catch
			{
			}
		}

		private static readonly Dictionary<string, KeyValuePair<long, DateTime>> _lastMoved = new Dictionary<string, KeyValuePair<long, DateTime>>(StringComparer.OrdinalIgnoreCase);

		private sealed class Entry
		{
			public DateTime When;
			public string Hash = "", Title = "", Link = "";
		}

		private static readonly object Lock = new object();
		private static Dictionary<string, Entry> _list;
		private static HashSet<string> _dead;

		private static string File => Config.GetFile(Config.Dir.Config, "MyTorrents.grabbed.txt");
		private static string DeadFile => Config.GetFile(Config.Dir.Config, "MyTorrents.deadcopies.txt");

		private static string Key(SeriesItem s, string episode) => (s.show?.ID ?? s.show?.Name ?? "") + "|" + (episode ?? "").ToUpperInvariant();

		private static Dictionary<string, Entry> List
		{
			get
			{
				if (_list != null) return _list;
				_list = new Dictionary<string, Entry>();
				try
				{
					if (System.IO.File.Exists(File))
					{
						foreach (string line in System.IO.File.ReadAllLines(File))
						{
							// key <tab> ticks [<tab> hash <tab> title <tab> link]
							string[] f = line.Split('\t');
							long ticks;
							if (f.Length < 2 || !long.TryParse(f[1], out ticks)) continue;
							Entry e = new Entry { When = new DateTime(ticks) };
							if (DateTime.Now - e.When >= Keep) continue;
							if (f.Length > 2) e.Hash = f[2];
							if (f.Length > 3) e.Title = f[3];
							if (f.Length > 4) e.Link = f[4];
							_list[f[0]] = e;
						}
					}
				}
				catch (Exception ex)
				{
					Log.Instance().Print("Auto grabber downloaded list: " + ex.Message);
				}
				return _list;
			}
		}

		private static HashSet<string> Dead
		{
			get
			{
				if (_dead != null) return _dead;
				_dead = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				try
				{
					if (System.IO.File.Exists(DeadFile))
					{
						foreach (string line in System.IO.File.ReadAllLines(DeadFile)) if (line.Length > 0) _dead.Add(line);
					}
				}
				catch
				{
				}
				return _dead;
			}
		}

		private static void Save()
		{
			try
			{
				System.IO.File.WriteAllLines(File, List.Where(kv => DateTime.Now - kv.Value.When < Keep)
					.Select(kv => kv.Key + "\t" + kv.Value.When.Ticks + "\t" + kv.Value.Hash + "\t" + Clean(kv.Value.Title) + "\t" + Clean(kv.Value.Link)));
			}
			catch (Exception ex)
			{
				Log.Instance().Print("Auto grabber downloaded list: " + ex.Message);
			}
		}

		private static string Clean(string s) => (s ?? "").Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");

		public static bool Recently(SeriesItem s, string episode)
		{
			if (string.IsNullOrEmpty(episode)) return false;
			lock (Lock)
			{
				Entry e;
				return List.TryGetValue(Key(s, episode), out e) && DateTime.Now - e.When < Keep;
			}
		}

		public static DateTime When(SeriesItem s, string episode)
		{
			lock (Lock)
			{
				Entry e;
				return List.TryGetValue(Key(s, episode), out e) ? e.When : DateTime.MinValue;
			}
		}

		/// 2026 (1.3.66): a season pack has been copied - any episode it was counted for but didn't have (The Haunting
		/// S02 was one 1.8 GB episode, not 9) is let go, so it's looked for again at the next search instead of
		/// waiting a week. Only when every copied file name carries its SxxEyy code, so nothing is let go by mistake.
		public static void ReleaseMissing(string hash, List<string> copied)
		{
			if (string.IsNullOrEmpty(hash) || copied == null || copied.Count == 0) return;
			HashSet<string> have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (string f in copied)
			{
				System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(System.IO.Path.GetFileName(f), @"S(\d{1,2})((?:-?E\d{1,3})+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
				if (!m.Success) return;
				int season = int.Parse(m.Groups[1].Value);
				foreach (System.Text.RegularExpressions.Match ep in System.Text.RegularExpressions.Regex.Matches(m.Groups[2].Value, @"\d+"))
					have.Add("S" + season.ToString("00") + "E" + int.Parse(ep.Value).ToString("00"));
			}
			lock (Lock)
			{
				List<string> gone = List.Where(kv => kv.Value.Hash.Equals(hash, StringComparison.OrdinalIgnoreCase))
					.Select(kv => kv.Key).Where(k => !have.Contains(k.Substring(k.IndexOf('|') + 1))).ToList();
				if (gone.Count == 0) return;
				foreach (string k in gone) List.Remove(k);
				Save();
				Log.Instance().Print("Auto grabber: the download had only " + string.Join(", ", have.OrderBy(x => x)) + " - " +
					string.Join(", ", gone.Select(k => k.Substring(k.IndexOf('|') + 1)).OrderBy(x => x)) + " will be looked for again");
			}
		}

		public static void Add(SeriesItem s, string episode, string hash, string title, string link)
		{
			lock (Lock)
			{
				List[Key(s, episode)] = new Entry { When = DateTime.Now, Hash = hash ?? "", Title = title ?? "", Link = link ?? "" };
				Save();
			}
		}

		/// 2026: same, by the show's MP-TVSeries ID (MyTorrents Configuration has no SeriesItem)
		public static bool IsDead(string showId, string episode, string link)
		{
			if (string.IsNullOrEmpty(link)) return false;
			lock (Lock)
			{
				return Dead.Contains((showId ?? "") + "|" + (episode ?? "").ToUpperInvariant() + "\t" + link);
			}
		}

		/// MediaPortal: the show of each episode given up on, so it can be looked at again soon
		public static event Action<string> LookAgainSoon;

		public static bool IsDead(SeriesItem s, string episode, string link)
		{
			if (string.IsNullOrEmpty(link)) return false;
			lock (Lock)
			{
				return Dead.Contains(Key(s, episode) + "\t" + link);
			}
		}

		private static string RetryFile => Config.GetFile(Config.Dir.Config, "MyTorrents.retry.txt");

		/// 2026: the episode and copy name when this torrent was started by the Auto grabber, else null.
		/// Read straight from the file, so it also works in MyTorrents Configuration (no MediaPortal).
		public static string FindByHash(string hash)
		{
			if (string.IsNullOrEmpty(hash)) return null;
			try
			{
				if (!System.IO.File.Exists(File)) return null;
				foreach (string line in System.IO.File.ReadAllLines(File))
				{
					string[] f = line.Split('\t');
					if (f.Length > 3 && string.Equals(f[2], hash, StringComparison.OrdinalIgnoreCase))
					{
						return f[3];
					}
				}
			}
			catch
			{
			}
			return null;
		}

		/// 2026 (1.3.42): the episodes of a show the Auto grabber started in the last week - (episode, copy, hash, when),
		/// newest first. Read straight from the file, so it also works in MyTorrents Configuration.
		public static List<Tuple<string, string, string, DateTime>> ForShow(string showId)
		{
			List<Tuple<string, string, string, DateTime>> list = new List<Tuple<string, string, string, DateTime>>();
			try
			{
				if (string.IsNullOrEmpty(showId) || !System.IO.File.Exists(File)) return list;
				foreach (string line in System.IO.File.ReadAllLines(File))
				{
					string[] f = line.Split('\t');
					long ticks;
					if (f.Length < 4 || !f[0].StartsWith(showId + "|") || f[2].Length == 0 || !long.TryParse(f[1], out ticks)) continue;
					DateTime when = new DateTime(ticks);
					if (DateTime.Now - when >= Keep) continue;
					list.Add(Tuple.Create(f[0].Substring(showId.Length + 1), f[3], f[2], when));
				}
			}
			catch
			{
			}
			return list.OrderByDescending(x => x.Item4).ToList();
		}

		/// 2026 (1.3.42): "try a different copy" - MediaPortal takes the copy out of the torrent program (its files kept
		/// or deleted), never picks it again and looks for another one.
		public static void TryDifferentCopy(string hash, bool deleteFiles)
		{
			if (string.IsNullOrEmpty(hash)) return;
			try
			{
				ITorrentClient session = TorrentEngine.Instance().TorrentSession;
				if (session != null) session.Remove(hash, deleteFiles);
			}
			catch (Exception ex)
			{
				Log.Instance().Print("Try a different copy: could not remove the old one: " + ex.Message);
			}
			RequestRetry(hash);
			TakeRetries();
		}

		/// 2026: a copy removed by hand in the Downloads tab that should be replaced - noted in MyTorrents.retry.txt,
		/// picked up by MediaPortal at its next check (or the next time it starts).
		public static void RequestRetry(string hash)
		{
			if (string.IsNullOrEmpty(hash)) return;
			try
			{
				System.IO.File.AppendAllText(RetryFile, hash + Environment.NewLine);
			}
			catch
			{
			}
		}

		/// Copies removed by hand with "try another copy": remembered as dead and the episode is looked for again.
		public static void TakeRetries()
		{
			string[] hashes;
			try
			{
				if (!System.IO.File.Exists(RetryFile)) return;
				hashes = System.IO.File.ReadAllLines(RetryFile).Where(h => h.Trim().Length > 0).Select(h => h.Trim()).ToArray();
				System.IO.File.Delete(RetryFile);
			}
			catch
			{
				return;
			}
			lock (Lock)
			{
				bool changed = false;
				foreach (string hash in hashes)
				{
					KeyValuePair<string, Entry> kv = List.FirstOrDefault(x => string.Equals(x.Value.Hash, hash, StringComparison.OrdinalIgnoreCase));
					if (kv.Value == null) continue;
					MarkDead(kv.Key, kv.Value.Link);
					List.Remove(kv.Key);
					changed = true;
					Log.Instance().Print($"Auto grabber: {kv.Value.Title} was removed by hand - another copy will be tried");
					try
					{
						LookAgainSoon?.Invoke(kv.Key); // "showId|S01E03"
					}
					catch
					{
					}
				}
				if (changed) Save();
			}
		}

		/// 2026 (1.3.50): a movie's copy that never got going (Wanted movies) - key "movie|<title key><year>".
		public static void MarkMovieDead(string movieKey, string link)
		{
			lock (Lock)
			{
				MarkDead("movie|" + movieKey, link);
			}
		}

		public static bool IsMovieDead(string movieKey, string link) => IsDead("movie", movieKey, link);

		private static void MarkDead(string key, string link)
		{
			if (string.IsNullOrEmpty(link)) return;
			Dead.Add(key + "\t" + link);
			try
			{
				System.IO.File.AppendAllText(DeadFile, key + "\t" + link + Environment.NewLine);
			}
			catch
			{
			}
		}

		/// Called every few minutes: removes copies the Auto grabber started that never got going, so the next
		/// check tries another copy.
		public static void CheckForDeadCopies()
		{
			TakeRetries();
			ITorrentClient session;
			TorrentList all;
			try
			{
				session = TorrentEngine.Instance().TorrentSession;
				all = session?.TorrentsAll;
			}
			catch
			{
				return;
			}
			if (session == null || all == null) return;
			List<string> gave = new List<string>();
			lock (Lock)
			{
				foreach (KeyValuePair<string, Entry> kv in List.ToList())
				{
					Entry e = kv.Value;
					if (e.Hash.Length == 0) continue;
					Torrent t = all.FirstOrDefault(x => string.Equals(x.Hash, e.Hash, StringComparison.OrdinalIgnoreCase));
					if (t == null)
					{
						// 2026 (1.3.51): removed by hand before it finished ("if I delete the bad ones in qBittorrent
						// will they be tried again?") - yes: that copy counts as bad and the episode is looked for again
						// straight away, instead of waiting a week. Finished and copied ones are left alone.
						// (only when the torrent program answered with its list, and finished downloads are copied)
						if (all.Count > 0 && PostProcessor.Enabled && DateTime.Now - e.When > TimeSpan.FromMinutes(10) && !PostProcessor.WasCopied(e.Hash) && !all.Any(x => x.Progress >= 100 && string.Equals(x.Name, e.Title, StringComparison.OrdinalIgnoreCase)))
						{
							Log.Instance().Print($"Auto grabber: {e.Title} was removed before it finished - another copy will be looked for");
							MarkDead(kv.Key, e.Link);
							List.Remove(kv.Key);
							gave.Add(e.Title);
							try
							{
								LookAgainSoon?.Invoke(kv.Key);
							}
							catch
							{
							}
						}
						continue;
					}
					TimeSpan age = DateTime.Now - e.When;
					bool noDetails = t.Size <= 0 || string.IsNullOrEmpty(t.Name) || t.Name.Equals(t.Hash, StringComparison.OrdinalIgnoreCase);
					bool dead = (noDetails && age > NoDetailsLimit) || (!noDetails && t.Progress <= 0 && t.Downloaded <= 0 && age > NoProgressLimit);
					// 2026 (1.3.42): stuck part way - no seeds and nothing new coming in for hours
					bool stuck = false;
					if (!dead && !noDetails && t.Progress < 100.0 && !t.HasStatus(ClientSupport.Paused))
						stuck = HasStalled(t, StuckHours);
					if (!dead && !stuck) continue;
					ForgetStalled(t.Hash);
					string why = stuck ? "is stuck at " + t.Progress.ToString("0") + "% with no seeds" : noDetails ? "never got its details" : "never started downloading";
					Log.Instance().Print($"Auto grabber: {e.Title} {why} ({(int)age.TotalHours} hours since it started) - removing it and trying another copy");
					try
					{
						session.Remove(t.Hash, true);
					}
					catch (Exception ex)
					{
						Log.Instance().Print("Auto grabber: could not remove it: " + ex.Message);
						continue;
					}
					MarkDead(kv.Key, e.Link);
					List.Remove(kv.Key); // the episode is looked for again at the next check
					gave.Add(e.Title);
					try
					{
						LookAgainSoon?.Invoke(kv.Key); // 2026 (1.3.42)
					}
					catch
					{
					}
				}
				if (gave.Count > 0) Save();
			}
			if (gave.Count == 0 || !MyTorrents.InMediaPortal) return;
			GUIWindowManager.SendThreadCallback((p1, p2, data) =>
			{
				try
				{
					if (!GUIGraphicsContext.IsFullScreenVideo)
					{
						GUIDialogNotify n = (GUIDialogNotify)GUIWindowManager.GetWindow(2016);
						n.Reset();
						n.SetHeading("Auto grabber");
						n.SetText("A copy that never started or got stuck was removed:\n" + gave[0] + "\nAnother copy will be tried.");
						n.TimeOut = 10;
						n.DoModal(GUIWindowManager.ActiveWindow);
						Menus.Check(n);
					}
				}
				catch
				{
				}
				return 0;
			}, 0, 0, null);
		}
	}
}
