using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Sohvaperuna.MyTorrents
{
	/// <summary>
	/// 2026: works out a tidy library name from a release name, e.g.
	///   "Elsbeth.S02E17.Four.Body.Problem.1080p.AMZN.WEB-DL.mkv"  ->  Elsbeth\Season 02\Elsbeth - S02E17.mkv
	///   "Dune.Part.Two.2024.2160p.UHD.BluRay.x265.mkv"             ->  Dune Part Two (2024)\Dune Part Two (2024).mkv
	/// </summary>
	public static class MediaNames
	{
		private static readonly Regex Episode = new Regex(
			@"^(?<show>.*?)[\s._\-\[\(]*\b[Ss](?<s>\d{1,2})[\s._\-]?[Ee](?<e>\d{1,3})(?:[\s._\-]?(?:[Ee]|-[Ee]?)(?<e2>\d{1,3}))?(?!\d)",
			RegexOptions.Compiled);

		private static readonly Regex EpisodeX = new Regex(
			@"^(?<show>.*?)[\s._\-\[\(]*\b(?<s>\d{1,2})x(?<e>\d{2,3})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		private static readonly Regex Movie = new Regex(
			@"^(?<title>.+?)[\s._\-\[\(]+(?<year>(?:19|20)\d{2})(?:[\s._\-\]\)]|$)", RegexOptions.Compiled);

		// 1.3.63: also age tags at the front - "18+.2050.2019..." was filed as "18+ 2050 (2019)" (Mark couldn't find it)
		private static readonly Regex LeadingTag = new Regex(@"^\s*(\[[^\]]*\]|\([^\)]*\)|www\.\S+\s*-\s*|(?:18|16|15|13|12)\+(?=[\s._\-])|R18(?=[\s._\-])|X18(?=[\s._\-]))[\s._\-]*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		public static bool TryEpisode(string text, out string show, out int season, out string episodeCode)
		{
			show = "";
			season = 0;
			episodeCode = "";
			if (string.IsNullOrWhiteSpace(text)) return false;
			string t = StripLeadingTags(text);
			Match m = Episode.Match(t);
			if (!m.Success) m = EpisodeX.Match(t);
			if (!m.Success) return false;
			season = int.Parse(m.Groups["s"].Value);
			int e = int.Parse(m.Groups["e"].Value);
			episodeCode = "S" + season.ToString("00") + "E" + e.ToString("00");
			if (m.Groups["e2"].Success)
			{
				int e2 = int.Parse(m.Groups["e2"].Value);
				if (e2 > e && e2 - e < 10) episodeCode += "E" + e2.ToString("00");
			}
			show = Regex.Replace(Clean(m.Groups["show"].Value), @"\s(?<y>(?:19|20)\d{2})$", " (${y})");
			return true;
		}

		/// The film's year is the last plausible year in the name, so "Blade.Runner.2049.2017" and "1917.2019" work.
		public static bool TryMovie(string text, out string title, out int year)
		{
			title = "";
			year = 0;
			if (string.IsNullOrWhiteSpace(text)) return false;
			string t = StripLeadingTags(text);
			Match best = null;
			foreach (Match m in YearRe.Matches(t))
			{
				int y = int.Parse(m.Groups["year"].Value);
				if (y < 1900 || y > DateTime.Now.Year + 1 || m.Index == 0) continue;
				if (Clean(t.Substring(0, m.Index)).Length == 0) continue;
				best = m;
			}
			if (best == null) return false;
			year = int.Parse(best.Groups["year"].Value);
			title = Clean(t.Substring(0, best.Index));
			return title.Length > 0;
		}

		private static readonly Regex YearRe = new Regex(@"(?<=[\s._\-\[\(])(?<year>(?:19|20)\d{2})(?=[\s._\-\]\)]|$)", RegexOptions.Compiled);

		/// "the.show.name-" -> "The Show Name"
		public static string Clean(string s)
		{
			s = Regex.Replace(s ?? "", @"[._]+", " ");
			s = Regex.Replace(s, @"\s+", " ").Trim(' ', '-', '[', '(', ')', ']');
			if (s.Length > 0 && s == s.ToLowerInvariant())
			{
				s = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s);
			}
			return SafeName(s);
		}

		private static string StripLeadingTags(string s)
		{
			string prev;
			do
			{
				prev = s;
				s = LeadingTag.Replace(s, "");
			} while (s != prev && s.Length > 0);
			return s;
		}

		/// Removes characters Windows does not allow in file names.
		public static string SafeName(string s)
		{
			char[] bad = Path.GetInvalidFileNameChars().Concat(new[] { ':', '*', '?', '"', '<', '>', '|', '/', '\\' }).ToArray();
			string r = new string((s ?? "").Where(c => Array.IndexOf(bad, c) < 0).ToArray()).Trim(' ', '.');
			return Regex.Replace(r, @"\s+", " ");
		}

		/// For matching existing folders: lower case, letters and digits only, no leading "the", no year.
		public static string Key(string s)
		{
			s = (s ?? "").ToLowerInvariant();
			s = Regex.Replace(s, @"\[[^\]]*\]", " ").Trim(); // "[2.0][Action-Adventure]" tags from renaming tools
			s = Regex.Replace(s, @"\((19|20)\d{2}\)|\b(19|20)\d{2}$", "").Trim();
			s = Regex.Replace(s, @"^(.*?),\s*(the|a|an)$", "$2 $1"); // 2026 (1.3.49): "Diplomat, The" = "The Diplomat"
			s = Regex.Replace(s, @"&", "and");
			s = Regex.Replace(s, @"[^a-z0-9]", "");
			if (s.StartsWith("the") && s.Length > 6) s = s.Substring(3);
			return s;
		}

		/// The show / season folder to use: an existing one when it matches, otherwise a new name.
		public static string ShowFolder(string root, string show)
		{
			string key = Key(show);
			try
			{
				string existing = Directory.GetDirectories(root).FirstOrDefault(d => Key(Path.GetFileName(d)) == key);
				if (existing != null) return existing;
			}
			catch
			{
			}
			// 2026 (1.3.49): a new show's folder named your way (When finished tab)
			return Path.Combine(root, Naming.ShowFolderName(show) ?? SafeName(show));
		}

		public static string SeasonFolder(string showFolder, int season)
		{
			try
			{
				if (Directory.Exists(showFolder))
				{
					// "Season 2", "Series 02", "S02", and "Show (2026) Season 01" style
					Regex re = new Regex(@"(^|\s)(season|series|staffel|s)\s*0*" + season + @"(\s*\[[^\]]*\])*$", RegexOptions.IgnoreCase);
					string existing = Directory.GetDirectories(showFolder).FirstOrDefault(d => re.IsMatch(Path.GetFileName(d).Trim()));
					if (existing != null) return existing;
				}
			}
			catch
			{
			}
			// a new season: copy the style of the season folders already there ("Apocalypse (2026) Season 01" -> "... Season 02")
			try
			{
				if (Directory.Exists(showFolder))
				{
					foreach (string d in Directory.GetDirectories(showFolder))
					{
						Match m = Regex.Match(Path.GetFileName(d), @"^(?<p>.*?\b(season|series|staffel)\s*)(?<n>\d+)$", RegexOptions.IgnoreCase);
						if (m.Success) return Path.Combine(showFolder, m.Groups["p"].Value + season.ToString(new string('0', m.Groups["n"].Value.Length)));
					}
				}
			}
			catch
			{
			}
			return Path.Combine(showFolder, "Season " + season.ToString("00"));
		}

		/// The show's name as the library writes it: an existing folder's name without "[...]" tags.
		public static string LibraryShowName(string showFolder, string fallback)
		{
			if (!Directory.Exists(showFolder)) return fallback;
			string n = Regex.Replace(Path.GetFileName(showFolder), @"\s*\[[^\]]*\]", "").Trim();
			return n.Length > 0 ? n : fallback;
		}

		/// How existing episode files in this folder start, e.g. "Apocalypse (2026) S01E01 Episode 1 [1080p].mkv" -> "Apocalypse (2026) ".
		/// Null when there are none to copy the style from.
		public static string EpisodePrefix(string seasonFolder)
		{
			try
			{
				if (!Directory.Exists(seasonFolder)) return null;
				foreach (string f in Directory.GetFiles(seasonFolder))
				{
					Match m = Regex.Match(Path.GetFileName(f), @"^(?<p>.*?)\b[Ss]\d{1,2}[Ee]\d{1,3}");
					if (m.Success && m.Groups["p"].Value.Trim().Length > 0) return m.Groups["p"].Value;
				}
			}
			catch
			{
			}
			return null;
		}

		public static string MovieFolder(string root, string title, int year) => MovieFolder(root, title, year, null);

		/// 2026 (1.3.49): the film's folder - one you already have for it, else a new one named folderName
		/// (your naming choice; "Title (Year)" when not given).
		public static string MovieFolder(string root, string title, int year, string folderName)
		{
			string name = string.IsNullOrWhiteSpace(folderName) ? SafeName(title + " (" + year + ")") : folderName;
			string key = Key(title) + year;
			try
			{
				string existing = Directory.GetDirectories(root).FirstOrDefault(d =>
				{
					string n = Path.GetFileName(d);
					return Key(n) + (Regex.Match(n, @"(19|20)\d{2}").Value) == key;
				});
				if (existing != null) return existing;
			}
			catch
			{
			}
			return Path.Combine(root, name);
		}
	}
}
