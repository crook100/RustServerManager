using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RustServerManager
{
    public class Plugin
    {
        public string Name;
        public string DisplayName;
        public string Filepath;
        public string Description;
        public string Version;
        public string Author;
        public List<string> Permissions = new List<string>();
    }
}
