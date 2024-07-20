using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RustServerManager
{
    public class Packet
    {
        public int Identifier;
        public string Message;
        public string Name;
        public string Stacktrace;
        public string Type;
    }
}
