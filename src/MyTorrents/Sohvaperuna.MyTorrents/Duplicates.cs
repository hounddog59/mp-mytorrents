using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Sohvaperuna.MyTorrents
{
	/// <summary>
	/// 2026 (1.3.59): the same episode or film more than once in the library - from MP-TVSeries (two files matched to
	/// one episode) and Moving Pictures (one film, two copies). Suggests the copy to keep: the better picture, then
	/// English sound, then more sound channels, then the bigger file. The others can be set aside: ".duplicate" is
	/// added to the end of the name, right where the file is (so MP-TVSeries / Moving Pictures don't scan it back in),
	/// and later put back or deleted for good. Different versions (extended, director's cut...) are never pre-ticked.
	/// </summary>
	public static class Duplicates
	{
		public const string AsideEnding = ".duplicate";

		public sealed class Copy
		{
			public string File = "", Sound = "";
			public long Size;
			public int Lines, Channels;
			public bool Foreign, Hevc;
			public string Picture => Lines > 0 ? Lines + "p" + (Hevc ? " HEVC" : "") : "?";
		}

		public sealed class Group
		{
			public string Kind = "", Title = "", Key = "";
			public List<Copy> Copies = new List<Copy>();
			public Copy Keep;
			public bool Versions; // the names say they're different versions (extended...) - you decide
			public bool OtherShow; // 1.3.60: the copies are in different shows' folders - most likely a wrong match in MP-TVSeries
			public bool OtherEpisode; // 1.3.61: the file names say different episodes (S01E04 / S02E01) - a wrong match, not copies
		}

		private static string Cfg(string name) => MediaPortal.Configuration.Config.GetFile(MediaPortal.Configuration.Config.Dir.Config, name);
		private static string IgnoreFile => Cfg("MyTorrents.duplicates-ignore.txt");
		private static string AsideFile => Cfg("MyTorrents.duplicates-aside.txt");

		private static readonly Regex VersionWords = new Regex(@"\b(extended|director'?s[ .]?cut|uncut|unrated|special[ .]edition|remastered|theatrical|imax|alternate|alt[ .]cut|redux|final[ .]cut|part[ .]?\d|pt[ .]?\d|cd\d|disc[ .]?\d)\b", RegexOptions.IgnoreCase);
		// 1.3.60: "Part 1" / "Part 2" (pt1, cd2, disc 2) - pieces of one episode or film, not two copies of it
		private static readonly Regex PartNumber = new Regex(@"\b(?:part|pt|cd|disc)[ .]?(\d+)\b", RegexOptions.IgnoreCase);
		private static readonly Regex SeasonDir = new Regex(@"(^|\s)(season|series|staffel|s)\s*\d+(\s*\[[^\]]*\])*$|^specials?\b", RegexOptions.IgnoreCase);

		private static string PartOf(string file)
		{
			Match m = PartNumber.Match(Path.GetFileNameWithoutExtension(file) ?? "");
			return m.Success ? m.Groups[1].Value.TrimStart('0') : "";
		}

		private static readonly Regex EpisodeCodeRx = new Regex(@"\b[Ss](\d{1,4})[Ee](\d{1,3})\b|\b(\d{1,2})x(\d{2,3})\b");

		/// "S13E01" in the name, as "13x1" ("" if none)
		private static string EpisodeCode(string file)
		{
			Match m = EpisodeCodeRx.Match(Path.GetFileNameWithoutExtension(file) ?? "");
			if (!m.Success) return "";
			return m.Groups[1].Success ? int.Parse(m.Groups[1].Value) + "x" + int.Parse(m.Groups[2].Value) : int.Parse(m.Groups[3].Value) + "x" + int.Parse(m.Groups[4].Value);
		}

		/// 2026 (1.3.66): the air date in the name ("[2018-10-12]"), or null
		private static DateTime? AirDateOf(string file)
		{
			Match m = Regex.Match(Path.GetFileNameWithoutExtension(file) ?? "", @"(?:^|[\[ .])((?:19|20)\d\d)-(\d\d)-(\d\d)(?:[\] .]|$)");
			DateTime d;
			return m.Success && DateTime.TryParse(m.Groups[1].Value + "-" + m.Groups[2].Value + "-" + m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out d) ? d : (DateTime?)null;
		}

		/// 2026 (1.3.66): the number of the "Season 02" folder the file is in, or -1
		private static int SeasonFolderNumber(string file)
		{
			Match m = Regex.Match(Path.GetFileName(Path.GetDirectoryName(file) ?? "") ?? "", @"(?:season|series|staffel)\s*(\d+)", RegexOptions.IgnoreCase);
			return m.Success ? int.Parse(m.Groups[1].Value) : -1;
		}

		/// 2026 (1.3.66): the episode title in the name ("Steven Sees a Ghost"), "" if none or only "Episode 1"
		private static string TitleOf(string file)
		{
			string n = Path.GetFileNameWithoutExtension(file) ?? "";
			Match m = EpisodeCodeRx.Match(n);
			if (!m.Success) return "";
			string t = n.Substring(m.Index + m.Length);
			t = Regex.Replace(t, @"\[[^\]]*\]|\([^)]*\)", " ");
			t = Regex.Replace(t, @"[^A-Za-z0-9]+", " ").Trim().ToLowerInvariant();
			if (Regex.IsMatch(t, @"^(episode|ep|part|chapter)?\s*(\d+|one|two|three|four|five|six|seven|eight|nine|ten)?$")) return "";
			return t;
		}

		/// 2026 (1.3.66): the names say these are different episodes even though the codes match - aired far apart
		/// (Monster 2022 / 2024 / 2025, The Haunting 2018 / 2020, Trailer Park Boys Out of the Park 2016 / 2017), in
		/// different season folders, or with different real titles. 1.3.59-1.3.61 deleted about 45 of these on Mark's
		/// HTPC as "copies" - never again.
		private static bool DifferentByName(List<Copy> copies)
		{
			List<DateTime> dates = copies.Select(c => AirDateOf(c.File)).Where(d => d.HasValue).Select(d => d.Value).ToList();
			if (dates.Count > 1 && (dates.Max() - dates.Min()).TotalDays > 60) return true;
			List<int> seasons = copies.Select(c => SeasonFolderNumber(c.File)).Where(x => x >= 0).Distinct().ToList();
			if (seasons.Count > 1) return true;
			List<string> titles = copies.Select(c => TitleOf(c.File)).Where(t => t.Length > 0).Distinct().ToList();
			for (int i = 0; i < titles.Count; i++)
				for (int j = i + 1; j < titles.Count; j++)
					if (!titles[i].Contains(titles[j]) && !titles[j].Contains(titles[i])) return true;
			return false;
		}

		/// "Terror of the Zygons (2)" - the part of a story in brackets ("" if none)
		private static string PartInBrackets(string file)
		{
			Match m = Regex.Match(Path.GetFileNameWithoutExtension(file) ?? "", @"\((\d{1,2})\)");
			return m.Success ? m.Groups[1].Value : "";
		}

		/// The show's folder: above "Season 01" if the file is in one.
		private static string ShowFolderName(string file)
		{
			string dir = Path.GetDirectoryName(file) ?? "";
			if (SeasonDir.IsMatch(Path.GetFileName(dir))) dir = Path.GetDirectoryName(dir) ?? "";
			return Path.GetFileName(dir);
		}
		private static readonly Regex NotAFilm = new Regex(@"^(backdrop\d*|folder|fanart\d*|poster|theme|trailer|sample)\b|-(trailer|sample)$", RegexOptions.IgnoreCase);

		private static HashSet<string> ReadSet(string file)
		{
			try
			{
				return System.IO.File.Exists(file) ? new HashSet<string>(System.IO.File.ReadAllLines(file).Where(l => l.Trim().Length > 0), StringComparer.OrdinalIgnoreCase) : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			}
			catch
			{
				return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			}
		}

		private static void WriteSet(string file, IEnumerable<string> lines)
		{
			try
			{
				System.IO.File.WriteAllLines(file, lines.OrderBy(x => x).ToArray());
			}
			catch (Exception ex)
			{
				Log.Instance().Print("Duplicates: could not save " + Path.GetFileName(file) + ": " + ex.Message);
			}
		}

		public static void Ignore(Group g)
		{
			HashSet<string> s = ReadSet(IgnoreFile);
			s.Add(g.Key);
			WriteSet(IgnoreFile, s);
		}

		private static int LinesOf(string resolution)
		{
			Match m = Regex.Match(resolution ?? "", @"(\d{3,4})");
			if (m.Success) return int.Parse(m.Groups[1].Value);
			return (resolution ?? "").IndexOf("4k", StringComparison.OrdinalIgnoreCase) >= 0 ? 2160 : 0;
		}

		private static int Rounded(int height) => height >= 1700 ? 2160 : height >= 1000 ? 1080 : height >= 700 ? 720 : height >= 560 ? 576 : height >= 400 ? 480 : height > 0 ? height : 0;

		private static int ChannelsOf(string c)
		{
			Match m = Regex.Match(c ?? "", @"(\d+)(?:\.(\d))?");
			if (!m.Success) return 0;
			int n = int.Parse(m.Groups[1].Value);
			if (m.Groups[2].Success) n += int.Parse(m.Groups[2].Value);
			return n;
		}

		private static string SoundText(int channels) => channels >= 8 ? "7.1" : channels >= 6 ? "5.1" : channels == 2 ? "stereo" : channels == 1 ? "mono" : channels > 0 ? channels + " ch" : "?";

		/// The best copy first: picture, then English, then sound channels, then size.
		private static IEnumerable<Copy> Best(IEnumerable<Copy> copies) =>
			copies.OrderByDescending(c => c.Lines).ThenBy(c => c.Foreign ? 1 : 0).ThenByDescending(c => c.Channels).ThenByDescending(c => c.Size);

		private static long SizeOf(string file, long known)
		{
			if (known > 0) return known;
			try
			{
				return new FileInfo(file).Length;
			}
			catch
			{
				return 0;
			}
		}

		/// Everything that's in the library twice (slow-ish: run it in the background).
		public static List<Group> Find()
		{
			HashSet<string> ignored = ReadSet(IgnoreFile);
			List<Group> groups = new List<Group>();

			// ---- TV: two files matched to the same episode (double-episode files left out)
			Dictionary<string, Group> tv = new Dictionary<string, Group>();
			foreach (string row in LibraryFolders.Read("TVSeriesDatabase4.db3",
				"select l.CompositeID || char(9) || ifnull(s.Pretty_Name,'') || char(9) || o.SeasonIndex || char(9) || o.EpisodeIndex || char(9) || ifnull(o.EpisodeName,'') || char(9) || " +
				"l.EpisodeFilename || char(9) || ifnull(l.videoHeight,0) || char(9) || ifnull(l.AudioChannels,'') || char(9) || ifnull(l.AudioLanguage,'') || char(9) || ifnull(l.VideoCodec,'') " +
				"from local_episodes l join online_episodes o on o.CompositeID = l.CompositeID join online_series s on s.ID = l.SeriesID " +
				"where o.EpisodeID > 0 and l.EpisodeFilename != '' and ifnull(l.CompositeID2,'') = '' and l.CompositeID in " +
				"(select CompositeID from local_episodes where EpisodeFilename != '' and ifnull(CompositeID2,'') = '' group by CompositeID having count(*) > 1)"))
			{
				string[] p = row.Split('\t');
				if (p.Length < 10 || ignored.Contains("tv:" + p[0]) || !System.IO.File.Exists(p[5])) continue;
				Group g;
				if (!tv.TryGetValue(p[0], out g))
				{
					int se, ep;
					int.TryParse(p[2], out se);
					int.TryParse(p[3], out ep);
					g = new Group { Kind = "TV", Key = "tv:" + p[0], Title = p[1] + " S" + se.ToString("00") + "E" + ep.ToString("00") + (p[4].Length > 0 ? " " + p[4] : "") };
					tv[p[0]] = g;
				}
				int h;
				int.TryParse(p[6], out h);
				int lines = Math.Max(Rounded(h), LinesOf(Naming.QualityOf(Path.GetFileName(p[5]))));
				string lang = p[8].ToLowerInvariant();
				bool foreign = Sound.OtherLanguage(Path.GetFileNameWithoutExtension(p[5]), true) ||
					(lang.Length > 1 && lang != "-1" && !lang.Contains("en") && !lang.Contains("und"));
				int ch = ChannelsOf(p[7]);
				g.Copies.Add(new Copy { File = p[5], Lines = lines, Channels = ch, Foreign = foreign, Hevc = Films.IsHevc(p[9] + " " + Path.GetFileName(p[5])), Size = SizeOf(p[5], 0), Sound = SoundText(ch) + (foreign ? " (not English)" : "") });
			}
			groups.AddRange(tv.Values.Where(g => g.Copies.Count > 1));

			// ---- films: one film (by title and year) in more than one copy - parts 2, 3... of a film don't count
			Dictionary<string, Group> films = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
			foreach (string row in LibraryFolders.Read("movingpictures.db3",
				"select ifnull(m.title,'') || char(9) || ifnull(m.year,0) || char(9) || f.fullpath || char(9) || ifnull(f.part,1) || char(9) || ifnull(f.videoheight,0) || char(9) || " +
				"ifnull(f.videoresolution,'') || char(9) || ifnull(f.audiochannels,'') || char(9) || ifnull(f.videocodec,'') || char(9) || ifnull(f.filesize,0) " +
				"from movie_info m join local_media__movie_info l on l.movie_info_id = m.id join local_media f on f.id = l.local_media_id " +
				"where (f.ignored = 0 or f.ignored is null)"))
			{
				string[] p = row.Split('\t');
				if (p.Length < 9 || p[0].Length == 0 || p[2].Length == 0) continue;
				int part;
				if (int.TryParse(p[3], out part) && part > 1) continue;
				if (NotAFilm.IsMatch(Path.GetFileNameWithoutExtension(p[2]))) continue;
				string key = "film:" + p[0].Trim().ToLowerInvariant() + "|" + p[1];
				if (ignored.Contains(key)) continue;
				Group g;
				if (!films.TryGetValue(key, out g))
				{
					g = new Group { Kind = "Film", Key = key, Title = p[0] + (p[1] != "0" ? " (" + p[1] + ")" : "") };
					films[key] = g;
				}
				if (g.Copies.Any(c => string.Equals(c.File, p[2], StringComparison.OrdinalIgnoreCase))) continue;
				// the picture size from the name first (some copies carry their cover as a tiny second "video")
				int lines = LinesOf(Naming.QualityOf(Path.GetFileName(p[2]) + " " + Path.GetFileName(Path.GetDirectoryName(p[2]))));
				long size;
				long.TryParse(p[8], out size);
				if (lines == 0)
				{
					int h;
					int.TryParse(p[4], out h);
					lines = h > 0 ? Rounded(h) : LinesOf(p[5]);
					if (lines > 0 && lines < 400 && size > 900L * 1024 * 1024) lines = 0;
				}
				int ch = ChannelsOf(p[6]);
				bool foreign = Sound.OtherLanguage(Path.GetFileNameWithoutExtension(p[2]), false);
				g.Copies.Add(new Copy { File = p[2], Lines = lines, Channels = ch, Foreign = foreign, Hevc = Films.IsHevc(p[7] + " " + Path.GetFileName(p[2])), Size = size, Sound = SoundText(ch) + (foreign ? " (not English)" : "") });
			}
			foreach (Group g in films.Values.Where(g => g.Copies.Count > 1))
			{
				g.Copies = g.Copies.Where(c => System.IO.File.Exists(c.File)).ToList();
				if (g.Copies.Count > 1) groups.Add(g);
			}

			// 1.3.60: every copy a different part ("Part 1", "Part 2") - pieces of one thing, not duplicates
			groups.RemoveAll(g =>
			{
				List<string> parts = g.Copies.Select(c => PartOf(c.File)).ToList();
				return parts.All(x => x.Length > 0) && parts.Distinct().Count() == parts.Count;
			});

			foreach (Group g in groups)
			{
				g.Copies = Best(g.Copies).ToList();
				g.OtherShow = g.Kind == "TV" && g.Copies.Select(c => ShowFolderName(c.File)).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
				// 1.3.61: "Doctor Who S13E01 ... (1)" and "S13E03 ... (3)" matched to one episode are different episodes -
				// 1.3.59 / 1.3.60 hid 33 of them on Mark's HTPC as "copies"
				g.OtherEpisode = g.Kind == "TV" && (g.Copies.Select(c => EpisodeCode(c.File)).Where(x => x.Length > 0).Distinct().Count() > 1 ||
					g.Copies.Select(c => PartInBrackets(c.File)).Where(x => x.Length > 0).Distinct().Count() > 1 ||
					DifferentByName(g.Copies)); // 1.3.66
				g.Keep = g.Copies[0];
				g.Versions = g.Copies.Select(c => VersionWords.Match(Path.GetFileNameWithoutExtension(c.File)).Value.ToLowerInvariant()).Distinct().Count() > 1;
			}
			return groups.OrderBy(g => g.Kind == "TV" ? 0 : 1).ThenBy(g => g.Title).ToList();
		}

		/// Why this copy is kept / not: "keep - better picture" etc.
		public static string Note(Group g, Copy c)
		{
			if (g.OtherEpisode)
				return "a different episode - probably a wrong match in MP-TVSeries";
			if (g.OtherShow)
				return c == g.Keep ? "different show folders - check the match" : "in " + ShowFolderName(c.File) + " - probably matched to the wrong show in MP-TVSeries";
			if (g.Versions && VersionWords.IsMatch(Path.GetFileNameWithoutExtension(c.File)))
				return "a different version (" + VersionWords.Match(Path.GetFileNameWithoutExtension(c.File)).Value + ") - you decide";
			if (c == g.Keep)
			{
				Copy next = g.Copies.Count > 1 ? g.Copies[1] : null;
				if (next == null) return "keep";
				if (c.Lines > next.Lines) return "keep - better picture";
				if (!c.Foreign && next.Foreign) return "keep - English";
				if (c.Channels > next.Channels) return "keep - better sound";
				return c.Size > next.Size ? "keep - bigger file" : "keep - the same as the other";
			}
			return g.Versions ? "you decide" : "extra copy";
		}

		/// 1.3.60: in plain words, why this one is on the list
		public static string Why(Group g)
		{
			List<string> shows = g.Copies.Select(c => ShowFolderName(c.File)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			List<string> dirs = g.Copies.Select(c => Path.GetDirectoryName(c.File) ?? "").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			List<string> libs = g.Copies.Select(c => LibraryOf(c.File)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			string what = g.Kind == "TV" ? "MP-TVSeries has matched " + g.Copies.Count + " files to this one episode." : "Moving Pictures has this movie " + g.Copies.Count + " times.";
			List<string> why = new List<string> { what };
			if (g.OtherEpisode)
				why.Add("But the file names say they are different episodes (" + string.Join(" and ", g.Copies.Select(c => Path.GetFileNameWithoutExtension(c.File)).Distinct().ToArray()) +
					" - different numbers, titles, air dates or season folders), so MP-TVSeries has most likely matched one of them to the wrong episode - they are not copies. Keep them all, then put the match right in MP-TVSeries.");
			else if (g.OtherShow)
				why.Add("But they are in different shows' folders (" + string.Join(" and ", shows.Select(x => "\"" + x + "\"").ToArray()) + "), so they are most likely different shows " +
					"and one of them has been matched to the wrong show in MP-TVSeries - not a duplicate. Keep them all, then put the match right in MP-TVSeries " +
					"(Details tab: right-click the show).");
			else if (libs.Count > 1)
				why.Add("The copies are in different library folders (" + string.Join(" and ", libs.ToArray()) + ") - the show (or movie) is in more than one place.");
			else if (dirs.Count > 1)
				why.Add("They are in different folders of the same " + (g.Kind == "TV" ? "show" : "library") + " (" + string.Join(" and ", dirs.Select(d => "\"" + Path.GetFileName(d) + "\"").ToArray()) + ").");
			else
				why.Add("They are in the same folder - most likely it was downloaded or copied in twice.");

			List<string> parts = g.Copies.Select(c => PartOf(c.File)).ToList();
			if (parts.Any(x => x.Length > 0) && parts.Any(x => x.Length == 0))
				why.Add("One name says \"Part " + parts.First(x => x.Length > 0) + "\" and another has no part number: that one may be the whole " + (g.Kind == "TV" ? "episode" : "movie") +
					" and the other just a piece of it - or a different piece. Have a look before you choose.");
			else if (g.Versions)
				why.Add("The names say they are different versions (" + string.Join(", ", g.Copies.Select(c => VersionWords.Match(Path.GetFileNameWithoutExtension(c.File)).Value).Where(v => v.Length > 0).Distinct().ToArray()) +
					") - you may want to keep both, or just the one you like.");
			else if (g.Copies.Select(c => c.Size).Distinct().Count() == 1 && g.Copies[0].Size > 0)
				why.Add("They are exactly the same size, so they are almost certainly the same file twice.");
			if (!g.OtherShow && !g.OtherEpisode)
			{
				string r = Note(g, g.Keep);
				if (r.StartsWith("keep - ")) why.Add("Suggested: copy 1 (" + r.Substring(7) + ").");
			}
			return string.Join(" ", why.ToArray());
		}

		/// The library folder a file is in (\\HOMENAS\Series_Ended).
		private static string LibraryOf(string file)
		{
			string[] p = (file ?? "").TrimStart('\\').Split('\\');
			return p.Length >= 2 ? "\\\\" + p[0] + "\\" + p[1] : Path.GetPathRoot(file ?? "") ?? "";
		}

		/// 1.3.60: "Do the easy ones for me" - no doubt which copy to keep: the same file twice (same size), or one clearly better
		/// in picture or English sound; never different versions, parts, or copies in different shows' folders.
		public static bool IsEasy(Group g)
		{
			if (g.OtherShow || g.OtherEpisode || g.Versions || g.Copies.Count < 2) return false;
			if (g.Copies.Any(c => PartOf(c.File).Length > 0)) return false;
			if (g.Copies.Select(c => c.Size).Distinct().Count() == 1 && g.Copies[0].Size > 0) return true;
			return g.Copies.Skip(1).All(c => g.Keep.Lines > c.Lines + 100 || (!g.Keep.Foreign && c.Foreign));
		}

		/// Files set aside earlier (still on disk with ".duplicate" on the end).
		public static List<string> SetAside() => ReadSet(AsideFile).Where(f => System.IO.File.Exists(f + AsideEnding)).ToList();

		/// Adds ".duplicate" to the end of the name (and of its subtitles / .nfo / thumbnails). "" = done, else why not.
		public static string PutAside(string file)
		{
			try
			{
				if (!System.IO.File.Exists(file)) return "it isn't there any more";
				foreach (string c in Companions(file)) TryMove(c, c + AsideEnding);
				System.IO.File.Move(file, file + AsideEnding);
				HashSet<string> s = ReadSet(AsideFile);
				s.Add(file);
				WriteSet(AsideFile, s);
				Log.Instance().Print("Duplicates: set aside " + file);
				return "";
			}
			catch (Exception ex)
			{
				return ex.Message;
			}
		}

		public static string PutBack(string file)
		{
			try
			{
				if (!System.IO.File.Exists(file + AsideEnding)) return "it isn't there any more";
				if (System.IO.File.Exists(file)) return "another file has that name now";
				System.IO.File.Move(file + AsideEnding, file);
				foreach (string c in Companions(file + AsideEnding)) TryMove(c, c.Substring(0, c.Length - AsideEnding.Length));
				Forget(file);
				Log.Instance().Print("Duplicates: put back " + file);
				return "";
			}
			catch (Exception ex)
			{
				return ex.Message;
			}
		}

		public static string DeleteForGood(string file)
		{
			try
			{
				foreach (string c in Companions(file + AsideEnding)) TryDelete(c);
				if (System.IO.File.Exists(file + AsideEnding)) System.IO.File.Delete(file + AsideEnding);
				Forget(file);
				Log.Instance().Print("Duplicates: deleted " + file);
				return "";
			}
			catch (Exception ex)
			{
				return ex.Message;
			}
		}

		private static void Forget(string file)
		{
			HashSet<string> s = ReadSet(AsideFile);
			if (s.Remove(file)) WriteSet(AsideFile, s);
		}

		/// "Name.en.srt", "Name.nfo", "Name-thumb.jpg"... next to a file (never another video).
		private static IEnumerable<string> Companions(string file)
		{
			List<string> list = new List<string>();
			try
			{
				string dir = Path.GetDirectoryName(file);
				string b = Path.GetFileNameWithoutExtension(file.EndsWith(AsideEnding, StringComparison.OrdinalIgnoreCase) ? file.Substring(0, file.Length - AsideEnding.Length) : file);
				bool aside = file.EndsWith(AsideEnding, StringComparison.OrdinalIgnoreCase);
				foreach (string f in Directory.GetFiles(dir, b + "*"))
				{
					if (string.Equals(f, file, StringComparison.OrdinalIgnoreCase)) continue;
					string rest = Path.GetFileName(f).Substring(b.Length);
					if (rest.Length == 0 || (rest[0] != '.' && rest[0] != '-' && rest[0] != '_')) continue;
					if (aside != f.EndsWith(AsideEnding, StringComparison.OrdinalIgnoreCase)) continue;
					string ext = Path.GetExtension(aside ? f.Substring(0, f.Length - AsideEnding.Length) : f).ToLowerInvariant();
					if (new[] { ".srt", ".sub", ".idx", ".ass", ".ssa", ".nfo", ".jpg", ".png", ".tbn", ".txt" }.Contains(ext)) list.Add(f);
				}
			}
			catch
			{
			}
			return list;
		}

		private static void TryMove(string a, string b)
		{
			try
			{
				if (!System.IO.File.Exists(b)) System.IO.File.Move(a, b);
			}
			catch
			{
			}
		}

		private static void TryDelete(string a)
		{
			try
			{
				System.IO.File.Delete(a);
			}
			catch
			{
			}
		}
	}

	/// <summary>
	/// 2026 (1.3.60): the duplicates window, one at a time - "which copy do you want to keep?" with the reason it's on the list,
	/// each copy's picture / sound / size / folder, and big buttons. (1.3.59 had one long list with ticks: too confusing.)
	/// The copies you don't keep are hidden (".duplicate" on the end), never deleted until you say so under "Hidden copies".
	/// </summary>
	public class DuplicatesForm : Form
	{
		private readonly Label _count = new Label { AutoSize = false, ForeColor = Color.DimGray };
		private readonly ComboBox _show = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
		private readonly Button _easy = new Button { Text = "Do the easy ones for me", AutoSize = true };
		private readonly Button _hidden = new Button { Text = "Hidden copies...", AutoSize = true };
		private readonly Label _title = new Label { AutoSize = false, Font = new Font("Microsoft Sans Serif", 12f, FontStyle.Bold) };
		private readonly Label _why = new Label { AutoSize = false, Font = new Font("Microsoft Sans Serif", 9f) };
		private readonly Panel _copies = new Panel { AutoScroll = true, BorderStyle = BorderStyle.None };
		private readonly Button _keepAll = new Button { Text = "Keep them all - they're different", AutoSize = true };
		private readonly Button _skip = new Button { Text = "Skip for now", AutoSize = true };
		private readonly Button _back = new Button { Text = "< Back", AutoSize = true };
		private readonly Label _note = new Label { AutoSize = false, ForeColor = Color.DarkGreen };
		private readonly Button _close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
		private List<Duplicates.Group> _all = new List<Duplicates.Group>();
		private List<Duplicates.Group> _todo = new List<Duplicates.Group>();
		private int _at;

		public DuplicatesForm()
		{
			Text = "Duplicates - which copy do you want to keep?";
			StartPosition = FormStartPosition.CenterParent;
			Size = new Size(980, 680);
			MinimumSize = new Size(760, 520);
			Font = new Font("Microsoft Sans Serif", 8.25f);
			ShowIcon = false;
			CancelButton = _close;
			_show.Items.AddRange(new object[] { "TV episodes and movies", "TV episodes only", "Movies only" });
			_show.SelectedIndex = 0;
			Controls.AddRange(new Control[] { _count, _show, _easy, _hidden, _title, _why, _copies, _keepAll, _skip, _back, _note, _close });
			_show.SelectedIndexChanged += (s, e) => { Filter(); Show(0); };
			_easy.Click += (s, e) => DoEasy();
			_hidden.Click += (s, e) => HiddenMenu();
			_keepAll.Click += (s, e) => KeepAll();
			_skip.Click += (s, e) => Show(_at + 1);
			_back.Click += (s, e) => Show(_at - 1);
			Resize += (s, e) => { LayOut(); ShowCopies(); };
			LayOut();
			Shown += (s, e) => Reload();
		}

		private void LayOut()
		{
			int w = ClientSize.Width - 24;
			_count.SetBounds(12, 14, w - 560, 18);
			_show.SetBounds(ClientSize.Width - 12 - 170, 10, 170, 21);
			_hidden.Location = new Point(_show.Left - 8 - _hidden.PreferredSize.Width, 9);
			_easy.Location = new Point(_hidden.Left - 8 - _easy.PreferredSize.Width, 9);
			_title.SetBounds(12, 44, w, 24);
			int whyH = TextRenderer.MeasureText(_why.Text.Length > 0 ? _why.Text : "x", _why.Font, new Size(w, 0), TextFormatFlags.WordBreak).Height + 4;
			_why.SetBounds(12, 72, w, Math.Max(36, whyH));
			int bottom = ClientSize.Height - 44;
			_copies.SetBounds(12, _why.Bottom + 8, w, Math.Max(120, bottom - _why.Bottom - 16));
			_back.Location = new Point(12, bottom + 8);
			_skip.Location = new Point(_back.Right + 6, bottom + 8);
			_keepAll.Location = new Point(_skip.Right + 6, bottom + 8);
			_note.SetBounds(_keepAll.Right + 12, bottom + 13, Math.Max(80, ClientSize.Width - _keepAll.Right - 110), 18);
			_close.Location = new Point(ClientSize.Width - 12 - _close.Width, bottom + 8);
		}

		private void Reload()
		{
			_title.Text = "Looking through MP-TVSeries and Moving Pictures...";
			_why.Text = "This can take a minute on a NAS.";
			_copies.Controls.Clear();
			UseWaitCursor = true;
			BackgroundWorker bw = new BackgroundWorker();
			bw.DoWork += (s, e) => e.Result = Duplicates.Find();
			bw.RunWorkerCompleted += (s, e) =>
			{
				UseWaitCursor = false;
				if (IsDisposed) return;
				_all = e.Error == null ? (List<Duplicates.Group>)e.Result : new List<Duplicates.Group>();
				if (e.Error != null) Log.Instance().Print("Duplicates: " + e.Error.Message);
				Filter();
				Show(0);
			};
			bw.RunWorkerAsync();
		}

		private void Filter()
		{
			_todo = _all.Where(g => _show.SelectedIndex == 0 || (_show.SelectedIndex == 1) == (g.Kind == "TV")).ToList();
			int easy = _todo.Count(Duplicates.IsEasy);
			_easy.Text = "Do the easy ones for me (" + easy + ")";
			_easy.Enabled = easy > 0;
			int hidden = Duplicates.SetAside().Count;
			_hidden.Text = "Hidden copies (" + hidden + ")...";
			_hidden.Enabled = hidden > 0;
			LayOut();
		}

		private new void Show(int index)
		{
			_at = Math.Max(0, Math.Min(index, _todo.Count - 1));
			_back.Enabled = _at > 0;
			_skip.Enabled = _keepAll.Enabled = _todo.Count > 0;
			if (_todo.Count == 0)
			{
				_count.Text = "";
				_title.Text = _all.Count == 0 ? "No duplicates found." : "Nothing left to sort out here.";
				_why.Text = "Every episode and movie is in the library once. Copies you hid are under \"Hidden copies\" - put them back, or delete them for good when you're happy.";
				_copies.Controls.Clear();
				LayOut();
				return;
			}
			Duplicates.Group g = _todo[_at];
			int tv = _todo.Count(x => x.Kind == "TV");
			_count.Text = "Duplicate " + (_at + 1) + " of " + _todo.Count + "   (" + tv + " TV, " + (_todo.Count - tv) + " movies)";
			_title.Text = (g.Kind == "TV" ? "" : "Movie: ") + g.Title;
			_why.Text = Duplicates.Why(g);
			LayOut();
			ShowCopies();
		}

		private void ShowCopies()
		{
			_copies.SuspendLayout();
			_copies.Controls.Clear();
			if (_todo.Count == 0) { _copies.ResumeLayout(); return; }
			Duplicates.Group g = _todo[_at];
			int w = _copies.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4, y = 0, n = 0;
			foreach (Duplicates.Copy c in g.Copies)
			{
				n++;
				bool star = c == g.Keep && !g.OtherShow && !g.OtherEpisode;
				Panel box = new Panel { BorderStyle = BorderStyle.FixedSingle, Location = new Point(0, y), Width = w, BackColor = star ? Color.FromArgb(240, 248, 240) : SystemColors.Window };
				Label head = new Label { AutoSize = false, Location = new Point(10, 8), Size = new Size(w - 190, 20), Font = new Font("Microsoft Sans Serif", 10f, FontStyle.Bold),
					Text = "Copy " + n + (star ? "   ★ suggested" : "") };
				string version = Duplicates.Note(g, c);
				Label info = new Label { AutoSize = false, Location = new Point(10, 30), Size = new Size(w - 190, 18),
					Text = "Picture: " + c.Picture + "      Sound: " + c.Sound + "      Size: " + (c.Size > 0 ? Films.SizeText(c.Size / 1024 / 1024) : "?") + "      " + version };
				Label name = new Label { AutoSize = false, Location = new Point(10, 50), Size = new Size(w - 190, 18), Text = "File: " + Path.GetFileName(c.File) };
				LinkLabel folder = new LinkLabel { AutoSize = false, Location = new Point(10, 70), Size = new Size(w - 190, 18), Text = "Folder: " + Path.GetDirectoryName(c.File) };
				string file = c.File;
				folder.LinkClicked += (s, e) => { try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + file + "\""); } catch { } };
				Button keep = new Button { Text = "Keep this one", Size = new Size(150, 34), Location = new Point(w - 165, 26), Font = new Font("Microsoft Sans Serif", 9f, star ? FontStyle.Bold : FontStyle.Regular) };
				keep.Click += (s, e) => KeepOnly(c);
				box.Height = 96;
				box.Controls.AddRange(new Control[] { head, info, name, folder, keep });
				_copies.Controls.Add(box);
				y += box.Height + 8;
			}
			_copies.ResumeLayout();
		}

		/// Keep this copy, hide the others (".duplicate" on the end - nothing deleted).
		private void KeepOnly(Duplicates.Copy keep)
		{
			Duplicates.Group g = _todo[_at];
			int ok = 0;
			List<string> bad = new List<string>();
			UseWaitCursor = true;
			foreach (Duplicates.Copy c in g.Copies.Where(c => c != keep))
			{
				string err = Duplicates.PutAside(c.File);
				if (err.Length == 0) ok++; else bad.Add(Path.GetFileName(c.File) + ": " + err);
			}
			UseWaitCursor = false;
			if (bad.Count > 0) MessageBox.Show(this, "Couldn't hide:\r\n\r\n" + string.Join("\r\n", bad.ToArray()), "Duplicates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			_note.Text = g.Title + ": kept 1, hid " + ok + " (nothing deleted).";
			Done(g);
		}

		private void KeepAll()
		{
			Duplicates.Group g = _todo[_at];
			Duplicates.Ignore(g);
			_note.Text = g.Title + ": all kept - won't be shown again.";
			Done(g);
		}

		private void Done(Duplicates.Group g)
		{
			_all.Remove(g);
			int at = _at;
			Filter();
			Show(at);
		}

		private void DoEasy()
		{
			List<Duplicates.Group> easy = _todo.Where(Duplicates.IsEasy).ToList();
			if (easy.Count == 0) return;
			if (MessageBox.Show(this, "Do these " + easy.Count + " for you?\r\n\r\nThey are the ones with no doubt: the same file twice, or one copy with a clearly better picture or English sound - " +
				"never different versions, parts, or copies in different shows' folders. The ★ copy is kept and the others are hidden (nothing deleted - \"Hidden copies\" puts them back).\r\n\r\n" +
				string.Join("\r\n", easy.Take(10).Select(g => g.Title).ToArray()) + (easy.Count > 10 ? "\r\n... and " + (easy.Count - 10) + " more" : ""),
				"Do the easy ones for me", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
			int hid = 0, bad = 0;
			UseWaitCursor = true;
			foreach (Duplicates.Group g in easy)
			{
				foreach (Duplicates.Copy c in g.Copies.Where(c => c != g.Keep))
					if (Duplicates.PutAside(c.File).Length == 0) hid++; else bad++;
				_all.Remove(g);
			}
			UseWaitCursor = false;
			_note.Text = easy.Count + " done: hid " + hid + " copies" + (bad > 0 ? ", " + bad + " couldn't be hidden" : "") + " (nothing deleted).";
			Filter();
			Show(0);
		}

		private void HiddenMenu()
		{
			List<string> aside = Duplicates.SetAside();
			ContextMenuStrip m = new ContextMenuStrip();
			m.Items.Add("Put them all back (" + aside.Count + ")", null, (s, e) =>
			{
				int bad = aside.Count(f => Duplicates.PutBack(f).Length > 0);
				_note.Text = (aside.Count - bad) + " put back" + (bad > 0 ? ", " + bad + " couldn't be" : "") + ". They come back at the next scan.";
				Reload();
			});
			m.Items.Add("Delete them for good (" + aside.Count + ")...", null, (s, e) =>
			{
				if (MessageBox.Show(this, "Delete these " + aside.Count + " hidden copies for good?\r\n\r\nThis can't be undone. The copies you kept aren't touched.", "Duplicates", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
				int bad = aside.Count(f => Duplicates.DeleteForGood(f).Length > 0);
				_note.Text = (aside.Count - bad) + " deleted" + (bad > 0 ? ", " + bad + " couldn't be" : "") + ".";
				Filter();
			});
			m.Show(_hidden, new Point(0, _hidden.Height));
		}
	}
}
