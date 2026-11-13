#!/usr/bin/env python3
"""
Builds MyTorrents.<version>.mpe1 - the MediaPortal Extension Installer package.

    python Installer/build_mpe1.py            (run from the repository root, after building Release)

Written for the MPEI that ships with MediaPortal 1.38 (MpeCore 1.38):
  * PlatformCompatibility = AnyCPU  -> installs on 64-bit (and 32-bit) MediaPortal.
    Packages without it count as x86 and are refused by 64-bit MP.
  * MediaPortal dependency 1.34 .. 1.99
  * Same extension Id as the old 0.7x/0.80 packages, so MPEI treats it as an upgrade.
"""
import os, sys, uuid, zipfile, datetime
from xml.sax.saxutils import escape

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VERSION = (1, 3, 66, 0)
EXT_ID = "4e2af33d-c719-4478-95f9-231f7ddd3e33"   # original MyTorrents extension id
BIN = os.path.join(ROOT, "bin", "Release")
OUT = os.path.join(ROOT, "Installer", "MyTorrents.%d.%d.%d.mpe1" % VERSION[:3])

DLLS = ["MyTorrents.dll", "TorrentEngine.dll", "TorrentSearch.dll", "RSSReader.dll",
        "UTorrentEngine.dll", "DelugeEngine.dll", "AzureusEngine.dll",
        "QBittorrentEngine.dll", "TransmissionEngine.dll"]

groups = []        # (name, display, description, [file items])
unique = []        # every file item once


def item(local, dest, install_type="CopyFile", update="AlwaysOverwrite", param1="", system=False):
    # MPEI finds a file's data by its LocalFileName in UniqueFileList, so each source file is stored once
    # and every group entry for that file shares the same zip entry.
    local = local.replace("/", "\\")
    same = next((u for u in unique if u["local"] == local), None)
    zipname = same["zip"] if same else "Installer{%s}\\{%s}-%s" % (install_type, uuid.uuid4(), os.path.basename(local.replace("\\", "/")))
    it = dict(local=local, dest=dest, type=install_type, update=update, zip=zipname, param1=param1, system=system)
    if not same:
        unique.append(it)
    return it


def file_xml(it, indent):
    p = " " * indent
    return (f'{p}<FileItem InstallType="{it["type"]}" SystemFile="{str(it["system"]).lower()}" Modified="true">\n'
            f'{p}  <Param1>{escape(it["param1"])}</Param1>\n' if it["param1"] else
            f'{p}<FileItem InstallType="{it["type"]}" SystemFile="{str(it["system"]).lower()}" Modified="true">\n{p}  <Param1 />\n') + \
           (f'{p}  <UpdateOption>{it["update"]}</UpdateOption>\n'
            f'{p}  <LocalFileName>{escape(it["local"])}</LocalFileName>\n'
            f'{p}  <ZipFileName>{escape(it["zip"])}</ZipFileName>\n'
            f'{p}  <DestinationFilename>{escape(it["dest"])}</DestinationFilename>\n'
            f'{p}</FileItem>\n')


# ---- groups -------------------------------------------------------------------------------
plugin = [item(os.path.join(BIN, d), "%Plugins%\\windows\\" + d) for d in DLLS]
# Stand-alone "MyTorrents Configuration" program (settings without starting MediaPortal)
CONFIG_EXE = "MyTorrents Configuration.exe"
plugin.append(item(os.path.join(BIN, CONFIG_EXE), "%Base%\\" + CONFIG_EXE))
plugin.append(item(os.path.join(BIN, CONFIG_EXE + ".config"), "%Base%\\" + CONFIG_EXE + ".config"))
# BiglyBT Web Remote plugin, used by the setup assistant to turn on BiglyBT remote control
for f in sorted(os.listdir(os.path.join(ROOT, "Installer", "thirdparty"))):
    if f.endswith(".zip"):
        plugin.append(item(os.path.join(ROOT, "Installer", "thirdparty", f), "%Plugins%\\windows\\MyTorrents.Setup\\" + f))
groups.append(("Default", "Plugin", "MyTorrents plugin files", plugin))

config = [item(os.path.join(ROOT, "Config", "MyTorrents.xml"), "%Config%\\MyTorrents.xml", update="NeverOverwrite")]
groups.append(("Config", "Config", "Search configuration (kept if you already have one)", config))

skins = []
# Universal screens for every skin: the plugin copies them into skins that need them at start-up
# (SkinSetup.cs), choosing 1080 or 720 from each skin's references.xml.
UNI = os.path.join(ROOT, "Skin", "Universal")
for sub in ("1080", "720", "Media"):
    for f in sorted(os.listdir(os.path.join(UNI, sub))):
        skins.append(item(os.path.join(UNI, sub, f), "%Plugins%\\windows\\MyTorrents.Skin\\" + sub + "\\" + f))
groups.append(("Skins", "Skin files", "Universal screens for any skin (1920x1080 and 1280x720)", skins))

# Images shown by the installer itself (SystemFile: unpacked to temp, not installed)
ART = os.path.join(ROOT, "Installer", "art")
LEFT_IMG = item(os.path.join(ART, "installer_left.png"), "%Temp%\\installer_left.png", system=True)["local"]
ICON_IMG = item(os.path.join(ART, "installer_icon.png"), "%Temp%\\installer_icon.png", system=True)["local"]

# Tick boxes on the last installer page (ticked by default) - empty groups used as switches
groups.append(("DesktopIcon", "Create a desktop icon for MyTorrents Configuration", "Desktop shortcut to the MyTorrents settings program", []))

for it in unique:
    it["path"] = it["local"].replace("\\", "/")
    if not os.path.exists(it["path"]):
        sys.exit("Missing file: " + it["local"])

# ---- install script: enable the plugin in MediaPortal.xml -------------------------------------
script = """//css_reference "MpeCore.dll";
//css_reference "Utils.dll";
using MpeCore.Classes;
using MpeCore;
using MediaPortal.Profile;
public class Script
{
  public static void Main(PackageClass packageClass, ActionItem actionItem)
  {
    using (MediaPortal.Profile.Settings xmlwriter = new MPSettings())
    {
      xmlwriter.SetValueAsBool("plugins", "MyTorrents", true);
      xmlwriter.SetValueAsBool("pluginsdlls", "MyTorrents.dll", true);
      xmlwriter.SetValueAsBool("pluginswindows", "Sohvaperuna.MyTorrents.MyTorrents", true);
      xmlwriter.SetValueAsBool("home", "MyTorrents", true);
      // first install: point at BiglyBT Web Remote on this PC
      if (xmlwriter.GetValueAsString("MyTorrents", "uTorrent.Backend", "") == "")
      {
        xmlwriter.SetValue("MyTorrents", "uTorrent.Backend", "biglybt");
        xmlwriter.SetValue("MyTorrents", "uTorrent.Server.IP", "127.0.0.1");
        xmlwriter.SetValue("MyTorrents", "uTorrent.Server.Port", "9091");
        xmlwriter.SetValue("MyTorrents", "uTorrent.User", "");
        xmlwriter.SetValue("MyTorrents", "uTorrent.Password", "");
      }
    }
    MediaPortal.Profile.Settings.SaveCache();
    return;
  }
}
"""
# 2026 (1.3.50): the background helper ("MyTorrents Configuration.exe /helper") keeps MyTorrents.dll and
# MP-TVSeries.dll open, so Windows wouldn't let the installer replace them - the install "worked" but the old
# files stayed (1.3.47 - 1.3.49 on the HTPC, 4 Nov). Stop it (and MyTorrents Configuration) before the files go in;
# MyTorrents Configuration starts it again afterwards.
stopscript = """//css_reference "MpeCore.dll";
using MpeCore.Classes;
using MpeCore;
public class Script
{
  public static void Main(PackageClass packageClass, ActionItem actionItem)
  {
    StopIt();
  }
  public static void StopIt()
  {
    try
    {
      using (System.Threading.EventWaitHandle e = System.Threading.EventWaitHandle.OpenExisting("MyTorrentsHelperStop")) { e.Set(); }
    }
    catch { }
    for (int i = 0; i < 40; i++)
    {
      if (System.Diagnostics.Process.GetProcessesByName("MyTorrents Configuration").Length == 0) return;
      System.Threading.Thread.Sleep(250);
    }
    foreach (System.Diagnostics.Process p in System.Diagnostics.Process.GetProcessesByName("MyTorrents Configuration"))
    {
      try { p.Kill(); p.WaitForExit(5000); } catch { }
    }
  }
}
"""
# 2026 (1.3.57): start the background helper again once the new files are in (it was stopped above) - as the
# ordinary user, not as the installer's administrator (runas /trustlevel), and only if it is switched on
startscript = r"""//css_reference "MpeCore.dll";
using MpeCore.Classes;
using MpeCore;
public class Script
{
  public static void Main(PackageClass packageClass, ActionItem actionItem)
  {
    try
    {
      string cmd = null;
      using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
        if (k != null) cmd = k.GetValue("MyTorrents (keeps working when MediaPortal is closed)") as string;
      if (string.IsNullOrEmpty(cmd)) return;
      if (System.Diagnostics.Process.GetProcessesByName("MyTorrents Configuration").Length > 0) return;
      string arg = "/trustlevel:0x20000 \"" + cmd.Replace("\"", "\\\"") + "\"";
      System.Diagnostics.ProcessStartInfo si = new System.Diagnostics.ProcessStartInfo("runas.exe", arg);
      si.UseShellExecute = false;
      si.CreateNoWindow = true;
      System.Diagnostics.Process.Start(si);
    }
    catch { }
  }
}
"""
# 2026 (1.3.60): the desktop icon. MPEI's own "create shortcut" deleted the icon and made a new one at every update,
# so Windows put it in the next free spot. Now an icon that's already there is left alone (it keeps its place);
# it's only made when it's missing. "Remove MyTorrents" (MyTorrents Configuration) takes it away.
iconscript = r"""//css_reference "MpeCore.dll";
using MpeCore.Classes;
using MpeCore;
public class Script
{
  public static void Main(PackageClass packageClass, ActionItem actionItem)
  {
    try
    {
      string mine = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory), "MyTorrents Configuration.lnk");
      string all = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonDesktopDirectory), "MyTorrents Configuration.lnk");
      if (System.IO.File.Exists(mine) || System.IO.File.Exists(all)) return;
      string exe = System.IO.Path.Combine(BaseFolder("MyTorrents Configuration.exe"), "MyTorrents Configuration.exe");
      System.Type t = System.Type.GetTypeFromProgID("WScript.Shell");
      object shell = System.Activator.CreateInstance(t);
      object sc = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { mine });
      System.Type st = sc.GetType();
      st.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { exe });
      st.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { System.IO.Path.GetDirectoryName(exe) });
      st.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { "MyTorrents settings for MediaPortal" });
      st.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, sc, null);
    }
    catch { }
  }

  // the MediaPortal folder: the installer (MpeInstaller.exe) runs from it - 1.3.61 / 4.8.2.2 (MediaPortal's own Config
  // class isn't always available to installer scripts, so the icon wasn't made)
  static string BaseFolder(string exe)
  {
    string[] tries = {
      System.AppDomain.CurrentDomain.BaseDirectory,
      System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName),
      System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFilesX86), @"Team MediaPortal\MediaPortal"),
      System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles), @"Team MediaPortal\MediaPortal") };
    foreach (string d in tries)
      if (!string.IsNullOrEmpty(d) && System.IO.File.Exists(System.IO.Path.Combine(d, exe))) return d;
    return tries[0];
  }
}
"""
unscript = """//css_reference "MpeCore.dll";
using MpeCore.Classes;
using MpeCore;
public class Script
{
    public static void Main(PackageClass packageClass, UnInstallItem item)
    {
        try
        {
            using (System.Threading.EventWaitHandle e = System.Threading.EventWaitHandle.OpenExisting("MyTorrentsHelperStop")) { e.Set(); }
        }
        catch { }
        System.Threading.Thread.Sleep(3000);
        return;
    }
}
"""
nounscript = """//css_reference "MpeCore.dll";
using MpeCore.Classes;
using MpeCore;
public class Script
{
    public static void Main(PackageClass packageClass, UnInstallItem item)
    {
        return;
    }
}
"""


def param(name, value, vtype, desc="", indent=12):
    p = " " * indent
    v = f"{p}  <Value>{escape(value)}</Value>\n" if value else f"{p}  <Value />\n"
    d = f"{p}  <Description>{escape(desc)}</Description>\n" if desc else f"{p}  <Description />\n"
    return f'{p}<SectionParam Name="{name}">\n{v}{p}  <ValueType>{vtype}</ValueType>\n{d}{p}</SectionParam>\n'


now = datetime.datetime.now().replace(microsecond=0).isoformat()
x = []
x.append('<?xml version="1.0" encoding="utf-8"?>\n<PackageClass xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">\n  <Version>2.0</Version>\n  <Groups>\n    <Items>\n')
for name, disp, desc, files in groups:
    x.append(f'      <GroupItem Name="{name}">\n        <ParentGroup />\n        <DisplayName>{escape(disp)}</DisplayName>\n        <DefaulChecked>true</DefaulChecked>\n        <Description>{escape(desc)}</Description>\n        <Files>\n          <Items>\n')
    for it in files:
        x.append(file_xml(it, 12))
    x.append('          </Items>\n        </Files>\n      </GroupItem>\n')
x.append('    </Items>\n  </Groups>\n  <Sections>\n    <Items>\n')
# Welcome
x.append('      <SectionItem Guid="4ad0af3b-a07b-415e-845c-3ebdb1ff118f" Name="Welcome Screen" ConditionGroup="">\n        <Params>\n          <Items>\n')
x.append(param("Header text", "Welcome to the Extension Installer for [Name]", "String"))
x.append(param("Description", "This will install [Name] version [Version] on your computer.\nPlease close MediaPortal before continuing.\nClick Next to continue or Cancel to exit Setup.", "String"))
x.append(param("Left part image", LEFT_IMG, "File"))
x.append(param("Header image", "", "File", "Image in upper right part"))
x.append('          </Items>\n        </Params>\n        <Actions>\n          <Items />\n        </Actions>\n        <IncludedGroups />\n        <PanelName>Welcome Screen</PanelName>\n        <WizardButtonsEnum>NextCancel</WizardButtonsEnum>\n      </SectionItem>\n')
# Install
x.append('      <SectionItem Guid="fb52ac16-38a9-459c-8048-3f2531aa9611" Name="Install Section" ConditionGroup="">\n        <Params>\n          <Items>\n')
x.append(param("Header Title", "", "String", "Header title"))
x.append(param("Header description", "", "String", "Description of section, shown in under section title"))
x.append(param("Header image", ICON_IMG, "File", "Image in upper right part"))
x.append('          </Items>\n        </Params>\n        <Actions>\n          <Items>\n')
x.append('            <ActionItem Name="StopHelper" ActionType="Script" ConditionGroup="">\n              <Params>\n                <Items>\n')
x.append(param("Script", stopscript, "Script", indent=18))
x.append(param("Uninstall Script", nounscript, "Script", indent=18))
x.append('                </Items>\n              </Params>\n              <ExecuteLocation>AfterPanelShow</ExecuteLocation>\n            </ActionItem>\n')
x.append('            <ActionItem Name="InstallFiles" ActionType="InstallFiles" ConditionGroup="">\n              <Params>\n                <Items />\n              </Params>\n              <ExecuteLocation>AfterPanelShow</ExecuteLocation>\n            </ActionItem>\n')
x.append('            <ActionItem Name="Script" ActionType="Script" ConditionGroup="">\n              <Params>\n                <Items>\n')
x.append(param("Script", script, "Script", indent=18))
x.append(param("Uninstall Script", unscript, "Script", indent=18))
x.append('                </Items>\n              </Params>\n              <ExecuteLocation>AfterPanelShow</ExecuteLocation>\n            </ActionItem>\n')
x.append('            <ActionItem Name="StartHelper" ActionType="Script" ConditionGroup="">\n              <Params>\n                <Items>\n')
x.append(param("Script", startscript, "Script", indent=18))
x.append(param("Uninstall Script", nounscript, "Script", indent=18))
x.append('                </Items>\n              </Params>\n              <ExecuteLocation>AfterPanelShow</ExecuteLocation>\n            </ActionItem>\n')
x.append('          </Items>\n        </Actions>\n        <IncludedGroups />\n        <PanelName>Install Section</PanelName>\n        <WizardButtonsEnum>Next</WizardButtonsEnum>\n      </SectionItem>\n')
# Finish
x.append('      <SectionItem Guid="774977cb-8b76-4b44-80e0-afd9ba7962e9" Name="Setup Complete" ConditionGroup="">\n        <Params>\n          <Items>\n')
x.append(param("Header text", "[Name] [Version] is installed. Click Finish to open the setup assistant.", "String"))
x.append(param("Left part image", LEFT_IMG, "File"))
x.append(param("Show radio buttons", "", "Bool", "Use radiobutton in place of combobox"))
x.append(param("Header image", "", "File", "Image in upper right part"))
x.append('          </Items>\n        </Params>\n        <Actions>\n          <Items>\n')
x.append('            <ActionItem Name="DesktopIcon" ActionType="Script" ConditionGroup="DesktopIcon">\n              <Params>\n                <Items>\n')
x.append(param("Script", iconscript, "Script", indent=18))
x.append(param("Uninstall Script", nounscript, "Script", indent=18))
x.append('                </Items>\n              </Params>\n              <ExecuteLocation>AfterPanelHide</ExecuteLocation>\n            </ActionItem>\n')
x.append('            <ActionItem Name="OpenConfig" ActionType="RunApplication" ConditionGroup="">\n              <Params>\n                <Items>\n')
x.append(param("Path to application", "%Base%\\" + CONFIG_EXE, "Template", "Path to the application", indent=18))
x.append(param("Parameters for application", "/setup", "String", "Command line parameters", indent=18))
x.append(param("Wait for exit", "NO", "Bool", "Wait for exit ", indent=18))
x.append(param("Don't run when silent install", "YES", "Bool", "Don't run when the installation is silent", indent=18))
x.append(param("Path to uninstall application", "", "Template", "Path to the application which should be executed when uninstall", indent=18))
x.append(param("Parameters for uninstall application", "", "String", "Command line parameters for uninstall app", indent=18))
x.append(param("Wait for exit on uninstall", "", "Bool", "Wait for exit on uninstall ", indent=18))
x.append('                </Items>\n              </Params>\n              <ExecuteLocation>AfterPanelHide</ExecuteLocation>\n            </ActionItem>\n')
x.append('          </Items>\n        </Actions>\n        <IncludedGroups>\n          <string>DesktopIcon</string>\n        </IncludedGroups>\n        <PanelName>Setup Complete</PanelName>\n        <WizardButtonsEnum>Finish</WizardButtonsEnum>\n      </SectionItem>\n')
x.append('    </Items>\n  </Sections>\n')
# Dependencies
x.append('''  <Dependencies>
    <Items>
      <DependencyItem>
        <Type>MediaPortal</Type>
        <Id />
        <MinVersion>
          <Major>1</Major>
          <Minor>34</Minor>
          <Build>0</Build>
          <Revision>0</Revision>
        </MinVersion>
        <MaxVersion>
          <Major>1</Major>
          <Minor>99</Minor>
          <Build>999</Build>
          <Revision>999</Revision>
        </MaxVersion>
        <WarnOnly>false</WarnOnly>
        <Message>requires MediaPortal 1.34 or newer (32 or 64-bit).</Message>
        <Name>MediaPortal</Name>
      </DependencyItem>
    </Items>
  </Dependencies>
  <PluginDependencies>
    <Items>
      <PluginDependencyItem AssemblyName="MyTorrents.dll">
        <CompatibleVersion>
          <Items>
            <CompatibleVersionItem>
              <MinRequiredVersion>1.1.6.27644</MinRequiredVersion>
              <DesignedForVersion>1.6.100.0</DesignedForVersion>
            </CompatibleVersionItem>
          </Items>
        </CompatibleVersion>
        <SubSystemsUsed>
          <Items />
        </SubSystemsUsed>
      </PluginDependencyItem>
    </Items>
  </PluginDependencies>
''')
desc = ("2026 update for MediaPortal 1.38 x64:\n"
        "- New in 1.3.66: duplicates check never offers episodes with different air dates, season folders or titles as copies; Configuration opens on your last tab; a short season pack no longer blocks the missing episodes\n"
        "- New in 1.3.65: Poor copies and Stereo-only movies are one list - Movies to improve - with the buttons in each row and a Year column; each movie looks for the best it can (a poor stereo copy gets a better copy, 5.1 if there is one); hidden ones have their own list to put back\n"
        "- New in 1.3.64: web page - pressing a button further down a list (Find 5.1, Not this one, Add...) brings you back to the same spot, not the top\n"
        "- New in 1.3.63: age tags (18+, R18) left out of movie names; Use it now works again when the earlier copy has gone; show-folder and movie styles without the rating (When finished)\n"
        "- New in 1.3.62: Drop folder: when you already have everything in something you dropped in, the Check-up says so and offers Delete it - you have it all; packs already looked at aren't gone through again at every start\n"
        "- New in 1.3.61: Duplicates never treats different episodes (S01E04 / S02E01, part (1) / (3)) as copies - a wrong match in MP-TVSeries is shown as one; the desktop icon step finds the MediaPortal folder more safely\n"
        "- New in 1.3.60: Duplicates one at a time - which copy to keep, with the reason in plain words and big buttons, plus Do the easy ones for me; Check-up: pick several lines at once; every list sorts by clicking a heading (Wanted movies gets Year and Added); the desktop icon stays where you put it; movies everywhere (not films)\n"
        "- New in 1.3.59: Check-up -> Find duplicates: the same episode or movie twice, with the better copy to keep suggested; the others are set aside (nothing deleted until you say); Check-up buttons under the explanation; movies MCM renamed with a year either side are no longer flagged\n"
        "- New in 1.3.58: menus never loop when MediaPortal closes; setup recommends qBittorrent, with help for BiglyBT, uTorrent, Deluge and Transmission; ticked search sites at the top; Browse buttons for the Movies and Collections folders\n"
        "- New in 1.3.57: the Auto grabber only takes copies whose name starts with the show's name (no more other shows with that word in the title); the daily log files really start each day; Check-up recognises movies the renamer renamed; the background helper starts again by itself after an update\n"
        "- New in 1.3.56: the drop folder never replaces or doubles up what you have (it stays there, the Check-up says why); copies dubbed into another language are skipped; qBittorrent is started if it isn't running; Check-up no longer flags movies you moved or renamed; no more duplicate-column errors; quieter log\n"
        "- New in 1.3.55: MyTorrents.log starts a new file each day and keeps a week (it used to grow for ever)\n"
        "- New in 1.3.54: a first install sets qBittorrent to start with Windows, minimised to the tray\n"
        "- New in 1.3.53: downloads stuck at 99%+ with no seeds are checked and copied to the library anyway (When finished > Nearly finished, or right-click > Use it now); nearly finished downloads (90%+) are never thrown away as stuck, and the stuck time counts from when data last came in; a first install has good search sites ticked already\n"
        "- New in 1.3.52: right-click Look it up on TheTVDB / TMDb on every list (and links on the web page); Downloads: Peers and Seeds:Peers; Check-up finds sample / advert videos to delete; site advert videos left out of downloads and over-long names shortened (fixes copies that failed); Poor copies read the picture size from the name (cover pictures inside files fooled it); Movies is the first tab, My shows is now Show list, Search sites after qBittorrent\n"
        "- New in 1.3.51: a download you delete before it finishes is looked for again with another copy (episodes and movies); web page: sort any column, Find box, choose columns, more columns on Downloads / My shows / Wanted movies, Delete and search again\n"
        "- New in 1.3.50: updates really go in now (the installer stops the background helper first); drop folder - put a movie or episode there and it's filed like a download; Downloads at once (the Auto grabber and Wanted movies wait when that many are going); dead movie links swapped for another copy; Trakt list items really come off the list and are never added twice; right-click Open location / double-click on the file lists\n"
        "- New in 1.3.49: Check-up (once a day: everything filed is in place, in the library, not twice, not a poor copy; NAS space, search sites - with Fix it buttons; Configuration tab, MediaPortal menu, web page); Poor copies - look for a better one (swapped in once it's safely there); movie name and folder styles like the TV ones (incl. like Media Center Master) with Rename my movies to match (collections too); new show folder style; Smallest movie knows x265 copies are smaller\n"
        "- New in 1.3.48: Smallest movie setting (Automatic: 720p 500 MB, 1080p 1.2 GB, 4K 3 GB) so tiny poor copies are skipped; Tick all / Untick all in Stereo-only movies, Collections you could make and New movies waiting; Movies tab: Favourites box shorter with its note beside it\n"
        "- New in 1.3.47: Open the web page works straight away (starts the background helper itself); fixed the look-up of shows already in MP-TVSeries (new episodes go into the show's own folder again, already-have checks work); one quiet MP-TVSeries scan at night (2-5 am, never while something plays) so its start-up and every-few-minutes scans can be turned off\n"
        "- New in 1.3.46: Collections you could make (movies that belong together, moved into your Collections folder after your OK, plus what's missing from them); stereo-only movies - look for a 5.1 copy (the stereo one is deleted once the new one is in, or kept, your choice); TV shows tab tidied - everyday settings on the tab, the rest under More settings; Uninstall MyTorrents (was Remove); boxes grow with the window; How MyTorrents works (automatic / manual) is one line on My shows\n"
        "- New in 1.3.45: keeps working when MediaPortal is closed (background helper, Background tab); web page for the laptop / desktop / phone (shows, wanted movies, not found, downloads, search, search sites, settings); sound preference (surround / stereo) for TV and movies; movie favourites; tabs renamed and reordered (TV shows, Movies, My shows...), opens on the next set-up step; new movies can be filed for you after your OK; removing a wanted movie also takes it off your Trakt list\n"
        "- New in 1.3.44: no more Media Center Master - new movies get a .nfo with their IMDb id and can be filed for you into Movies or Collections\\[collection] (TMDb through MP-TVSeries); new episodes are handed to MP-TVSeries to import straight away; Save what's not found yet as a text file\n"
        "- New in 1.3.43: Can't find list (episodes and movies looked for 14 days without a copy that fits, with why); OK on a show in the Auto grabber opens its menu; no more garbled pop-ups from background work; dead links try the next copy; 2 shows search at once (setting), shows you ask for never wait; movies on the NAS read in the background (Movies New often, the rest at start or when you ask - Read my movies again); bigger, resizable Configuration window; Movies tab (movie settings, Trakt list and the Wanted movies list)\n"
        "- New in 1.3.42: your own Trakt list (e.g. MCM) - movies go into Wanted movies and come off the list once safely in your movies folder, shows go into the Auto grabber; Torrent Search button on the Trakt plugin screens; 1080p when there is no good 720p copy (optional); fewer seeders over time when nothing turns up; stuck downloads replaced by another copy; Downloads split into Downloading and Seeding; Search again / try a different copy; Remove MyTorrents (and the programs its setup installed); already-have checks also look in the movies folder; whole seasons only for finished seasons with enough missing; Automatic or Manual at the top of the Auto grabber tab; movies are now called movies\n"
        "- New in 1.3.41: Wanted movies (add movies you want - downloaded when a good copy turns up); whole seasons first, then single episodes (with a season size limit and favourite season groups); what's missing for a show and a missing-episodes report for your whole library, with Grab the missing ones; Trakt lists - recommended, your watchlist, similar, trending, popular and most anticipated movies and shows; episodes you already have are not copied again from season packs\n"
        "- New in 1.3.40: Torrent Search from the left menu of TV Series and Moving Pictures; movie searches use their own quality (1080p) and size (2.5 GB); Add a new series (only new episodes / whole series / one season, with its folder and TVDB id file); Automatic or Manual mode (Manual for new installs)\n"
        "- New in 1.3.39: naming choices for finished episodes and new season folders (When finished tab, with a live example); the next episode's air date is right after a download; Airs sorts by calendar\n"
        "- New in 1.3.38: new episodes go into the show and season folders you already have; Check now, Search again and Remove (with Removed shows) in MyTorrents Configuration; click column headings to sort; clean-up after seeding; views in MediaPortal show again; Configuration tab renamed Torrent client\n"
        "- Works on 64-bit MediaPortal (.NET 4.7.2, AnyCPU)\n"
        "- New clients: BiglyBT (Web Remote) and qBittorrent\n"
        "- When finished: copies downloads into your MP-TVSeries / Moving Pictures folders, renamed and sorted; torrent keeps seeding, tagged MyTorrents-copied\n"
        "- Setup assistant: installs and connects qBittorrent or BiglyBT, Jackett (with sites) and FlareSolverr\n"
        "- Search sites tab: add your own torrent sites with just a search address\n"
        "- MyTorrents Configuration program + desktop icon (no need to start MediaPortal)\n"
        "- Downloads list in MyTorrents Configuration: progress, pause / resume, remove, columns you can drag into any order (opens on its own while MediaPortal is running)\n"
        "- About tab: version, credits and links\n"
        "- Own settings file (MyTorrents.settings.xml): change settings any time, also while MediaPortal runs\n"
        "- Get an episode now, just this once (any size) - in MediaPortal and on the new Shows tab\n"
        "- Size report (what size limit fits), smallest size to skip fakes, placeholder episodes skipped\n"
        "- Removing an Auto grabber download in MediaPortal offers to look for another copy; Auto grabber list A-Z\n"
        "- Auto grabber favourites: release groups / sites you like (e.g. MeGusta, PSA) are chosen first, or only them\n"
        "- Running shows in your TV folder are added to the Auto grabber by themselves (new episodes only; ended shows and shows you are far behind on are left alone; shows you remove stay removed)\n"
        "- Works with any skin: universal screens are added automatically to skins without MyTorrents screens\n"
        "- Jackett: address/API key in settings, 'all indexers' search, works with admin password\n"
        "- Magnet-link and redirect fixes, Deluge host fix, many crash fixes")
x.append(f'''  <GeneralInfo>
    <Name>MyTorrents</Name>
    <Id>{EXT_ID}</Id>
    <Author>Sohvaperuna, Edalex - 2026 update by Mark Willmer</Author>
    <HomePage />
    <ForumPage>https://forum.team-mediaportal.com/</ForumPage>
    <UpdateUrl />
    <Version>
      <Major>{VERSION[0]}</Major>
      <Minor>{VERSION[1]}</Minor>
      <Build>{VERSION[2]}</Build>
      <Revision>{VERSION[3]}</Revision>
    </Version>
    <ExtensionDescription>Control your torrent client (BiglyBT, qBittorrent, Deluge, uTorrent) from MediaPortal, search torrent sites and Jackett, and follow RSS feeds.</ExtensionDescription>
    <VersionDescription>{escape(desc)}</VersionDescription>
    <DevelopmentStatus>Stable</DevelopmentStatus>
    <OnlineLocation />
    <ReleaseDate>{now}</ReleaseDate>
    <Tags>torrent,rss,biglybt,qbittorrent,jackett</Tags>
    <PlatformCompatibility>AnyCPU</PlatformCompatibility>
    <Location>{escape(OUT)}</Location>
    <Params>
      <Items>
''')
x.append(param("Icon", ICON_IMG, "File", "The icon file of the package (jpg,png,bmp)", indent=8))
x.append(param("Online Icon", "", "String", "The icon file of the package stored online (jpg,png,bmp)", indent=8))
x.append(param("Configuration file", "%Base%\\" + CONFIG_EXE, "Template", "The file used to configure the extension.\n If have .exe extension the will be executed\n If have .dll extension used like MP plugin configuration", indent=8))
x.append(param("Online Screenshots", "", "String", "Online stored screenshot urls separated by ; ", indent=8))
x.append(param("Force to uninstall on update", "YES", "Bool", "Show dialog and force to uninstall previous version when updating an extension. Should only be disabled if you are using an NSIS/MSI installer.", indent=8))
x.append('      </Items>\n    </Params>\n  </GeneralInfo>\n  <UniqueFileList>\n    <Items>\n')
for it in unique:
    x.append(file_xml(it, 6))
x.append('    </Items>\n  </UniqueFileList>\n  <ProjectSettings>\n    <FolderGroups />\n    <ProjectFilename />\n    <UpdatePath1 />\n    <UpdatePath2 />\n    <UpdatePath3 />\n  </ProjectSettings>\n  <IsSkin>false</IsSkin>\n</PackageClass>\n')

xml = "".join(x)
os.makedirs(os.path.dirname(OUT), exist_ok=True)
with zipfile.ZipFile(OUT, "w", zipfile.ZIP_DEFLATED) as z:
    z.writestr("MediaPortalExtension.xml", xml.encode("utf-8"))
    for it in unique:
        z.write(it["path"], it["zip"].replace("\\", "/"))
print("wrote", OUT, os.path.getsize(OUT), "bytes,", len(unique), "files")
