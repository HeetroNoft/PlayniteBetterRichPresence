using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace BetterRichPresence
{
    public partial class BetterRichPresenceSettingsView : UserControl
    {
        public BetterRichPresenceSettingsView()
        {
            InitializeComponent();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}
