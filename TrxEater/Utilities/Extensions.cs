using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using TrxEater.Models;

namespace TrxEater.Utilities
{
    public static class Extensions
    {
        public static async Task<List<UploadedFileInfo>> SaveFiles(this HttpRequestMessage request, string savePath)
        {
            // Check if the request contains multipart/form-data.
            if (!request.Content.IsMimeMultipartContent())
            {
                throw new HttpResponseException(request.CreateErrorResponse(HttpStatusCode.UnsupportedMediaType, "Request mime type is not multipart"));
            }

            if (!Directory.Exists(savePath))
            {
                Directory.CreateDirectory(savePath);
            }

            var provider = new MultipartFormDataStreamProvider(savePath);

            await request.Content.ReadAsMultipartAsync(provider);

            if (provider.FileData == null || !provider.FileData.Any())
            {
                return null;
            }

            var files = provider.FileData.Select(file => (new UploadedFileInfo
            {
                SavePath = savePath,
                LocalFileName = file.LocalFileName,
                GivenFileName = file.Headers.ContentDisposition.FileName,
                GivenMimeType = file.Headers.ContentType.MediaType
            }).FixGivenFileName().SafeRename()).ToList();

            return files;
        }
    }
}