using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;

namespace UpdateHelper
{
    class Program
    {
        static void Main(string[] args)
        {
            string path = args[0];

            string newPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "IRSpeedy");
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path)))
            {
                try
                {
                    process.Kill();
                }
                catch { }
            }
            if (File.Exists(path))
            {
                System.Threading.Thread.Sleep(1000);
                File.Delete(path);
            }
            string source = newPath + "\\IRSpeedyVPN.exe";            
            File.Move(source, path);
            File.Delete(source);
            Process.Start(path);
        }
    }
}
