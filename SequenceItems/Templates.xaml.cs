using System.ComponentModel.Composition;
using System.Windows;

namespace NINA.Plugin.Phd2GuidingAssistant.SequenceItems {

    [Export(typeof(ResourceDictionary))]
    public partial class Templates : ResourceDictionary {

        public Templates() {
            InitializeComponent();
        }
    }
}
