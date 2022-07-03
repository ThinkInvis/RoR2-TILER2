using System;
using System.Collections.Generic;
using System.Text;

namespace TILER2 {
    public static class AutoConfigPresetExtensions {
        public static void ApplyPreset(this AutoConfigContainer container, string name) {

        }
    }

    ///<summary>Use with an AutoConfigAttribute to attach metadata for a config preset.</summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
    public class AutoConfigPresetAttribute : Attribute {
        public readonly string presetName;
        public readonly object value;

        public AutoConfigPresetAttribute(string name, object value) {
            this.presetName = name;
            this.value = value;
        }
    }
}
