using System.ComponentModel.Composition;
using System.Windows;

namespace NINA.Plugin.Phd2GuidingAssistant {

    [Export(typeof(ResourceDictionary))]
    public partial class Options : ResourceDictionary {

        public Options() {
            InitializeComponent();
        }
    }
}
