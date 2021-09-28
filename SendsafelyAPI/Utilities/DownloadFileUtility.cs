using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using SendSafely.Exceptions;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SendSafely.Objects;

namespace SendSafely.Utilities
{
    class DownloadFileUtility
    {
        public static int BufferSize = 4096;
        
        private PackageInformation pkgInfo;
        private Directory directoryInfo;
        private ISendSafelyProgress progress;
        private Connection connection;
        private String downloadAPI;
        private String password;

        public DownloadFileUtility(Connection connection, PackageInformation pkgInfo, ISendSafelyProgress progress, String downloadAPI, String password)
        {
            this.pkgInfo = pkgInfo;
            this.progress = progress;
            this.connection = connection;
            this.downloadAPI = downloadAPI;
            this.password = password;
        }

        public DownloadFileUtility(Connection connection, Directory directory, PackageInformation pkgInfo, ISendSafelyProgress progress, String downloadAPI)
        {
            this.pkgInfo = pkgInfo;
            this.progress = progress;
            this.connection = connection;
            this.downloadAPI = downloadAPI;
            this.directoryInfo = directory;
        }
        private Object _progressLock = new Object();
        public FileInfo downloadFile(String fileId)
        {
            File fileToDownload = findFile(fileId);

            FileInfo newFile = createTempFile(fileToDownload);
            
            Endpoint p = createEndpoint(pkgInfo, fileId);
            
            string cachedChecksum = CryptUtility.pbkdf2(pkgInfo.KeyCode, pkgInfo.PackageCode, 1024);
            
            int partCount = fileToDownload.Parts;
            int finished = 0;
            MemoryStream[] partStreams = new MemoryStream[partCount];
            Parallel.For(1, partCount + 1, (i, state) =>
            {
                partStreams[i - 1] = new MemoryStream(3072000);
                DownloadSegment(partStreams[i-1], p, i, cachedChecksum);
                finished += 1;
                lock(_progressLock)
                {
                    progress.UpdateProgress($"Downloading", (finished / (double)partCount) * 100d);
                }
            });
            
            // Decrypt parts back into main file
            char[] cachedDecryptionKey = (pkgInfo.ServerSecret + pkgInfo.KeyCode).ToCharArray();
            using (FileStream decryptedFileStream = newFile.OpenWrite())
            {
                foreach (var s in partStreams)
                {
                    s.Seek(0, SeekOrigin.Begin);
                    CryptUtility.DecryptFile(decryptedFileStream, s, cachedDecryptionKey);
                }
            }
            return newFile;
        }

        private Endpoint createEndpoint(PackageInformation pkgInfo, String fileId)
        {
            Endpoint p;
            if (this.directoryInfo != null)
            {
                p = ConnectionStrings.Endpoints["downloadFileFromDirectory"].Clone();
                p.Path = p.Path.Replace("{packageId}", pkgInfo.PackageId);
                p.Path = p.Path.Replace("{fileId}", fileId);
                p.Path = p.Path.Replace("{directoryId}", directoryInfo.DirectoryId);
            } else
            {
                p = ConnectionStrings.Endpoints["downloadFile"].Clone();
                p.Path = p.Path.Replace("{packageId}", pkgInfo.PackageId);
                p.Path = p.Path.Replace("{fileId}", fileId);
            }

            return p;
        }
        

        private void DownloadSegment(Stream progressStream, Endpoint p, int part, string cachedChecksum)
        {
            DownloadFileRequest request = new DownloadFileRequest
            {
                Api = this.downloadAPI,
                Checksum = cachedChecksum,
                Part = part
            };
            
            if (this.password != null) {
                request.Password = this.password;
            }

            using (Stream objStream = connection.CallServer(p, request))
            {
                byte[] tmp = new byte[BufferSize];
                int l;
                while ((l = objStream.Read(tmp, 0, BufferSize)) != 0)
                {
                    progressStream.Write(tmp, 0, l);
                }
            }
        }

        private File findFile(String fileId)
        {
            foreach (File f in pkgInfo.Files)
            {
                if (f.FileId.Equals(fileId))
                {
                    return f;
                }
            }

            if (this.directoryInfo != null)
            { 
                foreach (FileResponse f in directoryInfo.Files)
                {
                    if (f.FileId.Equals(fileId))
                    {
                        return new File(f.FileId, f.FileName, f.FileSize, f.Parts);
                    }
                }
            }
            throw new FileDownloadException("Failed to find the file");
        }

        private FileInfo createTempFile(File file)
	    {
            return createTempFile(Guid.NewGuid().ToString());
	    }
        
        private FileInfo createTempFile(String fileName)
        {
            return new FileInfo(System.IO.Path.GetTempPath() + fileName);
        }
    }
}
