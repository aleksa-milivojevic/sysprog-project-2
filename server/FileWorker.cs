using System;
using System.IO;
using LogUtil;

namespace FileUtil
{
    class FileWorker
    {
        private async Task<string> TrackFile(string fileName) {

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string targetDir = home + "/sysprog/testfiles";
            var files = Directory.EnumerateFiles(targetDir, fileName, SearchOption.AllDirectories);
            return files.FirstOrDefault();
        }

        public async Task<string> GetAvgWordLen(string fileName) {
            Console.WriteLine(fileName);
            if (fileName == "") {
                return "[Error] File not specified...";
            }
            var path = await TrackFile(fileName);
            if (path == null) {
                return "[Error] File not found";
            }

            int count = 0;
            int sum = 0;
            
            string text = File.ReadAllText(path);
            var words = text.Split(" ", System.StringSplitOptions.None);
            foreach(var word in words) {
                sum += word.Length;
                count += 1;
            }
            
            double avg = Math.Round((double)sum/(double)count, 2);
            return "" + avg;
        }
    }
}