using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Sohvaperuna.MyTorrents
{
	/// <summary>
	/// 2026 (1.3.46): movies you have only in stereo (or mono) - Moving Pictures records each file's sound channels,
	/// so no file is opened. "Look for 5.1" puts the film on Wanted movies as an upgrade: only 5.1 / 7.1 copies count,
	/// with your movie quality and size limit. When the new copy is safely in place the stereo one is deleted (setting
	/// "Films.UpgradeDeleteOld", on) or kept. Optional: a few are looked for by themselves each day ("Films.UpgradeAuto").
	/// Films you say no to are kept in MyTorrents.stereo-ignore.txt.
	/// </summary>
	public static class SoundUpgrades
	{
		public sealed class Movie
		{
			public string Title = "", File = "", Channels = "", Resolution = "";
			public int Year;
			public long Size;
			public string Display => Title + (Year > 0 ? " (" + Year + ")" : "");
		}

		public static bool DeleteOld => Configuration.GetExtra("Films.UpgradeDeleteOld", "true") != "false";

		public static bool Auto => Configuration.GetExtra("Films.UpgradeAuto", "false") == "true";

		public const int AutoPerDay = 3;

		private static string IgnoreFile => MediaPortal.Configuration.Config.GetFile(MediaPortal.Configuration.Config.Dir.Config, "MyTorrents.stereo-ignore.txt");

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

		/// 2026 (1.3.65): "Not this one" hides it from Poor copies too when it's on both lists.
		public static void Ignore(Movie m)
		{
			IgnoreOnly(m.File);
			if (PoorCopies.PoorFiles().ContainsKey(m.File)) PoorCopies.IgnoreOnly(m.File);
		}

		/// 2026 (1.3.65): the hidden ones, to look over and put back.
		public static HashSet<string> IgnoredFiles() => Ignored();

		public static void Unignore(string file)
		{
			try
			{
				if (!System.IO.File.Exists(IgnoreFile)) return;
				System.IO.File.WriteAllLines(IgnoreFile, System.IO.File.ReadAllLines(IgnoreFile).Where(l => l.Length > 0 && !l.Equals(file, StringComparison.OrdinalIgnoreCase)).ToArray());
			}
			catch
			{
			}
		}

		public static void IgnoreOnly(string file)
		{
			try
			{
				System.IO.File.AppendAllText(IgnoreFile, file + Environment.NewLine);
			}
			catch
			{
			}
		}

		/// 2026 (1.3.65): every stereo / mono movie file, whatever was said about it.
		public static HashSet<string> StereoFiles() => new HashSet<string>(LibraryFolders.Read("movingpictures.db3",
			"select ifnull(f.fullpath,'') from local_media f where f.audiochannels in ('Stereo','Mono','stereo','mono','2.0','1.0') and (f.ignored = 0 or f.ignored is null)")
			.Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);

		/// Your stereo / mono movies that aren't already being upgraded or turned down.
		public static List<Movie> StereoMovies()
		{
			HashSet<string> ignored = Ignored();
			HashSet<string> trying = new HashSet<string>(WantedFilms.Read().Where(f => f.Replace.Length > 0).Select(f => f.Replace), StringComparer.OrdinalIgnoreCase);
			List<Movie> list = new List<Movie>();
			foreach (string row in LibraryFolders.Read("movingpictures.db3",
				"select ifnull(m.title,'') || char(9) || ifnull(m.year,0) || char(9) || ifnull(f.fullpath,'') || char(9) || ifnull(f.audiochannels,'') || char(9) || ifnull(f.filesize,0) || char(9) || ifnull(f.videoresolution,'') " +
				"from movie_info m join local_media__movie_info l on l.movie_info_id = m.id join local_media f on f.id = l.local_media_id " +
				"where f.audiochannels in ('Stereo','Mono','stereo','mono','2.0','1.0') and (f.ignored = 0 or f.ignored is null)"))
			{
				string[] p = row.Split('\t');
				if (p.Length < 6 || p[2].Length == 0 || ignored.Contains(p[2]) || trying.Contains(p[2])) continue;
				int y;
				long size;
				int.TryParse(p[1], out y);
				long.TryParse(p[4], out size);
				list.Add(new Movie { Title = p[0], Year = y, File = p[2], Channels = p[3], Size = size, Resolution = p[5] });
			}
			return list.OrderBy(m => m.Title).ThenBy(m => m.Year).ToList();
		}

		/// 2026 (1.3.65): a poor copy as well = look for the best: a better copy, 5.1 if there is one.
		public static bool Look(Movie m) => WantedFilms.AddUpgrade(m.Title, m.Year > 0 ? m.Year.ToString() : "", m.File, PoorCopies.PoorFiles().ContainsKey(m.File) ? "best" : "");

		/// Called with the Wanted movies check: upgrades whose 5.1 copy is now safely in place are finished (the stereo
		/// copy deleted, or kept).
		public static void Finish(List<WantedFilms.Film> list)
		{
			bool changed = false;
			foreach (WantedFilms.Film f in list.Where(x => x.State == "grabbed" && x.Replace.Length > 0))
			{
				int y;
				int.TryParse(f.Year, out y);
				string newer = LibraryFolders.NewCopy(f.Title, y, f.Replace, f.Added, !f.Better);
				if (newer == null) continue;
				f.State = "have";
				f.Note = Path.GetFileName(newer);
				changed = true;
				if (!DeleteOld)
				{
					Log.Instance().Print("Upgrade: " + f.Title + " - new copy in place, the old one kept: " + f.Replace);
					continue;
				}
				try
				{
					string dir = Path.GetDirectoryName(f.Replace);
					string baseName = Path.GetFileNameWithoutExtension(f.Replace);
					// 1.3.49: when the new copy landed next to the old one with the same name (other extension), only the old
					// video goes - the new one and the shared .nfo / subtitles / artwork stay
					bool sameName = string.Equals(Path.GetDirectoryName(newer), dir, StringComparison.OrdinalIgnoreCase) &&
						string.Equals(Path.GetFileNameWithoutExtension(newer), baseName, StringComparison.OrdinalIgnoreCase);
					if (sameName) { if (!string.Equals(f.Replace, newer, StringComparison.OrdinalIgnoreCase)) System.IO.File.Delete(f.Replace); }
					else foreach (string side in Directory.GetFiles(dir, baseName + ".*")) System.IO.File.Delete(side);
					// its own folder with nothing left in it but artwork / info files: remove that too
					bool onlyExtras = Directory.GetFiles(dir).All(x => new[] { ".jpg", ".png", ".nfo", ".txt", ".xml", ".srt", ".sub", ".idx" }.Contains(Path.GetExtension(x).ToLowerInvariant()))
						&& Directory.GetDirectories(dir).Length == 0
						&& !LibraryFolders.Movies().Any(r => string.Equals((r ?? "").TrimEnd('\\'), dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
						&& !string.Equals(Path.GetDirectoryName(newer), dir, StringComparison.OrdinalIgnoreCase);
					if (onlyExtras) Directory.Delete(dir, true);
					Log.Instance().Print("Upgrade: " + f.Title + " - new copy in place (" + newer + "), the old one deleted");
				}
				catch (Exception ex)
				{
					Log.Instance().Print("Sound upgrade: " + f.Title + " - could not delete the stereo copy: " + ex.Message);
				}
			}
			if (changed)
			{
				// keep anything added meanwhile
				List<WantedFilms.Film> latest = WantedFilms.Read();
				foreach (WantedFilms.Film f in list.Where(x => x.Replace.Length > 0 && x.State == "have"))
				{
					WantedFilms.Film x = latest.FirstOrDefault(l => l.Replace == f.Replace);
					if (x != null)
					{
						x.State = f.State;
						x.Note = f.Note;
					}
				}
				WantedFilms.Save(latest);
			}
		}

		private static DateTime _lastAuto = DateTime.MinValue;

		/// "Look for a few by themselves": once a day, the next few stereo movies go on Wanted movies as upgrades.
		public static void AutoAdd()
		{
			if (!Auto || DateTime.Now - _lastAuto < TimeSpan.FromHours(24)) return;
			_lastAuto = DateTime.Now;
			int n = 0;
			foreach (Movie m in StereoMovies().OrderBy(x => Guid.NewGuid()).Take(AutoPerDay))
			{
				if (Look(m)) n++;
			}
			if (n > 0) Log.Instance().Print("Sound upgrades: looking for 5.1 copies of " + n + " more movies today");
		}
	}
}
