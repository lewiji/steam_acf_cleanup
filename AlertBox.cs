using Godot;
using GodotOnReady.Attributes;
#pragma warning disable CS0649

namespace SatiStream;

public partial class AlertBox : WindowDialog {
   [OnReadyGet("%CloseButton")] Button _closeButton;

   [OnReady] void ConnectSignals() {
      _closeButton.Connect("pressed", this, nameof(OnClosed));
      GetCloseButton().Visible = false;
   }

   void OnClosed() {
      Hide();
   }
}
