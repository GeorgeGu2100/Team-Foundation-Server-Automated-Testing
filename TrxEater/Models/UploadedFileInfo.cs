using System;
using System.IO;

namespace TrxEater.Models
{
    /// <summary>
    /// 
    /// </summary>
    public class UploadedFileInfo
    {
        /// <summary>
        /// 
        /// </summary>
        public string SavePath { get; set; }
        
        /// <summary>
        /// 
        /// </summary>
        public string LocalFileName { get; set; }
        
        /// <summary>
        /// 
        /// </summary>
        public string GivenFileName { get; set; }
        
        /// <summary>
        /// 
        /// </summary>
        public string GivenMimeType { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public UploadedFileInfo FixGivenFileName()
        {
            GivenFileName = GivenFileName.Trim().Trim(new []{ '"', '\'' }).Trim(Path.GetInvalidFileNameChars());
            return this;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public UploadedFileInfo SafeRename()
        {
            var newFileName = DateTime.Now.ToString("s").Replace(':', '-') + "." + Guid.NewGuid().ToString().Substring(0, 4) + Path.GetExtension(GivenFileName);
            var newFilePath = Path.Combine(SavePath, newFileName);
            File.Move(LocalFileName, newFilePath);
            LocalFileName = newFilePath;
            return this;
        }
    }
}