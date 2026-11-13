using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Sohvaperuna.MyTorrents
{
	/// <summary>
	/// 2026 (1.3.49): movies you have in a poor copy - smaller than the "Smallest movie" size for its picture, or a
	/// lower picture than your movie quality (e.g. a DVD copy when you want 1080p). Like the 700 MB 1080p
	/// Elephant Man. "Look for a better copy" puts it on Wanted movies as an upgrade: your movie quality and size,
	/// clearly bigger than the one you have; when the new copy is safely in place the old one is deleted (or kept -
	/// the same "delete the old copy" setting as the 5.1 upgrades). Turned down ones: MyTorrents.poor-ignore.txt.
	/// </summary>
	public static class PoorCopies
	{
		private static string IgnoreFile => MediaPortal.Configuration.Config.GetFile(MediaPortal.Configuration.Config.Dir.Config, "MyTorrents.poor-ignore.txt");

		private static HashSet<string> Ignored()
		{
			try
			{
				return File.Exists(IgnoreFile) ? new HashSet<string>(File.ReadAllLines(IgnoreFile).Where(l => l.Length > 0), StringComparer.OrdinalIgnoreCase) : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			}
			catch
			{
				return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			}
		}

		/// 2026 (1.3.65): "Not this one" hides it from Stereo-only too when it's on both lists.
		public static void Ignore(SoundUpgrades.Movie m)
		{
			IgnoreOnly(m.File);
			if (SoundUpgrades.StereoFiles().Contains(m.File)) SoundUpgrades.IgnoreOnly(m.File);
		}

		/// 2026 (1.3.65): the hidden ones, to look over and put back.
		public static HashSet<string> IgnoredFiles() => Ignored();

		public static void Unignore(string file)
		{
			try
			{
				if (!File.Exists(IgnoreFile)) return;
				File.WriteAllLines(IgnoreFile, File.ReadAllLines(IgnoreFile).Where(l => l.Length > 0 && !l.Equals(file, StringComparison.OrdinalIgnoreCase)).ToArray());
			}
			catch
			{
			}
		}

		public static void IgnoreOnly(string file)
		{
			try
			{
				File.AppendAllText(IgnoreFile, file + Environment.NewLine);
			}
			catch
			{
			}
		}

		private static int Lines(string res)
		{
			Match m = Regex.Match(res ?? "", @"(\d{3,4})");
			if (m.Success) return int.Parse(m.Groups[1].Value);
			return (res ?? "").IndexOf("4k", StringComparison.OrdinalIgnoreCase) >= 0 ? 2160 : 0;
		}

		/// Why this copy is poor ("" = it's fine). The minimum is for a 2-hour film - shorter ones are allowed less.
		public static string Why(long sizeBytes, string resolution, bool hevc, long durationMs)
		{
			int lines = Lines(resolution);
			int want = Lines(Films.Quality);
			long mb = sizeBytes / 1024 / 1024;
			long min = Films.MinSizeFor(lines + "p", hevc);
			if (durationMs > 10 * 60 * 1000 && durationMs < 120 * 60 * 1000) min = min * durationMs / (120 * 60 * 1000);
			if (mb > 0 && min > 0 && mb < min) return "only " + Films.SizeText(mb) + " for " + lines + "p";
			if (want > 0 && lines > 0 && lines < want - 100) return lines + "p - you want " + Films.Quality;
			return "";
		}

		/// 2026 (1.3.65): every poor copy (file -> why), whatever was said about it.
		public static Dictionary<string, string> PoorFiles()
		{
			Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (SoundUpgrades.Movie m in All()) d[m.File] = m.Channels;
			return d;
		}

		public static List<SoundUpgrades.Movie> List()
		{
			HashSet<string> ignored = Ignored();
			HashSet<string> trying = new HashSet<string>(WantedFilms.Read().Where(f => f.Replace.Length > 0 && f.State != "have").Select(f => f.Replace), StringComparer.OrdinalIgnoreCase);
			List<SoundUpgrades.Movie> list = All().Where(m => !ignored.Contains(m.File) && !trying.Contains(m.File)).ToList();
			return list.OrderBy(m => m.Title).ThenBy(m => m.Year).ToList();
		}

		private static List<SoundUpgrades.Movie> All()
		{
			List<SoundUpgrades.Movie> list = new List<SoundUpgrades.Movie>();
			foreach (string row in LibraryFolders.Read("movingpictures.db3",
				"select ifnull(m.title,'') || char(9) || ifnull(m.year,0) || char(9) || ifnull(f.fullpath,'') || char(9) || ifnull(f.audiochannels,'') || char(9) || ifnull(f.filesize,0) || char(9) || ifnull(f.videoresolution,'') || char(9) || ifnull(f.videocodec,'') || char(9) || ifnull(f.duration,0) " +
				"from movie_info m join local_media__movie_info l on l.movie_info_id = m.id join local_media f on f.id = l.local_media_id " +
				"where (f.ignored = 0 or f.ignored is null)"))
			{
				string[] p = row.Split('\t');
				if (p.Length < 8 || p[2].Length == 0) continue;
				if (Regex.IsMatch(Path.GetFileNameWithoutExtension(p[2]), @"^(backdrop\d*|folder|fanart\d*|poster|theme|trailer|sample)\b|-(trailer|sample)$", RegexOptions.IgnoreCase)) continue;
				int y;
				long size;
				int.TryParse(p[1], out y);
				long.TryParse(p[4], out size);
				long dur;
				long.TryParse(p[7], out dur);
				// 2026 (1.3.52): the picture size from the file's name first. Moving Pictures reads the first video it
				// finds in the file, and some copies carry their cover as a tiny second "video" (154x231) - so it said
				// 240p for a 1080p Along Came a Spider. A tiny picture in a big file is that cover - not counted.
				string res = Naming.QualityOf(Path.GetFileName(p[2]) + " " + Path.GetFileName(Path.GetDirectoryName(p[2])));
				if (res.Length == 0)
				{
					res = p[5];
					if (Lines(res) > 0 && Lines(res) < 400 && size > 900L * 1024 * 1024) res = "";
				}
				string why = Why(size, res, Films.IsHevc(p[6] + " " + Path.GetFileName(p[2])), dur);
				if (why.Length == 0) continue;
				list.Add(new SoundUpgrades.Movie { Title = p[0], Year = y, File = p[2], Channels = why, Size = size, Resolution = res });
			}
			return list;
		}

		/// 2026 (1.3.65): stereo as well = look for the best: a better copy, 5.1 if there is one.
		public static bool Look(SoundUpgrades.Movie m) => Look(m.Title, m.Year, m.File);

		/// Any movie you have (e.g. from the check-up): by its file.
		public static bool Look(string title, int year, string file) => WantedFilms.AddUpgrade(title, year > 0 ? year.ToString() : "", file, SoundUpgrades.StereoFiles().Contains(file) ? "best" : "better");
	}
}
