using System;
using System.Collections.Generic;
using System.Linq;

namespace Sohvaperuna.MyTorrents
{
	/// <summary>
	/// 2026 (1.3.65): "Movies to improve" - the poor copies and the stereo-only movies in one list (Mark, 12 Nov: "a
	/// list that has both in one and the buttons in the same rows"). Each row says what's wrong and what will be
	/// looked for:
	///   poor + stereo = the best copy: a better one, 5.1 if there is one (a better stereo copy still counts)
	///   poor only     = a better copy
	///   stereo only   = a 5.1 copy (the picture is already fine)
	/// "Not this one" hides it for good. Used by the web page, MyTorrents Configuration and MediaPortal alike.
	/// </summary>
	public static class Improve
	{
		public sealed class Item
		{
			public string Title = "", File = "", Resolution = "", Poor = "", Sound = "";
			public int Year;
			public long Size;
			public bool Stereo;

			public string Display => Title + (Year > 0 ? " (" + Year + ")" : "");

			/// "best" / "better" / "" (= 5.1) - as WantedFilms.AddUpgrade takes it.
			public string Kind => Poor.Length > 0 ? (Stereo ? "best" : "better") : "";

			public string PictureText => Poor.Length > 0 ? Poor : "fine";

			public string SoundText => Stereo ? (Sound.Length > 0 ? Sound : "stereo") : "fine";

			/// What the button does, in a few words.
			public string Button => Kind == "best" ? "Find the best copy" : Kind == "better" ? "Find a better one" : "Find 5.1";

			/// What will be looked for, in a sentence.
			public string LooksFor => Kind == "best" ? "a better copy - 5.1 if there is one" : Kind == "better" ? "a better copy" : "a 5.1 copy";

			public string Problem => (Poor.Length > 0 ? Poor : "") + (Poor.Length > 0 && Stereo ? ", " : "") + (Stereo ? SoundText : "");
		}

		public static List<Item> List()
		{
			Dictionary<string, Item> d = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
			HashSet<string> stereoAll = SoundUpgrades.StereoFiles();
			Dictionary<string, string> poorAll = PoorCopies.PoorFiles();
			Func<SoundUpgrades.Movie, Item> get = m =>
			{
				Item i;
				if (!d.TryGetValue(m.File, out i))
				{
					string why;
					i = new Item { Title = m.Title, Year = m.Year, File = m.File, Size = m.Size, Resolution = m.Resolution, Stereo = stereoAll.Contains(m.File), Poor = poorAll.TryGetValue(m.File, out why) ? why : "" };
					d[m.File] = i;
				}
				if (m.Size > i.Size) i.Size = m.Size;
				return i;
			};
			foreach (SoundUpgrades.Movie m in PoorCopies.List()) get(m);
			foreach (SoundUpgrades.Movie m in SoundUpgrades.StereoMovies())
			{
				Item i = get(m);
				i.Sound = m.Channels.ToLowerInvariant(); // "stereo" / "mono" / "2.0"
				if (i.Resolution.Length == 0) i.Resolution = m.Resolution;
			}
			return d.Values.OrderBy(i => i.Title).ThenBy(i => i.Year).ToList();
		}

		public static bool Look(Item i) => WantedFilms.AddUpgrade(i.Title, i.Year > 0 ? i.Year.ToString() : "", i.File, i.Kind);

		/// "Not this one": hidden from the list for good (both reasons).
		public static void Ignore(Item i)
		{
			if (i.Poor.Length > 0) PoorCopies.IgnoreOnly(i.File);
			if (i.Stereo) SoundUpgrades.IgnoreOnly(i.File);
		}

		/// 2026 (1.3.65): the movies you said "Not this one" to - to look over and put back on the list.
		public static List<Item> Hidden()
		{
			HashSet<string> poorHidden = PoorCopies.IgnoredFiles(), stereoHidden = SoundUpgrades.IgnoredFiles();
			HashSet<string> files = new HashSet<string>(poorHidden.Concat(stereoHidden), StringComparer.OrdinalIgnoreCase);
			if (files.Count == 0) return new List<Item>();
			Dictionary<string, Item> d = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
			foreach (string row in LibraryFolders.Read("movingpictures.db3",
				"select ifnull(m.title,'') || char(9) || ifnull(m.year,0) || char(9) || ifnull(f.fullpath,'') || char(9) || ifnull(f.filesize,0) || char(9) || ifnull(f.videoresolution,'') " +
				"from movie_info m join local_media__movie_info l on l.movie_info_id = m.id join local_media f on f.id = l.local_media_id"))
			{
				string[] p = row.Split('\t');
				if (p.Length < 5 || !files.Contains(p[2]) || d.ContainsKey(p[2])) continue;
				int y;
				long size;
				int.TryParse(p[1], out y);
				long.TryParse(p[3], out size);
				d[p[2]] = new Item { Title = p[0], Year = y, File = p[2], Size = size, Resolution = p[4] };
			}
			foreach (string f in files.Where(x => !d.ContainsKey(x))) // gone from Moving Pictures - still listed, by its name
				d[f] = new Item { Title = System.IO.Path.GetFileNameWithoutExtension(f), File = f };
			foreach (Item i in d.Values)
			{
				i.Poor = poorHidden.Contains(i.File) ? "hidden" : "";
				i.Stereo = stereoHidden.Contains(i.File);
			}
			return d.Values.OrderBy(i => i.Title).ThenBy(i => i.Year).ToList();
		}

		/// Back on Movies to improve (if it still needs it).
		public static void PutBack(string file)
		{
			PoorCopies.Unignore(file);
			SoundUpgrades.Unignore(file);
		}

		/// For a "Done - ..." message.
		public static string Added(Item i) => i.Display + " is on Wanted movies - looking for " + i.LooksFor;
	}
}
