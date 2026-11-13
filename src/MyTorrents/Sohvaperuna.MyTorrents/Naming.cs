using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Sohvaperuna.MyTorrents
{
	/// <summary>
	/// 2026 (1.3.39): how finished TV episodes (and new season folders) are named - a ready-made style or your own
	/// pattern built from pieces. Set on the "When finished" tab of MyTorrents Configuration.
	/// </summary>
	public static class Naming
	{
		/// The pieces a pattern can use.
		public const string Pieces = "{show} {season} {season:00} {episode:00} {title} {quality} {airdate} {year}";

		/// "" = like the episodes already in the folder (the old behaviour), otherwise a pattern.
		public static readonly string[][] Styles =
		{
			new[] { "Like the episodes already in the folder", "" },
			new[] { "Show S01E01 Episode Title [720p][2026-08-07]", "{show} S{season:00}E{episode:00} {title} [{quality}][{airdate}]" },
			new[] { "Show - S01E01 - Episode Title", "{show} - S{season:00}E{episode:00} - {title}" },
			new[] { "Show - S01E01", "{show} - S{season:00}E{episode:00}" },
			new[] { "Show S01E01", "{show} S{season:00}E{episode:00}" },
			new[] { "Show 1x01 Episode Title", "{show} {season}x{episode:00} {title}" },
			new[] { "Show.S01E01.Episode.Title", "{show}.S{season:00}E{episode:00}.{title}" },
		};

		/// "" = like the other season folders of the show (else "Season 01"), otherwise a pattern.
		public static readonly string[][] SeasonStyles =
		{
			new[] { "Like the show's other season folders", "" },
			new[] { "Season 01", "Season {season:00}" },
			new[] { "Season 1", "Season {season}" },
			new[] { "Show Season 01", "{show} Season {season:00}" },
		};

		// ---------------------------------------------------------------- 2026 (1.3.49): movies

		public const string MoviePieces = "{title} {sorttitle} {year} {rating} {quality} {source} {codec} {audio} {sound} {genre}";

		/// The details a movie is named with (1.3.49).
		public sealed class Movie
		{
			public string Title = "", Quality = "", Source = "", Codec = "", Audio = "", Sound = "", Genre = "";
			public int Year;
			public double Rating;
		}

		/// Set by Finished downloads while it names a movie: what TMDb said about it (rating, genres).
		[ThreadStatic] public static MovieFiling.Info CurrentInfo;

		public static readonly string[][] MovieStyles =
		{
			new[] { "Title (Year)", "{title} ({year})" },
			new[] { "Title (Year) [1080p]", "{title} ({year}) [{quality}]" },
			new[] { "Title (Year) - 1080p", "{title} ({year}) - {quality}" },
			new[] { "Title (Year) [1080p 5.1]", "{title} ({year}) [{quality} {sound}]" },
			new[] { "Title.Year.1080p", "{title}.{year}.{quality}" },
			// A library named by Media Center Master: "Revenant, The (2016) [7.5][1080p BluRay HEVC AC3][Western]"
			new[] { "Title, The (Year) [7.5][1080p BluRay HEVC AC3][Genre]  (like Media Center Master)", "{sorttitle} ({year}) [{rating}][{quality} {source} {codec} {audio}][{genre}]" },
			// 1.3.63: the same without the rating - it changes over time (Mark)
			new[] { "Title, The (Year) [1080p BluRay HEVC AC3][Genre]", "{sorttitle} ({year}) [{quality} {source} {codec} {audio}][{genre}]" },
		};

		public static readonly string[][] MovieFolderStyles =
		{
			new[] { "Title (Year)", "{title} ({year})" },
			new[] { "Same as the movie's name", "=" },
			new[] { "Title", "{title}" },
		};

		/// Setting "Films.NamePattern" ("" = "Title (Year)", as before).
		public static string MoviePattern
		{
			get
			{
				string p = (Configuration.GetExtra("Films.NamePattern", "") ?? "").Trim();
				return p.Length == 0 ? MovieStyles[0][1] : p;
			}
		}

		/// Setting "Films.FolderPattern" ("" = "Title (Year)"; "=" = the same as the movie's name).
		public static string MovieFolderPattern
		{
			get
			{
				string p = (Configuration.GetExtra("Films.FolderPattern", "") ?? "").Trim();
				return p.Length == 0 ? MovieFolderStyles[0][1] : p;
			}
		}

		/// A movie's name from a pattern. Pieces with nothing to show are left out tidily.
		public static string ApplyMovie(string pattern, string title, int year, string quality, string sound) =>
			ApplyMovie(pattern, new Movie { Title = title, Year = year, Quality = quality, Sound = sound });

		public static string ApplyMovie(string pattern, Movie m)
		{
			string title = m.Title, quality = m.Quality, sound = m.Sound;
			int year = m.Year;
			bool dots = Regex.IsMatch(pattern, @"\}\.\w|\}\.\{");
			Func<string, string> piece = s => dots ? Regex.Replace((s ?? "").Trim(), @"\s+", ".") : (s ?? "").Trim();
			string r = pattern
				.Replace("{sorttitle}", piece(SortTitle(title)))
				.Replace("{title}", piece(title))
				.Replace("{rating}", m.Rating > 0 ? m.Rating.ToString("0.0", CultureInfo.InvariantCulture) : "")
				.Replace("{source}", m.Source ?? "")
				.Replace("{codec}", m.Codec ?? "")
				.Replace("{audio}", m.Audio ?? "")
				.Replace("{genre}", piece(m.Genre))
				.Replace("{year}", year > 0 ? year.ToString(CultureInfo.InvariantCulture) : "")
				.Replace("{quality}", quality ?? "")
				.Replace("{sound}", sound ?? "");
			r = Regex.Replace(r, @"\[\s*\]|\(\s*\)", "");
			r = Regex.Replace(r, @"(\s-\s*)+(?=\s-\s|$)", "");
			r = Regex.Replace(r, @"\.{2,}", ".");
			r = Regex.Replace(r, @"\s{2,}", " ");
			r = Regex.Replace(r, @"\[\s+|\s+\]", x => x.Value.Trim());
			r = r.Trim(' ', '.', '-', '_');
			return MediaNames.SafeName(r);
		}

		/// "The Revenant" -> "Revenant, The"; "A Nightmare on Elm Street: The Dream Child" -> "Nightmare on Elm Street, The
		/// Dream Child, A" (how Media Center Master named them).
		public static string SortTitle(string title)
		{
			string t = (title ?? "").Trim();
			Match a = Regex.Match(t, @"^(The|A|An)\s+(.+)$", RegexOptions.IgnoreCase);
			string article = "";
			if (a.Success)
			{
				article = a.Groups[1].Value;
				t = a.Groups[2].Value;
			}
			t = Regex.Replace(t, @"\s*:\s*", ", ");
			return article.Length > 0 ? t + ", " + article : t;
		}

		public static string SourceOf(string name)
		{
			string n = name ?? "";
			if (Regex.IsMatch(n, @"blu-?ray|bdrip|brrip|bdremux|\bbd\b", RegexOptions.IgnoreCase)) return "BluRay";
			if (Regex.IsMatch(n, @"web-?dl", RegexOptions.IgnoreCase)) return "WEB-DL";
			if (Regex.IsMatch(n, @"web-?rip|\bweb\b", RegexOptions.IgnoreCase)) return "WEBRip";
			if (Regex.IsMatch(n, @"hdtv", RegexOptions.IgnoreCase)) return "HDTV";
			if (Regex.IsMatch(n, @"dvd", RegexOptions.IgnoreCase)) return "DVD";
			return "";
		}

		public static string CodecOf(string name)
		{
			string n = name ?? "";
			if (Regex.IsMatch(n, @"x265|h\.?265|hevc", RegexOptions.IgnoreCase)) return "HEVC";
			if (Regex.IsMatch(n, @"x264|h\.?264|avc", RegexOptions.IgnoreCase)) return "x264";
			if (Regex.IsMatch(n, @"\bav1\b", RegexOptions.IgnoreCase)) return "AV1";
			if (Regex.IsMatch(n, @"xvid", RegexOptions.IgnoreCase)) return "XviD";
			return "";
		}

		public static string AudioOf(string name)
		{
			string n = name ?? "";
			if (Regex.IsMatch(n, @"truehd", RegexOptions.IgnoreCase)) return "TrueHD";
			if (Regex.IsMatch(n, @"dts", RegexOptions.IgnoreCase)) return "DTS";
			if (Regex.IsMatch(n, @"e-?ac-?3|ddp|dd\+", RegexOptions.IgnoreCase)) return "EAC3";
			if (Regex.IsMatch(n, @"\bac-?3\b|\bdd5|\bdd\b", RegexOptions.IgnoreCase)) return "AC3";
			if (Regex.IsMatch(n, @"\baac", RegexOptions.IgnoreCase)) return "AAC";
			if (Regex.IsMatch(n, @"\bmp3\b", RegexOptions.IgnoreCase)) return "MP3";
			if (Regex.IsMatch(n, @"\bopus\b", RegexOptions.IgnoreCase)) return "Opus";
			return "";
		}

		/// "Comedy-Horror" style: the first two genres.
		public static string GenreText(IEnumerable<string> genres) =>
			string.Join("-", (genres ?? Enumerable.Empty<string>()).Select(g => g.Replace(" & ", "-").Replace(" ", "-").Trim()).Where(g => g.Length > 0).Distinct().Take(2));

		/// "5.1" / "7.1" / "Atmos" / "Stereo" from a copy's name ("" when it doesn't say).
		public static string SoundOf(string name)
		{
			string n = name ?? "";
			if (Regex.IsMatch(n, @"atmos", RegexOptions.IgnoreCase)) return "Atmos";
			if (Regex.IsMatch(n, @"(?<!\d)7[ ._]?1(?!\d)")) return "7.1";
			if (Regex.IsMatch(n, @"(?<!\d)5[ ._]?1(?!\d)|\b6ch\b", RegexOptions.IgnoreCase)) return "5.1";
			return Sound.Of(n) == "stereo" ? "Stereo" : "";
		}

		/// The movie's file name and folder name for the chosen styles.
		public static void MovieNames(string title, int year, string file, string torrentName, out string name, out string folder)
		{
			MovieFiling.Info info = CurrentInfo;
			MovieNames(Describe(title, year, file + " " + torrentName, info?.Rating ?? 0, info?.Genres), out name, out folder);
		}

		public static Movie Describe(string title, int year, string names, double rating, IEnumerable<string> genres)
		{
			return new Movie
			{
				Title = title, Year = year, Rating = rating, Genre = GenreText(genres),
				Quality = QualityOf(names), Source = SourceOf(names), Codec = CodecOf(names), Audio = AudioOf(names), Sound = SoundOf(names)
			};
		}

		public static void MovieNames(Movie m, out string name, out string folder)
		{
			name = ApplyMovie(MoviePattern, m);
			if (name.Length == 0) name = MediaNames.SafeName(m.Title + (m.Year > 0 ? " (" + m.Year + ")" : ""));
			string fp = MovieFolderPattern;
			folder = fp == "=" ? name : ApplyMovie(fp, m);
			if (folder.Length == 0) folder = name;
		}

		/// True when a name already has the shape of the pattern (the pieces' values may differ - e.g. a rating that
		/// has moved since) and the right year; a "[7] " order number in front (collections) is allowed.
		public static bool FitsMoviePattern(string existing, string pattern, int year) => FitsMoviePattern(existing, pattern, year, true);

		/// allowOrder: a "[7] " collection order number in front is fine (folders) or not (files: only on the folder).
		public static bool FitsMoviePattern(string existing, string pattern, int year, bool allowOrder)
		{
			string rx = Regex.Escape(pattern);
			rx = Regex.Replace(rx, @"\\\{(\w+)\}", m => m.Groups[1].Value == "year" ? (year > 0 ? year.ToString() : @"\d{4}") : ".*?");
			rx = rx.Replace("\\ ", @"\s*"); // pieces that weren't known leave fewer spaces
			return Regex.IsMatch(existing ?? "", "^" + (allowOrder ? @"(\[\d+\]\s*)?" : "") + rx + "$", RegexOptions.IgnoreCase);
		}

		public static string MovieExample(string pattern, string folderPattern)
		{
			Movie m = new Movie { Title = "The Revenant", Year = 2016, Rating = 7.5, Quality = "1080p", Source = "BluRay", Codec = "HEVC", Audio = "AC3", Sound = "5.1", Genre = "Western" };
			string n = ApplyMovie(pattern, m);
			string f = folderPattern == "=" ? n : ApplyMovie(folderPattern, m);
			return f + "\\" + n + ".mkv";
		}

		// ---------------------------------------------------------------- 2026 (1.3.49): show folders

		/// "" = the show's name as MP-TVSeries has it (as before).
		public static readonly string[][] ShowFolderStyles =
		{
			new[] { "Show  (as MP-TVSeries names it)", "" },
			new[] { "Show (Year)", "{title} ({year})" },
			new[] { "Show, The (Year) [7.9][Genre]  (like Media Center Master)", "{sorttitle} ({year}) [{rating}][{genre}]" },
			// 1.3.63: without the rating - it changes over time, and then the folder name would too (Mark)
			new[] { "Show, The (Year) [Genre]", "{sorttitle} ({year}) [{genre}]" },
		};

		public static string ShowFolderPattern => (Configuration.GetExtra("Finished.ShowFolderPattern", "") ?? "").Trim();

		/// A new show's folder name, or null = as before. Year, rating and genres from MP-TVSeries when it knows the show.
		public static string ShowFolderName(string show, int year = 0, double rating = 0, IEnumerable<string> genres = null)
		{
			string p = ShowFolderPattern;
			if (p.Length == 0) return null;
			string title = Regex.Replace(show ?? "", @"\s*\((19|20)\d{2}\)\s*$", "").Trim();
			Match y = Regex.Match(show ?? "", @"\((19|20)\d{2}\)\s*$");
			if (year <= 0 && y.Success) year = int.Parse(y.Value.Trim('(', ')', ' '));
			try
			{
				string[] found = LibraryFolders.FindShow(show);
				if (found != null)
				{
					string[] d = (LibraryFolders.Read("TVSeriesDatabase4.db3", "select ifnull(FirstAired,'') || char(9) || ifnull(Rating,'') || char(9) || ifnull(Genre,'') from online_series where ID = " + found[0]).FirstOrDefault() ?? "").Split('\t');
					int fy;
					if (year <= 0 && d.Length > 0 && d[0].Length >= 4 && int.TryParse(d[0].Substring(0, 4), out fy)) year = fy;
					double r;
					if (rating <= 0 && d.Length > 1 && double.TryParse(d[1], NumberStyles.Float, CultureInfo.InvariantCulture, out r)) rating = r;
					if (genres == null && d.Length > 2) genres = d[2].Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
				}
			}
			catch
			{
			}
			string n = ApplyMovie(p, new Movie { Title = title, Year = year, Rating = rating, Genre = GenreText(genres) });
			return n.Length > 0 ? n : null;
		}

		public static string ShowFolderExample(string pattern) =>
			pattern.Length == 0 ? "The Diplomat" : ApplyMovie(pattern, new Movie { Title = "The Diplomat", Year = 2023, Rating = 7.9, Genre = "Drama-Thriller" });

		public static string Pattern => Configuration.GetExtra("Finished.NamePattern", "");

		public static string SeasonPattern => Configuration.GetExtra("Finished.SeasonPattern", "");

		/// The details one episode is named with.
		public sealed class Episode
		{
			public string Show = "", Title = "", Quality = "", AirDate = "", Year = "";
			public int Season, Number, Number2; // Number2: the second episode of a double episode file (0 = none)
		}

		/// "720p" etc. from a file or torrent name ("" when it doesn't say).
		public static string QualityOf(string name)
		{
			Match m = Regex.Match(name ?? "", @"(?<![0-9])(2160|1080|720|576|480)[pi](?![a-z0-9])", RegexOptions.IgnoreCase);
			return m.Success ? m.Groups[1].Value + "p" : (Regex.IsMatch(name ?? "", @"\b(4k|uhd)\b", RegexOptions.IgnoreCase) ? "2160p" : "");
		}

		/// Fills a pattern in. Pieces with nothing to show are left out tidily ("[]", "- -", spare spaces / dots).
		public static string Apply(string pattern, Episode e)
		{
			string ep = e.Number.ToString("00") + (e.Number2 > 0 ? "E" + e.Number2.ToString("00") : "");
			string epShort = e.Number.ToString("00") + (e.Number2 > 0 ? "-" + e.Number2.ToString("00") : "");
			bool dots = Regex.IsMatch(pattern, @"\}\.\w|\}\.\{");
			Func<string, string> piece = s => dots ? Regex.Replace((s ?? "").Trim(), @"\s+", ".") : (s ?? "").Trim();
			string r = pattern
				.Replace("{show}", piece(e.Show))
				.Replace("{season:00}", e.Season.ToString("00"))
				.Replace("{season}", e.Season.ToString(CultureInfo.InvariantCulture))
				.Replace("E{episode:00}", "E" + ep)
				.Replace("x{episode:00}", "x" + epShort)
				.Replace("{episode:00}", ep)
				.Replace("{episode}", e.Number.ToString(CultureInfo.InvariantCulture))
				.Replace("{title}", piece(e.Title))
				.Replace("{quality}", e.Quality ?? "")
				.Replace("{airdate}", e.AirDate ?? "")
				.Replace("{year}", e.Year ?? "");
			// tidy up what empty pieces leave behind
			r = Regex.Replace(r, @"\[\s*\]|\(\s*\)", "");
			r = Regex.Replace(r, @"(\s-\s*)+(?=\s-\s|$)", "");
			r = Regex.Replace(r, @"\.{2,}", ".");
			r = Regex.Replace(r, @"\s{2,}", " ");
			r = r.Trim(' ', '.', '-', '_');
			return MediaNames.SafeName(r);
		}

		/// The file name (without extension) for an episode with the chosen style; null = use the old behaviour.
		public static string FileName(Episode e)
		{
			string p = Pattern;
			return string.IsNullOrWhiteSpace(p) ? null : Apply(p, e);
		}

		/// The folder for a season that doesn't have one yet; null = like the other season folders (old behaviour).
		public static string NewSeasonFolder(string showFolder, string show, int season)
		{
			string p = SeasonPattern;
			if (string.IsNullOrWhiteSpace(p)) return null;
			return Path.Combine(showFolder, Apply(p, new Episode { Show = show, Season = season }));
		}

		/// The folder for this season: an existing one, else the chosen season style, else like the other season folders.
		public static string SeasonFolderFor(string showFolder, string show, int season)
		{
			string s = MediaNames.SeasonFolder(showFolder, season);
			if (Directory.Exists(s)) return s;
			return NewSeasonFolder(showFolder, show, season) ?? s;
		}

		/// Episode details for a file: number(s) from the code ("S02E17", "S02E17E18"), quality from the names, and
		/// the title / air date from MP-TVSeries when it knows the show.
		public static Episode Describe(string show, string libraryShowName, int season, string code, string file, string torrentName)
		{
			Episode e = new Episode { Show = libraryShowName, Season = season };
			Match m = Regex.Match(code ?? "", @"[Ee](\d+)(?:-?[Ee](\d+))?");
			if (m.Success)
			{
				e.Number = int.Parse(m.Groups[1].Value);
				if (m.Groups[2].Success) e.Number2 = int.Parse(m.Groups[2].Value);
			}
			e.Quality = QualityOf(file);
			if (e.Quality.Length == 0) e.Quality = QualityOf(torrentName);
			try
			{
				string[] found = LibraryFolders.FindShow(show);
				string title, aired;
				if (found != null && LibraryFolders.EpisodeDetails(found[0], season, e.Number, out title, out aired))
				{
					e.Title = title;
					e.AirDate = aired;
					if (aired.Length >= 4) e.Year = aired.Substring(0, 4);
				}
			}
			catch
			{
			}
			return e;
		}

		/// The example shown under the choices: Elsbeth S02E17.
		public static string Example(string pattern, string seasonPattern) => Example(pattern, seasonPattern, ShowFolderPattern);

		public static string Example(string pattern, string seasonPattern, string showPattern)
		{
			Episode e = new Episode { Show = "Elsbeth", Season = 2, Number = 17, Title = "Ain't Misbehavin'", Quality = "720p", AirDate = "2026-04-10", Year = "2024" };
			string season = string.IsNullOrWhiteSpace(seasonPattern) ? "Season 02" : Apply(seasonPattern, e);
			string file = string.IsNullOrWhiteSpace(pattern) ? "Elsbeth - S02E17 (or like the episodes already there)" : Apply(pattern, e);
			string show = string.IsNullOrWhiteSpace(showPattern) ? "Elsbeth" : ApplyMovie(showPattern, new Movie { Title = "Elsbeth", Year = 2024, Rating = 7.6, Genre = "Comedy-Crime" });
			return show + "\\" + season + "\\" + file + ".mkv";
		}
	}
}
