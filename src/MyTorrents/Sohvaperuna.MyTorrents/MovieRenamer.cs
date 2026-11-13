using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaPortal.Configuration;

namespace Sohvaperuna.MyTorrents
{
	/// <summary>
	/// 2026 (1.3.49): movies whose file or folder name doesn't match your movie naming choice (When finished tab) -
	/// renamed after your OK ("a check to rename ones that have been missed"). Uses Moving Pictures'
	/// own title and year for each film, and tells Moving Pictures the new place so it keeps the film (watched
	/// status, artwork) instead of seeing a new one. Only from MyTorrents Configuration with MediaPortal closed.
	/// </summary>
	public static class MovieRenamer
	{
		public sealed class Item
		{
			public string Title = "", OldFile = "", NewFile = "";
			public int Year;
			public bool FolderToo;
			public string Display => Title + (Year > 0 ? " (" + Year + ")" : "");
		}

		private static readonly HashSet<string> VideoExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mkv", ".mp4", ".avi", ".m4v", ".mov", ".wmv", ".ts", ".m2ts", ".mpg", ".mpeg", ".iso" };

		public static List<Item> Proposals()
		{
			List<Item> list = new List<Item>();
			List<string> roots = LibraryFolders.Movies().Select(r => r.TrimEnd('\\')).ToList();
			string coll = Collections.Root.TrimEnd('\\');
			string pattern = Naming.MoviePattern, folderPattern = Naming.MovieFolderPattern;
			foreach (string row in LibraryFolders.Read("movingpictures.db3",
				"select ifnull(m.title,'') || char(9) || ifnull(m.year,0) || char(9) || ifnull(f.fullpath,'') || char(9) || ifnull(f.videoresolution,'') || char(9) || ifnull(f.audiochannels,'') || char(9) || " +
				"ifnull(m.score,0) || char(9) || ifnull(m.genres,'') || char(9) || ifnull(f.videocodec,'') || char(9) || ifnull(f.audiocodec,'') " +
				"from movie_info m join local_media__movie_info l on l.movie_info_id = m.id join local_media f on f.id = l.local_media_id " +
				"where (f.ignored = 0 or f.ignored is null)"))
			{
				try
				{
					string[] p = row.Split('\t');
					if (p.Length < 9 || p[0].Trim().Length == 0 || p[2].Length == 0) continue;
					string file = p[2];
					if (!VideoExt.Contains(Path.GetExtension(file))) continue;
					// artwork / extras videos Moving Pictures picked up ("backdrop-x265.mkv", "folder.mkv", "sample")
					if (System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileNameWithoutExtension(file), @"^(backdrop\d*|folder|fanart|poster|theme|trailer|sample)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
					int year;
					int.TryParse(p[1], out year);
					double score;
					double.TryParse(p[5], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out score);
					string res = p[3].Trim().ToLowerInvariant();
					if (res.Length > 0 && !res.EndsWith("p") && !res.EndsWith("i")) res += "p";
					// what's in the name first; Moving Pictures' own reading of the file fills the gaps
					string names = Path.GetFileName(file) + " " + Path.GetFileName(Path.GetDirectoryName(file));
					Naming.Movie mv = Naming.Describe(p[0].Trim(), year, names, score, p[6].Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries));
					if (mv.Quality.Length == 0) mv.Quality = res;
					if (mv.Codec.Length == 0) mv.Codec = Naming.CodecOf(p[7]);
					if (mv.Audio.Length == 0) mv.Audio = Naming.AudioOf(p[8].Replace("AC-3", "AC3"));
					if (mv.Sound.Length == 0) mv.Sound = Naming.SoundOf(p[4]);
					string name, folder;
					Naming.MovieNames(mv, out name, out folder);
					string dir = Path.GetDirectoryName(file);
					string dirName = Path.GetFileName(dir);
					// the film's own folder: not a library folder itself, not the Collections folder, not a collection's folder
					bool ownFolder = !roots.Any(r => string.Equals(r, dir, StringComparison.OrdinalIgnoreCase)) &&
						(coll.Length == 0 || (!string.Equals(dir, coll, StringComparison.OrdinalIgnoreCase) &&
						!string.Equals(Path.GetDirectoryName(dir)?.TrimEnd('\\'), coll, StringComparison.OrdinalIgnoreCase)));
					// already the right shape (e.g. only the rating has moved since) = left alone
					bool fileOff = !Naming.FitsMoviePattern(Path.GetFileNameWithoutExtension(file), pattern, year, false);
					bool folderOff = ownFolder && !Naming.FitsMoviePattern(dirName, folderPattern == "=" ? pattern : folderPattern, year, true);
					if (!fileOff && !folderOff) continue;
					// a collection's own folder (holds other folders, or more than one film) is never renamed
					if (folderOff && !OnlyThisFilm(dir)) folderOff = false;
					if (!fileOff && !folderOff) continue;
					// "[7] " - the film's place in its collection - stays in front of the folder (not the file)
					string folderOrder = OrderOf(dirName);
					string newDir = folderOff ? Path.Combine(Path.GetDirectoryName(dir), folderOrder + folder) : dir;
					string newName = fileOff ? name : Path.GetFileNameWithoutExtension(file);
					list.Add(new Item { Title = p[0].Trim(), Year = year, OldFile = file, NewFile = Path.Combine(newDir, newName + Path.GetExtension(file)), FolderToo = folderOff });
				}
				catch
				{
				}
			}
			// a folder holding more than one film (e.g. Movies New\something\ with two) is never renamed
			foreach (var g in list.Where(i => i.FolderToo).GroupBy(i => Path.GetDirectoryName(i.OldFile), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToList())
				foreach (Item i in g)
				{
					i.FolderToo = false;
					i.NewFile = Path.Combine(Path.GetDirectoryName(i.OldFile), Path.GetFileName(i.NewFile));
				}
			return list.Where(i => !string.Equals(i.OldFile, i.NewFile, StringComparison.OrdinalIgnoreCase)).OrderBy(i => i.Title).ThenBy(i => i.Year).ToList();
		}

		private static string OrderOf(string name)
		{
			System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(name ?? "", @"^\[\d+\]\s*");
			return m.Success ? m.Value.Trim() + " " : "";
		}

		/// True when the folder holds just this one film (no sub-folders with films, one main video).
		private static bool OnlyThisFilm(string dir)
		{
			try
			{
				int videos = Directory.GetFiles(dir).Count(f => VideoExt.Contains(Path.GetExtension(f)) &&
					!System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileNameWithoutExtension(f), @"^(backdrop\d*|folder|fanart|poster|theme|trailer|sample)\b|-(sample|trailer)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
				if (videos != 1) return false;
				foreach (string sub in Directory.GetDirectories(dir))
				{
					string n = Path.GetFileName(sub).ToLowerInvariant();
					if (n == "extras" || n == "featurettes" || n == "subs" || n == "subtitles" || n == "extrafanart" || n == "extrathumbs" || n == "behind the scenes") continue;
					if (Directory.EnumerateFiles(sub, "*", SearchOption.AllDirectories).Any(f => VideoExt.Contains(Path.GetExtension(f)))) return false;
				}
				return true;
			}
			catch
			{
				return false;
			}
		}

		/// Renames one film (and its .nfo / subtitles / artwork with the same name; the folder too when it's the film's
		/// own), then points Moving Pictures at the new place. "" = done, otherwise what went wrong.
		public static string Rename(Item i)
		{
			try
			{
				if (System.Diagnostics.Process.GetProcessesByName("MediaPortal").Length > 0) return "Close MediaPortal first - it has the movies open.";
				if (!File.Exists(i.OldFile)) return i.Display + ": the file isn't there any more (" + i.OldFile + ")";
				string oldDir = Path.GetDirectoryName(i.OldFile);
				string oldBase = Path.GetFileNameWithoutExtension(i.OldFile);
				string newBase = Path.GetFileNameWithoutExtension(i.NewFile);
				string dir = oldDir;
				if (i.FolderToo)
				{
					string newDir = Path.GetDirectoryName(i.NewFile);
					if (Directory.Exists(newDir) && !string.Equals(newDir, oldDir, StringComparison.OrdinalIgnoreCase)) return i.Display + ": a folder called " + Path.GetFileName(newDir) + " is already there";
					if (string.Equals(newDir, oldDir, StringComparison.OrdinalIgnoreCase))
					{
						// only the letters' case differs - Windows needs a step in between
						string temp = oldDir + ".mytorrents-rename";
						Directory.Move(oldDir, temp);
						Directory.Move(temp, newDir);
					}
					else Directory.Move(oldDir, newDir);
					dir = newDir;
				}
				if (!string.Equals(oldBase, newBase, StringComparison.Ordinal))
				{
					foreach (string f in Directory.GetFiles(dir, oldBase + "*"))
					{
						string rest = Path.GetFileName(f).Substring(oldBase.Length); // ".mkv", ".nfo", ".en.srt", "-poster.jpg"
						string to = Path.Combine(dir, newBase + rest);
						if (string.Equals(f, to, StringComparison.Ordinal)) continue;
						if (File.Exists(to) && !string.Equals(f, to, StringComparison.OrdinalIgnoreCase)) continue;
						if (string.Equals(f, to, StringComparison.OrdinalIgnoreCase))
						{
							File.Move(f, f + ".mytorrents-rename");
							File.Move(f + ".mytorrents-rename", to);
						}
						else File.Move(f, to);
					}
				}
				string now = Path.Combine(dir, newBase + Path.GetExtension(i.OldFile));
				Tell(i.OldFile, now, oldDir, dir);
				Log.Instance().Print("Movie names: " + i.OldFile + " -> " + now);
				return "";
			}
			catch (Exception ex)
			{
				return i.Display + ": " + ex.Message;
			}
		}

		/// Moving Pictures: the same film, new place (keeps watched status and artwork).
		private static void Tell(string oldFile, string newFile, string oldDir, string newDir)
		{
			try
			{
				string db = Path.Combine(Config.GetFolder(Config.Dir.Database), "movingpictures.db3");
				if (!File.Exists(db)) return;
				using (SQLite.NET.SQLiteClient c = new SQLite.NET.SQLiteClient(db))
				{
					c.Execute("update local_media set fullpath = '" + newFile.Replace("'", "''") + "' where fullpath = '" + oldFile.Replace("'", "''") + "'");
				}
			}
			catch (Exception ex)
			{
				Log.Instance().Print("Movie names: could not tell Moving Pictures about " + newFile + ": " + ex.Message);
			}
		}
	}
}
