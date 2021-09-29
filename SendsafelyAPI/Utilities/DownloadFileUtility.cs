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
        private Object _progressLock = new ();
        public FileInfo downloadFile(String fileId)
        {
            File fileToDownload = findFile(fileId);

            FileInfo newFile = createTempFile(fileToDownload);
            
            Endpoint p = createEndpoint(pkgInfo, fileId);
            
            string cachedChecksum = CryptUtility.pbkdf2(pkgInfo.KeyCode, pkgInfo.PackageCode, 1024);
            
            int partCount = fileToDownload.Parts;
            int downloadedParts = 0;
            MemoryStream[] partStreams = new MemoryStream[partCount];
            Parallel.For(1, partCount + 1, (i) =>
            {
                // Exact segment size
                int partStreamIndex = i - 1;
                partStreams[partStreamIndex] = new MemoryStream((int)PackageUtility.SEGMENT_SIZE);
                DownloadSegment(partStreams[partStreamIndex], p, i, cachedChecksum);
                lock(_progressLock)
                {
                    downloadedParts += 1;
                    progress.UpdateProgress($"Downloading Parts", (downloadedParts / (double)partCount) * 100d);
                }
            });
            lock(_progressLock)
            {
                progress.UpdateProgress($"Decrypting", 0d);
            }

            // Decrypt parts back into main file
            char[] cachedDecryptionKey = (pkgInfo.ServerSecret + pkgInfo.KeyCode).ToCharArray();
            int decryptedFiles = 0;
            MemoryStream[] decryptedStreams = new MemoryStream[partCount];
            Parallel.For(1, partCount + 1, (i) =>
            {
                int streamIndex = i - 1;
                decryptedStreams[streamIndex] = new MemoryStream((int)PackageUtility.SEGMENT_SIZE);
                partStreams[streamIndex].Seek(0, SeekOrigin.Begin);
                CryptUtility.DecryptStream(decryptedStreams[streamIndex], partStreams[streamIndex], cachedDecryptionKey);
                lock(_progressLock)
                {
                    decryptedFiles += 1;
                    progress.UpdateProgress($"Decrypting", decryptedFiles/(double)partCount * 100d);
                }
                partStreams[streamIndex].Dispose();
            });
            lock(_progressLock)
            {
                progress.UpdateProgress($"Decrypting", 100d);
            }

            using (FileStream decryptedFileStream = newFile.OpenWrite())
            {
                foreach (var s in decryptedStreams)
                {
                    s.Seek(0, SeekOrigin.Begin);
                    decryptedFileStream.Write(s.GetBuffer(), 0, (int)s.Length);
                    s.Dispose();
                }
            }
            return newFile;
        }

        public CoalesceStream downloadFileStream(String fileId)
        {
            File fileToDownload = findFile(fileId);
            Endpoint p = createEndpoint(pkgInfo, fileId);
            
            string cachedChecksum = CryptUtility.pbkdf2(pkgInfo.KeyCode, pkgInfo.PackageCode, 1024);
            
            int partCount = fileToDownload.Parts;
            int downloadedFiles = 0;
            MemoryStream[] partStreams = new MemoryStream[partCount];
            Parallel.For(1, partCount + 1, (i) =>
            {
                // Exact segment size
                int partStreamIndex = i - 1;
                partStreams[partStreamIndex] = new MemoryStream((int)PackageUtility.SEGMENT_SIZE);
                DownloadSegment(partStreams[partStreamIndex], p, i, cachedChecksum);
                lock(_progressLock)
                {
                    downloadedFiles += 1;
                    progress.UpdateProgress($"Downloading Parts", (downloadedFiles / (double)partCount) * 100d);
                }
            });
            lock(_progressLock)
            {
                progress.UpdateProgress($"Decrypting", 0d);
            }
            
            
            // Decrypt parts back into main file
            char[] cachedDecryptionKey = (pkgInfo.ServerSecret + pkgInfo.KeyCode).ToCharArray();
            int decryptedFiles = 0;
            MemoryStream[] decryptedStreams = new MemoryStream[partCount];
            long finalFileSize = 0L;
            Parallel.For(1, partCount + 1, (i) =>
            {
                int streamIndex = i - 1;
                decryptedStreams[streamIndex] = new MemoryStream((int)PackageUtility.SEGMENT_SIZE);
                partStreams[streamIndex].Seek(0, SeekOrigin.Begin);
                CryptUtility.DecryptStream(decryptedStreams[streamIndex], partStreams[streamIndex], cachedDecryptionKey);
                finalFileSize += decryptedStreams[streamIndex].Length;
                lock(_progressLock)
                {
                    decryptedFiles += 1;
                    progress.UpdateProgress($"Decrypting", decryptedFiles/(double)partCount * 100d);
                }
                partStreams[i-1].Dispose();
            });
            lock(_progressLock)
            {
                progress.UpdateProgress($"Decrypting", 100d);
            }

            CoalesceStream returnStream = new CoalesceStream(finalFileSize);
            foreach (var s in decryptedStreams)
            {
                s.Seek(0, SeekOrigin.Begin);
                // We know their block size isn't exceeding the max int
                returnStream.Write(s.GetBuffer(), 0, (int)s.Length);
                s.Dispose();
            }

            returnStream.Seek(0, SeekOrigin.Begin);
            return returnStream;
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
