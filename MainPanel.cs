using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GodotOnReady.Attributes;

namespace SteamLibraryCleanup;
#pragma warning disable CS0649
// ReSharper disable UnusedMember.Local
// ReSharper disable FieldCanBeMadeReadOnly.Local
public partial class MainPanel : Panel {
   Dictionary<string, string> _acfInstallDirs;
   List<string> _acfPaths;
   List<string> _acfsWithMissingInstallDir;
   List<string> _gameFolderPaths;
   [OnReadyGet("%DetectBtn")] Button _detectButton;
   [OnReadyGet("%DetectedAcfList")] ItemList _detectedAcfItemList;
   [OnReadyGet("%DetectedFolderList")] ItemList _detectedFoldersItemList;
   [OnReadyGet("%DetectMissingFoldersPopup")] PopupPanel _popupPanel;
   [OnReadyGet("%DetectMissingFoldersPopup/%DetectionLog")] RichTextLabel _detectionLog;
   [OnReadyGet("%DetectMissingFoldersPopup/%ClosePopup")] Button _closeButton;
   [OnReadyGet("%DetectMissingFoldersPopup/%DeleteAcfs")] Button _deleteAcfs;
   [OnReadyGet("%AlertBox")] WindowDialog _alertBox;
   [OnReadyGet("%RefreshBtn")] Button _refreshButton;
   [OnReadyGet("%SteamPathInput")] LineEdit _steamPathInput;
   [OnReadyGet("%Verbose")] CheckBox _verbose;
   string _steamPath;
   string SteamPath {
      set {
         _steamPath = value;
         _steamPathInput.Text = _steamPath;
      }
   }

   [OnReady]
   void InitSteamPath() {
      SteamPath = OS.GetSystemDir(OS.SystemDir.Downloads).Replace("Downloads", ".steam/steam");
      _steamPathInput.Connect("text_changed", this, nameof(OnSteamPathInputChanged));
   }

   void OnSteamPathInputChanged(string newText) {
      _steamPath = newText;
   }

   [OnReady] void InitFileFolderLists() {
      _acfPaths = new List<string>();
      _gameFolderPaths = new List<string>();
      _acfInstallDirs = new Dictionary<string, string>();
      _acfsWithMissingInstallDir = new List<string>();

      _refreshButton.Connect("pressed", this, nameof(OnRefreshPressed));
      _deleteAcfs.Connect("pressed", this, nameof(OnDeleteAcfsPressed));
      _closeButton.Connect("pressed", this, nameof(OnClosePopup));

      if (_steamPath != null && !_steamPath.Empty())
         OnRefreshPressed();
      else
         _alertBox.PopupCentered();
   }

   [OnReady] void InitDetectSequence() {
      _detectButton.Connect("pressed", this, nameof(OnDetectPressed));
   }

   async void OnRefreshPressed() {
      if (_detectedAcfItemList.Items.Count > 0 || _detectedFoldersItemList.Items.Count > 0)
         await AnimateRefresh();

      PopulateAcfLists();
      PopulateGameFolderLists();
   }

   async Task AnimateRefresh() {
      _detectedAcfItemList.Clear();
      _detectedFoldersItemList.Clear();
      _detectedAcfItemList.Modulate = new Color("55ffffff");
      _detectedFoldersItemList.Modulate = new Color("55ffffff");
      await ToSignal(GetTree().CreateTimer(0.3f), "timeout");
      _detectedAcfItemList.Modulate = Colors.White;
      _detectedFoldersItemList.Modulate = Colors.White;
   }

   void OnClosePopup() {
      _popupPanel.Hide();
   }

   async void OnDetectPressed() {
      if (_acfPaths.Count <= 0) return;

      _detectionLog.Clear();
      await ToSignal(GetTree(), "idle_frame");
      _deleteAcfs.Disabled = true;
      _acfsWithMissingInstallDir.Clear();
      _acfInstallDirs.Clear();
      _popupPanel.PopupCentered();

      AddLogLine(BbBold("Detecting missing game folders..."));

      PopulateInstallDirs();
      DetectMissingInstallDirs();

      if (_acfsWithMissingInstallDir.Count == 0) {
         AddLogLine("---------");
         AddLogLine("No missing install dirs found. Nothing to do!");
         return;
      }

      _deleteAcfs.Disabled = false;
   }

   void OnDeleteAcfsPressed() {
      var dir = new Directory();

      foreach (var acfPath in _acfsWithMissingInstallDir) {
         if (!acfPath.EndsWith(".acf")) {
            AddLogLine($"{BbError()} {BbAcf(acfPath)} is not a .acf file.");
            continue;
         }
         var fullPath = GetFullAcfPath(acfPath);

         if (!dir.FileExists(fullPath))
            AddLogLine($"{BbError()} Couldn't open {acfPath} in ReadWrite mode for deletion.");
         else
            AddLogLine(dir.Remove(fullPath) == Error.Ok
               ? $"{BbSuccess()} deleted {BbAcf(acfPath)}"
               : $"{BbError()} couldn't delete file: {BbAcf(acfPath)}");
      }
      _acfsWithMissingInstallDir.Clear();

      _acfPaths.Clear();
      _gameFolderPaths.Clear();
      _acfInstallDirs.Clear();
      _deleteAcfs.Disabled = true;
   }

   void DetectMissingInstallDirs() {
      var dir = new Directory();

      foreach (var (acfPath, installDir) in _acfInstallDirs) {
         if (dir.Open($"{_steamPath}/steamapps/common/{installDir}") == Error.Ok) continue;
         _acfsWithMissingInstallDir.Add(acfPath);
         AddLogLine($"{BbMissing()} Found missing install dir: {BbInstallDir(installDir)} for .acf: {BbAcf(acfPath)}");
      }
   }

   void PopulateInstallDirs() {
      _acfInstallDirs.Clear();

      foreach (var acfPath in _acfPaths) {
         ScanAcf(acfPath);
      }
   }

   void AddLogLine(string message) {
      _detectionLog.AppendBbcode($"{message}");
      _detectionLog.Newline();
   }

   string GetFullAcfPath(string acfFile) {
      return $"{_steamPath}/steamapps/{acfFile}";
   }

   void ScanAcf(string acfPath) {
      var verbose = _verbose.Pressed;
      var file = new File();
      var found = false;

      if (file.Open($"{GetFullAcfPath(acfPath)}", File.ModeFlags.Read) != Error.Ok) {
         AddLogLine($"{BbError()} Couldn't open {BbAcf(acfPath)}");
         return;
      }

      while (file.GetPosition() < file.GetLen()) {
         var line = file.GetLine();

         if (line == null || !line.Contains("installdir", StringComparison.InvariantCultureIgnoreCase))
            continue;

         var installDir = line.Split("\"", StringSplitOptions.RemoveEmptyEntries)?.Last();

         if (installDir == null) {
            if (verbose) {
               AddLogLine($"{BbWarning()} Couldn't parse installdir key in acf {BbAcf(acfPath)}");
            }
            continue;
         }

         found = true;
         _acfInstallDirs.Add(acfPath, installDir);

         if (verbose) {
            AddLogLine($"{BbFound()} install dir: {BbInstallDir(installDir)} for acf: {BbAcf(acfPath)}");
         }
         break;
      }

      if (!found) {
         AddLogLine($"{BbError()}: No {BbCode("installdir")} keys found in {BbAcf(acfPath)}.");
      }
      file.Close();
   }

   string BbMissing() {
      return BbBold(BbColor("[Missing]", "gray"));
   }

   string BbWarning() {
      return BbYellow("[Warning]");
   }

   string BbFound() {
      return BbGreen("[Found]");
   }

   string BbInstallDir(string installDir) {
      return BbBold(BbCode(BbLime(installDir)));
   }

   string BbAcf(string acf) {
      return BbBold(BbCode(BbTeal(acf)));
   }

   string BbError() {
      return BbBold(BbRed("[Error]"));
   }

   string BbSuccess() {
      return BbBold(BbGreen("[Success]"));
   }

   string BbBold(string input) {
      return $"[b]{input}[/b]";
   }

   string BbCode(string input) {
      return $"[code]{input}[/code]";
   }

   string BbColor(string input, string color) {
      return $"[color={color}]{input}[/color]";
   }

   string BbGreen(string input) {
      return BbColor(input, "greem");
   }

   string BbLime(string input) {
      return BbColor(input, "lime");
   }

   string BbTeal(string input) {
      return BbColor(input, "teal");
   }

   string BbPurple(string input) {
      return BbColor(input, "purple");
   }

   string BbYellow(string input) {
      return BbColor(input, "yellow");
   }

   string BbSilver(string input) {
      return BbColor(input, "silver");
   }

   string BbRed(string input) {
      return BbColor(input, "red");
   }

   void PopulateGameFolderLists() {
      _gameFolderPaths.Clear();
      _detectedFoldersItemList.Clear();

      _gameFolderPaths.AddRange(ListDir(SearchType.FoldersOnly, "steamapps/common"));

      foreach (var path in _gameFolderPaths) {
         _detectedFoldersItemList.AddItem(path);
      }
   }

   void PopulateAcfLists() {
      _acfPaths.Clear();
      _detectedAcfItemList.Clear();
      _acfPaths.AddRange(ListDir(SearchType.FilesOnly, "steamapps", "acf"));

      foreach (var acfPath in _acfPaths) {
         _detectedAcfItemList.AddItem(acfPath.GetFile());
      }
   }

   IEnumerable<string> ListDir(SearchType searchType, string subdir, string fileExtension = "") {
      var dir = new Directory();
      var pathList = new List<string>();

      if (dir.Open($"{_steamPath}/{subdir}") != Error.Ok) {
         _alertBox.PopupCentered();
         GD.PrintErr($"Failed to open dir {_steamPath}/{subdir}");
         return pathList;
      }

      dir.ListDirBegin(true);
      var fileName = dir.GetNext();

      while (fileName != "") {
         // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
         switch (searchType) {
            case SearchType.FoldersOnly when dir.CurrentIsDir():
            case SearchType.FilesOnly when !dir.CurrentIsDir() && fileExtension == "":
            case SearchType.FilesOnly when !dir.CurrentIsDir() && fileName.Extension() == fileExtension:
               pathList.Add(fileName);
               break;
         }
         fileName = dir.GetNext();
      }
      dir.ListDirEnd();

      return pathList;
   }

   enum SearchType { FoldersOnly, FilesOnly }
}