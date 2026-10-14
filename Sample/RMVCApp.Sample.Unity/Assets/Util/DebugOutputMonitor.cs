#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Assets.Util {
    internal class DebugOutputMonitor : System.IO.TextWriter {
        public DebugOutputMonitor() { 
        }
        public override void WriteLine(string? message) {
            Debug.Log(message);
        }

        public override void Write(string? message) {
            Debug.Log(message);
        }

        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
    }
}
